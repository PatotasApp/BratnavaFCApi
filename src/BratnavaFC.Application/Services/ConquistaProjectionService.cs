using BratnavaFC.Application.Abstractions;
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

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.PlayerMatchStatContributions.Where(x => x.GroupId == groupId).ExecuteDeleteAsync(ct);

        if (contributions.Count > 0)
            await _db.PlayerMatchStatContributions.AddRangeAsync(contributions, ct);

        await ReplaceProjectionsAsync(groupId, contributions, ct);
        await transaction.CommitAsync(ct);
    }

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
        await SealPastSeasonsAsync(groupId, projections, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task SealPastSeasonsAsync(Guid groupId,
        IReadOnlyCollection<PlayerStatProjectionEntity> projections, CancellationToken ct)
    {
        var currentSeason = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BrazilTz).Year;
        var alreadySealed = await _db.SeasonTitles.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .Select(x => x.Season)
            .Distinct()
            .ToListAsync(ct);

        foreach (var seasonGroup in projections
                     .Where(x => x.Season > 0 && x.Season < currentSeason && !alreadySealed.Contains(x.Season))
                     .GroupBy(x => x.Season))
        {
            var players = seasonGroup.ToList();
            if (players.Count < 4) continue;

            Award(players, "Gols", "Artilheiro", "⚽", x => x.Goals, x => x.Games >= 3);
            Award(players, "Assistencias", "Rei das Assistências", "🅰️", x => x.Assists, x => x.Games >= 3);
            Award(players, "MVPs", "Rei dos MVPs", "🏅", x => x.Mvps, x => x.Games >= 3);
            Award(players, "Presenca", "Mais Presente", "🎽", x => x.Games, x => x.Games >= 3);
            Award(players, "Aproveitamento", "Melhor Aproveitamento", "📈", x => x.WinRatePct, x => x.Games >= 5);
        }

        void Award(List<PlayerStatProjectionEntity> players, string category,
            string name, string icon, Func<PlayerStatProjectionEntity, int> value,
            Func<PlayerStatProjectionEntity, bool> eligible)
        {
            var valueGroups = players.Where(x => eligible(x) && value(x) > 0)
                .GroupBy(value)
                .OrderByDescending(x => x.Key)
                .Take(3)
                .ToList();

            for (var i = 0; i < valueGroups.Count; i++)
                foreach (var player in valueGroups[i])
                    _db.SeasonTitles.Add(new SeasonTitleEntity(
                        groupId, player.PlayerId, player.Season, category,
                        name, icon, i + 1, value(player)));
        }
    }

    private static TimeZoneInfo ResolveBrazilTz()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }
}
