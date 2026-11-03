using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.TeamBuilder;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

/// <summary>
/// Monte seu Time. Regras (iguais às Estatísticas):
/// - Presença pelo time escalado: Team 1/2 e !DidNotPlay. A resposta ao convite não importa
///   (partidas importadas não têm InviteResponse).
/// - "Juntos" = todos os selecionados no MESMO time. Em campo, mas em times opostos, não conta.
/// - Aproveitamento: V=3, E=1, em % dos pontos possíveis. Resultado pelo placar.
/// - Gols = só gols registrados com autor; gol contra não conta para ninguém e a assistência
///   num gol contra também não.
/// </summary>
public sealed class TeamBuilderService : ITeamBuilderService
{
    private const int MinOpponentGames = 3;
    private const int MinScoredGames    = 3;

    private static readonly TimeZoneInfo BrazilTz = ResolveBrazilTz();

    private readonly AppDbContext _db;

    public TeamBuilderService(AppDbContext db) => _db = db;

    public Task<Result<TeamBuilderStatsDto>> GetStatsAsync(
        Guid groupId, List<Guid> playerIds, CancellationToken ct) =>
        GetStatsAsync(groupId, new TeamBuilderRequestDto { PlayerIds = playerIds }, ct);

    public async Task<Result<TeamBuilderPlayersDto>> GetPlayersAsync(
        Guid groupId, TeamBuilderFilterDto filter, CancellationToken ct)
    {
        var all     = await LoadMatchesAsync(groupId, filter.IncludeChampionship, ct);
        var matches = ApplyFilter(all, filter);
        var names   = await ShortNamesAsync(groupId, ct);

        var games = new Dictionary<Guid, int>();
        foreach (var m in matches)
            foreach (var pid in m.TeamOf.Keys)
                games[pid] = games.GetValueOrDefault(pid) + 1;

        var players = await _db.Players
            .Where(p => p.GroupId == groupId && !p.IsGuest && p.Status == Status.Active)
            .Select(p => new { p.Id, p.Name, p.IsGoalkeeper })
            .ToListAsync(ct);

        return Result<TeamBuilderPlayersDto>.Ok(new TeamBuilderPlayersDto
        {
            PeriodMatches = matches.Count,
            Seasons       = all.Select(m => m.LocalDate.Year).Distinct().OrderByDescending(y => y).ToList(),
            Players       = players
                .Select(p => new TeamBuilderPlayerItemDto
                {
                    Id           = p.Id,
                    Name         = p.Name,
                    ShortName    = names.GetValueOrDefault(p.Id, p.Name),
                    IsGoalkeeper = p.IsGoalkeeper,
                    Games        = games.GetValueOrDefault(p.Id),
                })
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
        });
    }

