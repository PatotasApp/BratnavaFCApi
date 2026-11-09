using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Materializa as estatísticas derivadas. Partidas continuam sendo a fonte da verdade;
/// uma reconstrução pode ser executada quantas vezes for necessário sem duplicar dados.
/// </summary>
public sealed class ConquistaProjectionService : IConquistaProjectionService
{
    private readonly AppDbContext _db;
    private static readonly TimeZoneInfo BrazilTz = ResolveBrazilTz();

    public ConquistaProjectionService(AppDbContext db) => _db = db;

    public async Task EnsureGroupProjectedAsync(Guid groupId, CancellationToken ct = default)
    {
        if (await _db.PlayerStatProjections.AsNoTracking().AnyAsync(x => x.GroupId == groupId, ct))
            return;

        if (await _db.Matches.AsNoTracking().AnyAsync(
                x => x.GroupId == groupId && x.Status == MatchStatus.Finalized, ct))
            await RebuildGroupAsync(groupId, ct);
    }

    public async Task ProjectMatchAsync(Guid groupId, Guid matchId, CancellationToken ct = default)
    {
        var match = await _db.Matches.AsNoTracking().AsSplitQuery()
            .Where(m => m.GroupId == groupId && m.Id == matchId && m.Status == MatchStatus.Finalized)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .SingleOrDefaultAsync(ct);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.PlayerMatchStatContributions
            .Where(x => x.GroupId == groupId && x.MatchId == matchId)
            .ExecuteDeleteAsync(ct);

        if (match is not null)
        {
            var matchRows = BuildContributions([match]);
            if (matchRows.Count > 0)
                await _db.PlayerMatchStatContributions.AddRangeAsync(matchRows, ct);
            await _db.SaveChangesAsync(ct);
        }

        var contributions = await _db.PlayerMatchStatContributions.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .ToListAsync(ct);
        await ReplaceProjectionsAsync(groupId, contributions, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RebuildGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        var matches = await _db.Matches.AsNoTracking().AsSplitQuery()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .OrderBy(m => m.PlayedAt)
            .ToListAsync(ct);

        var contributions = BuildContributions(matches);

        // Campeonatos finalizados entram conforme as regras de cada um (Fase 3).
        var units = await new ChampionshipStatsSource(_db).GetUnitsAsync(groupId, ct);
        contributions.AddRange(BuildChampionshipContributions(units));

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.PlayerMatchStatContributions.Where(x => x.GroupId == groupId).ExecuteDeleteAsync(ct);

        if (contributions.Count > 0)
            await _db.PlayerMatchStatContributions.AddRangeAsync(contributions, ct);

        await ReplaceProjectionsAsync(groupId, contributions, ct);
        await transaction.CommitAsync(ct);
    }

    private static IEnumerable<PlayerMatchStatContributionEntity> BuildChampionshipContributions(IEnumerable<ChampionshipUnit> championshipUnits)
    {
        foreach (var championshipUnit in championshipUnits)
        {
            var unit = championshipUnit.Stat;
            var season = TimeZoneInfo.ConvertTime(unit.PlayedAt, BrazilTz).Year;
            foreach (var side in unit.Sides)
            {
                foreach (var p in side.Players)
                {
                    var goals = unit.Goals.Count(g => !g.IsOwnGoal && g.ScorerPlayerId == p.PlayerId);
                    var assists = unit.Goals.Count(g => !g.IsOwnGoal && g.AssistPlayerId == p.PlayerId);
                    var ownGoals = unit.Goals.Count(g => g.IsOwnGoal && g.ScorerPlayerId == p.PlayerId);

                    // Jogo separado: feitos do próprio jogo. Consolidado: feitos já apurados por jogo.
                    var feats = p.Feats ?? new StatFeats(
                        goals == 3 ? 1 : 0, goals == 4 ? 1 : 0, goals >= 5 ? 1 : 0,
                        goals > 0 && assists > 0 ? 1 : 0, assists >= 3 ? 1 : 0,
                        p.IsGoalkeeper && side.GoalsAgainst == 0 ? 1 : 0);

                    yield return PlayerMatchStatContributionEntity.ForChampionship(
                        championshipUnit.AnchorMatchId, unit.GroupId, p.PlayerId, season, unit.PlayedAt, ResultCode(p.Result),
                        p.IsGoalkeeper, goals, assists, p.IsMvp ? 1 : 0, ownGoals,
                        feats.HatTricks, feats.Pokers, feats.FiveGoalGames, feats.GoalAndAssistGames,
                        feats.ThreeAssistGames, feats.CleanSheets);
                }
            }
        }
    }

    private static string ResultCode(StatResult result) => result switch
    {
        StatResult.Win => "W",
        StatResult.Draw => "D",
        StatResult.Loss => "L",
        _ => "N"
    };

    private static List<PlayerMatchStatContributionEntity> BuildContributions(
        IEnumerable<MatchEntity> matches)
    {
        var contributions = new List<PlayerMatchStatContributionEntity>();
        foreach (var match in matches)
        {
            var season = TimeZoneInfo.ConvertTime(match.PlayedAt, BrazilTz).Year;
            var matchPlayers = match.Players.ToDictionary(p => p.Id);
            var goals = new Dictionary<Guid, int>();
            var assists = new Dictionary<Guid, int>();
            var ownGoals = new Dictionary<Guid, int>();

            foreach (var goal in match.Goals)
            {
                if (!matchPlayers.TryGetValue(goal.ScorerMatchPlayerId, out var scorer)) continue;
                if (goal.IsOwnGoal)
                    ownGoals[scorer.PlayerId] = ownGoals.GetValueOrDefault(scorer.PlayerId) + 1;
                else
                    goals[scorer.PlayerId] = goals.GetValueOrDefault(scorer.PlayerId) + 1;

                if (!goal.IsOwnGoal && goal.AssistMatchPlayerId is Guid assistId &&
                    matchPlayers.TryGetValue(assistId, out var assist))
                    assists[assist.PlayerId] = assists.GetValueOrDefault(assist.PlayerId) + 1;
            }

            var a = match.TeamAGoals ?? 0;
            var b = match.TeamBGoals ?? 0;
            foreach (var mp in match.Players)
            {
                if (mp.DidNotPlay || mp.Team is not (1 or 2)) continue;
                var ownScore = mp.Team == 1 ? a : b;
                var opponentScore = mp.Team == 1 ? b : a;
                var result = ownScore > opponentScore ? "W" : ownScore < opponentScore ? "L" : "D";
                contributions.Add(new PlayerMatchStatContributionEntity(
                    match.Id, match.GroupId, mp.PlayerId, season, match.PlayedAt, result,
                    mp.IsGoalkeeper, goals.GetValueOrDefault(mp.PlayerId),
                    assists.GetValueOrDefault(mp.PlayerId), mp.IsMvp == true ? 1 : 0,
                    ownGoals.GetValueOrDefault(mp.PlayerId), opponentScore));
            }
        }

        return contributions;
    }

    private async Task ReplaceProjectionsAsync(Guid groupId,
        IReadOnlyCollection<PlayerMatchStatContributionEntity> contributions, CancellationToken ct)
    {
        await _db.PlayerStatProjections.Where(x => x.GroupId == groupId).ExecuteDeleteAsync(ct);

        var projections = new List<PlayerStatProjectionEntity>();
        foreach (var playerRows in contributions.GroupBy(x => x.PlayerId))
        {
            projections.Add(new PlayerStatProjectionEntity(groupId, playerRows.Key, 0, playerRows));
            projections.AddRange(playerRows.GroupBy(x => x.Season)
                .Select(year => new PlayerStatProjectionEntity(groupId, playerRows.Key, year.Key, year)));
        }
        if (projections.Count > 0)
            await _db.PlayerStatProjections.AddRangeAsync(projections, ct);

        await _db.SaveChangesAsync(ct);
        await SealTitlesAsync(groupId, contributions, DateTime.UtcNow, ct);
        await _db.SaveChangesAsync(ct);
    }

    // ── Títulos (docs/temporadas/TEMPORADAS.md) ──────────────────────────────

    public async Task<IReadOnlyList<Guid>> SealTitlesAsync(Guid groupId, DateTime? now = null, CancellationToken ct = default)
    {
        var sealedNow = await SealTitlesAsync(groupId, null, now ?? DateTime.UtcNow, ct);
        await _db.SaveChangesAsync(ct);
        return sealedNow;
    }

    public async Task<IReadOnlyList<Guid>> ResealYearsAsync(Guid groupId, IEnumerable<int> years, DateTime? now = null, CancellationToken ct = default)
    {
        var list = years.Distinct().ToList();
        if (list.Count > 0)
            await _db.SeasonTitles.Where(x => x.GroupId == groupId && list.Contains(x.Season)).ExecuteDeleteAsync(ct);
        return await SealTitlesAsync(groupId, now, ct);
    }

    /// <summary>
    /// Temporada regular encerrada (fim já passou) ganha os títulos dela. O ano completo
    /// (todas fechadas e já existe temporada de um ano seguinte) com 2+ temporadas regulares
    /// ganha os títulos do ano; com uma só, o da temporada já é o do ano.
    /// </summary>
    private async Task<List<Guid>> SealTitlesAsync(Guid groupId,
        IReadOnlyCollection<PlayerMatchStatContributionEntity>? contributions, DateTime now, CancellationToken ct)
    {
        var seasons = await _db.Seasons.AsNoTracking()
            .Where(s => s.GroupId == groupId).OrderBy(s => s.StartsAt).ToListAsync(ct);
        var existing = await _db.SeasonTitles.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .Select(x => new { x.Scope, x.SeasonId, x.Season })
            .Distinct()
            .ToListAsync(ct);
        var sealedSeasons = existing.Where(x => x.Scope == SeasonTitleEntity.ScopeSeason).Select(x => x.SeasonId).ToHashSet();
        var sealedYears = existing.Where(x => x.Scope == SeasonTitleEntity.ScopeYear).Select(x => x.Season).ToHashSet();
        var sealedNow = new List<Guid>();

        bool Ended(SeasonEntity s) => s.EndsAt is { } end && end <= now;

        var pendingSeasons = seasons.Where(s => Ended(s) && !s.IsPreSeason && !sealedSeasons.Contains(s.Id)).ToList();
        var pendingYears = seasons.Select(s => s.Year).Distinct().Where(y => !sealedYears.Contains(y))
            .Select(year => seasons.Where(s => s.Year == year).ToList())
            .Where(ofYear => ofYear.Count(s => !s.IsPreSeason) >= 2 && ofYear.All(Ended)
                             && seasons.Any(s => s.Year > ofYear[0].Year))
            .ToList();
        if (pendingSeasons.Count == 0 && pendingYears.Count == 0) return sealedNow;

        contributions ??= await _db.PlayerMatchStatContributions.AsNoTracking()
            .Where(x => x.GroupId == groupId).ToListAsync(ct);

        foreach (var season in pendingSeasons)
        {
            var rows = contributions.Where(c => season.Contains(c.PlayedAt));
            if (Award(groupId, season.Year, season.Id, SeasonTitleEntity.ScopeSeason, rows))
                sealedNow.Add(season.Id);
        }

        foreach (var ofYear in pendingYears)
        {
            var regular = ofYear.Where(s => !s.IsPreSeason).ToList();
            var rows = contributions.Where(c => regular.Any(s => s.Contains(c.PlayedAt)));
            Award(groupId, ofYear[0].Year, null, SeasonTitleEntity.ScopeYear, rows);
        }

        return sealedNow;
    }

    /// <summary>Pódio (até 3 valores) de cada categoria. Com menos de 4 jogadores, não há título.</summary>
    private bool Award(Guid groupId, int year, Guid? seasonId, short scope,
        IEnumerable<PlayerMatchStatContributionEntity> rows)
    {
        var players = rows.GroupBy(x => x.PlayerId)
            .Select(g => new PlayerStatProjectionEntity(groupId, g.Key, year, g))
            .ToList();
        if (players.Count < 4) return false;

        foreach (var category in TitleCategories)
        {
            var valueGroups = players.Where(x => x.Games >= category.MinGames && category.Value(x) > 0)
                .GroupBy(category.Value)
                .OrderByDescending(x => x.Key)
                .Take(3)
                .ToList();

            for (var i = 0; i < valueGroups.Count; i++)
                foreach (var player in valueGroups[i])
                    _db.SeasonTitles.Add(new SeasonTitleEntity(
                        groupId, player.PlayerId, year, category.Category,
                        category.Name, category.Icon, i + 1, category.Value(player), seasonId, scope));
        }
        return true;
    }

    /// <summary>Categorias dos títulos (as mesmas da retrospectiva da temporada).</summary>
    public static readonly IReadOnlyList<TitleCategory> TitleCategories =
    [
        new("Gols", "Artilheiro", "Gols", "⚽", x => x.Goals, 3),
        new("Assistencias", "Rei das Assistências", "Assistências", "🅰️", x => x.Assists, 3),
        new("MVPs", "Rei dos MVPs", "MVPs", "🏅", x => x.Mvps, 3),
        new("Presenca", "Mais Presente", "Presença", "🎽", x => x.Games, 3),
        new("Aproveitamento", "Melhor Aproveitamento", "Aproveitamento (%)", "📈", x => x.WinRatePct, 5),
    ];

    /// <summary>Categoria de título: <paramref name="Name"/> no título, <paramref name="RankingName"/> na classificação.</summary>
    public sealed record TitleCategory(
        string Category, string Name, string RankingName, string Icon,
        Func<PlayerStatProjectionEntity, int> Value, int MinGames);

    private static TimeZoneInfo ResolveBrazilTz()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }
}