    public async Task<Result<TeamBuilderStatsDto>> GetStatsAsync(
        Guid groupId, TeamBuilderRequestDto request, CancellationToken ct)
    {
        var playerIds = request.PlayerIds;
        if (playerIds is null || playerIds.Count < 2 || playerIds.Count > 5)
            return Result<TeamBuilderStatsDto>.Fail(
                "Selecione entre 2 e 5 jogadores.", ResultStatus.BadRequest);

        playerIds = playerIds.Distinct().ToList();

        var validCount = await _db.Players
            .CountAsync(p => playerIds.Contains(p.Id) && p.GroupId == groupId, ct);

        if (validCount != playerIds.Count)
            return Result<TeamBuilderStatsDto>.Fail(
                "Um ou mais jogadores não pertencem a este grupo.", ResultStatus.BadRequest);

        var names = await ShortNamesAsync(groupId, ct);
        var fullNames = await _db.Players
            .Where(p => p.GroupId == groupId)
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        string Full(Guid id)  => fullNames.GetValueOrDefault(id, "");
        string Short(Guid id) => names.GetValueOrDefault(id, Full(id));

        // Mantém a ordem em que foram escolhidos.
        var dbPlayers = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.IsGoalkeeper })
            .ToListAsync(ct);
        var players = playerIds
            .Select(id => dbPlayers.First(p => p.Id == id))
            .Select(p => new SelectedPlayerDto { Id = p.Id, Name = p.Name, ShortName = Short(p.Id), IsGoalkeeper = p.IsGoalkeeper })
            .ToList();

        var matches = ApplyFilter(await LoadMatchesAsync(groupId, request.IncludeChampionship, ct), request);
        var sel     = playerIds.ToHashSet();

        // Aproveitamento "geral" de cada um: todos os jogos dele no recorte.
        var overall = playerIds.ToDictionary(id => id, id =>
        {
            int w = 0, d = 0, n = 0;
            foreach (var m in matches)
            {
                if (!m.TeamOf.TryGetValue(id, out var side)) continue;
                n++;
                var r = m.ResultFor(side);
                if (r > 0) w++; else if (r == 0) d++;
            }
            return (Games: n, Pct: PointsPct(w, d, n));
        });

        var breakdown = players.ToDictionary(p => p.Id, p => new PlayerBreakdownDto
        {
            Id = p.Id, Name = p.Name, ShortName = p.ShortName, IsGoalkeeper = p.IsGoalkeeper,
            OverallGames = overall[p.Id].Games, OverallPointsPct = overall[p.Id].Pct,
        });

        var allOnPitch = matches.Where(m => sel.All(m.TeamOf.ContainsKey)).ToList();
        var together   = allOnPitch
            .Where(m => playerIds.Select(id => m.TeamOf[id]).Distinct().Count() == 1)
            .ToList(); // já em ordem cronológica

        var dto = new TeamBuilderStatsDto
        {
            Players                    = players,
            PeriodMatches              = matches.Count,
            OppositeTeamsMatches       = allOnPitch.Count - together.Count,
            AverageIndividualPointsPct = RoundPct(playerIds.Average(id => (double)overall[id].Pct)),
            PlayerBreakdown            = playerIds.Select(id => breakdown[id]).ToList(),
        };

        if (together.Count == 0)
        {
            dto.NeverPlayedTogether = true;
            return Result<TeamBuilderStatsDto>.Ok(dto);
        }

        var history    = new List<FormationMatchDto>(together.Count);
        var pairs      = new Dictionary<(Guid Assister, Guid Scorer), int>();
        var opponents  = new Dictionary<Guid, int[]>(); // [V, E, D]
        var scoredWins = new Dictionary<Guid, (int Wins, int Games)>();
        int teamGoals = 0, ourGoals = 0;

        foreach (var m in together)
        {
            var side   = m.TeamOf[playerIds[0]];
            var result = m.ResultFor(side);
            var item   = m.ToFormationMatch(side);
            history.Add(item);

            dto.GoalsScored   += item.GoalsFor;
            dto.GoalsConceded += item.GoalsAgainst;
            if (result > 0) dto.Wins++; else if (result == 0) dto.Draws++; else dto.Losses++;

            var scorers = new HashSet<Guid>();
            foreach (var g in m.Goals.Where(g => g.ForSide == side))
            {
                teamGoals++;
                if (g.IsOwnGoal || g.Scorer is not { } scorer) continue;
                if (!sel.Contains(scorer)) continue;

                ourGoals++;
                breakdown[scorer].Goals++;
                scorers.Add(scorer);
                if (g.Assist is { } assist && sel.Contains(assist))
                    pairs[(assist, scorer)] = pairs.GetValueOrDefault((assist, scorer)) + 1;
            }
            // Assistência conta para o jogador mesmo quando o gol é de quem não está na seleção.
            foreach (var g in m.Goals.Where(g => g.ForSide == side && !g.IsOwnGoal))
                if (g.Assist is { } assist && sel.Contains(assist))
                    breakdown[assist].Assists++;

            foreach (var s in scorers)
            {
                var acc = scoredWins.GetValueOrDefault(s);
                scoredWins[s] = (acc.Wins + (result > 0 ? 1 : 0), acc.Games + 1);
            }

            foreach (var id in playerIds)
            {
                var b = breakdown[id];
                b.Games++;
                if (result > 0) b.Wins++; else if (result == 0) b.Draws++; else b.Losses++;
                if (m.Mvps.Contains(id)) b.Mvps++;
            }

            foreach (var (opp, oppSide) in m.TeamOf)
            {
                if (oppSide == side) continue;
                if (!opponents.TryGetValue(opp, out var rec)) opponents[opp] = rec = new int[3];
                rec[result > 0 ? 0 : result == 0 ? 1 : 2]++;
            }
        }

        foreach (var b in breakdown.Values)
            b.WinRate = b.Games > 0 ? b.Wins / (double)b.Games : 0.0;

        var n = together.Count;
        dto.TotalMatches         = n;
        dto.GoalsScoredByPlayers = ourGoals;
        dto.TeamRegisteredGoals  = teamGoals;
        dto.GoalDifference       = dto.GoalsScored - dto.GoalsConceded;
        dto.GoalsPerMatch        = Math.Round(dto.GoalsScored / (double)n, 2);
        dto.PointsPct            = PointsPct(dto.Wins, dto.Draws, n);

        dto.BiggestWin = history.Where(h => h.Result > 0)
            .OrderByDescending(h => h.GoalsFor - h.GoalsAgainst).ThenByDescending(h => h.GoalsFor).ThenByDescending(h => h.PlayedAt)
            .FirstOrDefault();
        dto.WorstLoss = history.Where(h => h.Result < 0)
            .OrderBy(h => h.GoalsFor - h.GoalsAgainst).ThenBy(h => h.GoalsFor).ThenByDescending(h => h.PlayedAt)
            .FirstOrDefault();
        dto.LastMatch = history[^1];

        var streak = 0;
        for (var i = history.Count - 1; i >= 0 && history[i].Result == dto.LastMatch.Result; i--) streak++;
        dto.CurrentStreak = new StreakDto { Result = dto.LastMatch.Result, Count = streak };

        var run = 0;
        foreach (var h in history)
        {
            run = h.Result > 0 ? run + 1 : 0;
            dto.BestWinStreak = Math.Max(dto.BestWinStreak, run);
        }

        var opps = opponents
            .Where(o => o.Value.Sum() >= MinOpponentGames)
            .Select(o => new OpponentRecordDto
            {
                PlayerId = o.Key, Name = Full(o.Key), ShortName = Short(o.Key),
                Wins = o.Value[0], Draws = o.Value[1], Losses = o.Value[2], Games = o.Value.Sum(),
                PointsPct = PointsPct(o.Value[0], o.Value[1], o.Value.Sum()),
            })
            .ToList();
        dto.Nemesis = opps.Where(o => o.Losses > o.Wins)
            .OrderBy(o => o.PointsPct).ThenByDescending(o => o.Losses).ThenByDescending(o => o.Games).ThenBy(o => o.Name)
            .FirstOrDefault();
        dto.FavoriteVictim = opps.Where(o => o.Wins > o.Losses)
            .OrderByDescending(o => o.PointsPct).ThenByDescending(o => o.Wins).ThenByDescending(o => o.Games).ThenBy(o => o.Name)
            .FirstOrDefault();

        dto.AssistPairs = pairs
            .Select(p => new AssistPairDto
            {
                AssisterId = p.Key.Assister, AssisterName = Full(p.Key.Assister), AssisterShortName = Short(p.Key.Assister),
                ScorerId   = p.Key.Scorer,   ScorerName   = Full(p.Key.Scorer),   ScorerShortName   = Short(p.Key.Scorer),
                Count      = p.Value,
            })
            .OrderByDescending(a => a.Count).ThenBy(a => a.AssisterName).ThenBy(a => a.ScorerName)
            .ToList();

        dto.Matches     = Enumerable.Reverse(history).ToList();
        dto.Curiosities = Curiosities(dto, scoredWins, Short);

        return Result<TeamBuilderStatsDto>.Ok(dto);
    }

    // ── Curiosidades ─────────────────────────────────────────────────────────

    private static List<string> Curiosities(
        TeamBuilderStatsDto s, Dictionary<Guid, (int Wins, int Games)> scoredWins, Func<Guid, string> shortName)
    {
        var list = new List<string>
        {
            $"Juntos fazem **{s.PointsPct}%** de aproveitamento. A média deles, somando todos os jogos de cada um, é **{s.AverageIndividualPointsPct}%**.",
        };

        var scorer = scoredWins
            .Where(x => x.Value.Games >= MinScoredGames)
            .OrderByDescending(x => x.Value.Wins / (double)x.Value.Games).ThenByDescending(x => x.Value.Games)
            .Select(x => (Id: x.Key, x.Value.Wins, x.Value.Games))
            .FirstOrDefault();
        if (scorer.Games > 0)
            list.Add($"Nos jogos juntos em que **{shortName(scorer.Id)}** marcou, o time venceu **{scorer.Wins} de {scorer.Games}**.");

        if (s.TeamRegisteredGoals > 0)
            list.Add($"**{RoundPct(s.GoalsScoredByPlayers * 100.0 / s.TeamRegisteredGoals)}%** dos gols registrados do time nesses jogos saíram dos pés deles.");

        if (s.OppositeTeamsMatches == 1)
            list.Add("Em **1** outro jogo estavam em campo, mas em times opostos. Esse não conta aqui.");
        else if (s.OppositeTeamsMatches > 1)
            list.Add($"Em outros **{s.OppositeTeamsMatches}** jogos estavam em campo, mas em times opostos. Esses não contam aqui.");

        if (s.BestWinStreak >= 3)
            list.Add($"Maior sequência juntos: **{s.BestWinStreak} vitórias seguidas**.");

        return list;
    }

    // ── Dados ────────────────────────────────────────────────────────────────

    private sealed record TbGoal(Guid? Scorer, Guid? Assist, bool IsOwnGoal, short ForSide);

    private sealed record TbMatch(
        Guid Id, DateTime PlayedAt, DateOnly LocalDate, int TeamA, int TeamB, string? PlaceName,
        string? ColorAHex, string? ColorBHex, bool IsChampionship,
        Dictionary<Guid, short> TeamOf, HashSet<Guid> Mvps, List<TbGoal> Goals)
    {
        public int ResultFor(short side)
        {
            var mine = side == 1 ? TeamA : TeamB;
            var them = side == 1 ? TeamB : TeamA;
            return mine > them ? 1 : mine == them ? 0 : -1;
        }

        public FormationMatchDto ToFormationMatch(short side) => new()
        {
            MatchId         = Id,
            PlayedAt        = new DateTimeOffset(DateTime.SpecifyKind(PlayedAt, DateTimeKind.Utc)),
            GoalsFor        = side == 1 ? TeamA : TeamB,
            GoalsAgainst    = side == 1 ? TeamB : TeamA,
            PlaceName       = PlaceName,
            ColorForHex     = side == 1 ? ColorAHex : ColorBHex,
            ColorAgainstHex = side == 1 ? ColorBHex : ColorAHex,
            Result          = ResultFor(side),
            IsChampionship  = IsChampionship,
        };
    }

    /// <summary>Partidas finalizadas com placar, em ordem cronológica.</summary>
    private async Task<List<TbMatch>> LoadMatchesAsync(Guid groupId, bool includeChampionship, CancellationToken ct)
    {
        // Jogos de campeonato só entram quando a regra os trata exatamente como partidas
        // normais. Com os ids já filtrados, a consulta abaixo ignora o filtro global.
        var championshipGameIds = includeChampionship
            ? await new ChampionshipStatsSource(_db).GetFullyCountingGameIdsAsync(groupId, ct)
            : [];

        var rows = await _db.Matches.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized
                     && m.TeamAGoals != null && m.TeamBGoals != null)
            .Where(m => m.ChampionshipId == null || championshipGameIds.Contains(m.Id))
            .Select(m => new
            {
                m.Id,
                m.PlayedAt,
                m.TeamAGoals,
                m.TeamBGoals,
                m.PlaceName,
                m.ChampionshipId,
                ColorA  = m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                ColorB  = m.TeamBColor != null ? m.TeamBColor.HexValue : null,
                Players = m.Players.Select(mp => new { mp.Id, mp.PlayerId, mp.Team, mp.DidNotPlay, mp.IsMvp }).ToList(),
                Goals   = m.Goals.Select(g => new { g.ScorerMatchPlayerId, g.AssistMatchPlayerId, g.IsOwnGoal }).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var result = new List<TbMatch>(rows.Count);
        foreach (var m in rows)
        {
            var teamOf = new Dictionary<Guid, short>();
            foreach (var mp in m.Players.Where(mp => mp.Team is 1 or 2 && !mp.DidNotPlay))
                teamOf.TryAdd(mp.PlayerId, mp.Team);

            var byMp   = m.Players.GroupBy(mp => mp.Id).ToDictionary(g => g.Key, g => g.First());
            var goals  = new List<TbGoal>(m.Goals.Count);
            foreach (var g in m.Goals)
            {
                if (!byMp.TryGetValue(g.ScorerMatchPlayerId, out var scorerMp) || scorerMp.Team is not (1 or 2)) continue;
                var forSide = g.IsOwnGoal ? (short)(3 - scorerMp.Team) : scorerMp.Team;
                Guid? scorer = scorerMp.DidNotPlay ? null : scorerMp.PlayerId;
                Guid? assist = !g.IsOwnGoal && g.AssistMatchPlayerId is { } a && byMp.TryGetValue(a, out var amp) && !amp.DidNotPlay
                    ? amp.PlayerId
                    : null;
                goals.Add(new TbGoal(scorer, assist, g.IsOwnGoal, forSide));
            }

            var mvps = m.Players.Where(mp => mp.IsMvp == true && !mp.DidNotPlay).Select(mp => mp.PlayerId).ToHashSet();
            var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(m.PlayedAt, DateTimeKind.Utc), BrazilTz));

            result.Add(new TbMatch(m.Id, m.PlayedAt, local, m.TeamAGoals!.Value, m.TeamBGoals!.Value, m.PlaceName,
                m.ColorA, m.ColorB, m.ChampionshipId != null, teamOf, mvps, goals));
        }

        return result.OrderBy(m => m.PlayedAt).ThenBy(m => m.Id).ToList();
    }

    private static List<TbMatch> ApplyFilter(List<TbMatch> matches, TeamBuilderFilterDto f)
    {
        IEnumerable<TbMatch> q = matches;
        if (f.Season is { } season) q = q.Where(m => m.LocalDate.Year == season);
        if (f.From is { } from)     q = q.Where(m => m.LocalDate >= from);
        if (f.To is { } to)         q = q.Where(m => m.LocalDate <= to);

        var list = q.ToList();
        if (f.LastMatches is { } last && last > 0 && list.Count > last)
            list = list.Skip(list.Count - last).ToList();
        return list;
    }

    /// <summary>Primeiro nome, ou o nome completo quando outro jogador da patota (convidados inclusive) tem o mesmo.</summary>
    private async Task<Dictionary<Guid, string>> ShortNamesAsync(Guid groupId, CancellationToken ct)
    {
        var all = await _db.Players
            .Where(p => p.GroupId == groupId)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        static string First(string name) => name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

        var counts = all.GroupBy(p => First(p.Name), StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.CurrentCultureIgnoreCase);

        return all.ToDictionary(p => p.Id, p => counts[First(p.Name)] > 1 ? p.Name.Trim() : First(p.Name));
    }

    private static int PointsPct(int wins, int draws, int games) =>
        games == 0 ? 0 : RoundPct((wins * 3 + draws) * 100.0 / (games * 3));

    private static int RoundPct(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static TimeZoneInfo ResolveBrazilTz()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }
}
