using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.TeamBuilder;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class TeamBuilderService : ITeamBuilderService
{
    private readonly AppDbContext _db;

    public TeamBuilderService(AppDbContext db) => _db = db;

    public async Task<Result<TeamBuilderStatsDto>> GetStatsAsync(
        Guid groupId, List<Guid> playerIds, CancellationToken ct)
    {
        if (playerIds is null || playerIds.Count < 2 || playerIds.Count > 5)
            return Result<TeamBuilderStatsDto>.Fail(
                "Selecione entre 2 e 5 jogadores.", ResultStatus.BadRequest);

        playerIds = playerIds.Distinct().ToList();

        var validCount = await _db.Players
            .CountAsync(p => playerIds.Contains(p.Id) && p.GroupId == groupId, ct);

        if (validCount != playerIds.Count)
            return Result<TeamBuilderStatsDto>.Fail(
                "Um ou mais jogadores não pertencem a este grupo.", ResultStatus.BadRequest);

        var players = await _db.Players
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new SelectedPlayerDto { Id = p.Id, Name = p.Name, IsGoalkeeper = p.IsGoalkeeper })
            .ToListAsync(ct);

        var playerIdSet = playerIds.ToHashSet();
        var n           = playerIds.Count;

        var matchIds = await _db.Matches
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized)
            .Where(m => _db.MatchPlayers
                .Count(mp => mp.MatchId == m.Id
                          && playerIdSet.Contains(mp.PlayerId)
                          && mp.InviteResponse == InviteResponse.Accepted
                          && !mp.DidNotPlay) == n)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (matchIds.Count == 0)
            return Result<TeamBuilderStatsDto>.Ok(
                new TeamBuilderStatsDto { NeverPlayedTogether = true, Players = players });

        var matchData = await _db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new
            {
                m.Id,
                m.PlayedAt,
                m.TeamAGoals,
                m.TeamBGoals,
                m.PlaceName,
                TeamAColorHex = m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                TeamBColorHex = m.TeamBColor != null ? m.TeamBColor.HexValue : null,
                SelectedMPs = m.Players
                    .Where(mp => playerIdSet.Contains(mp.PlayerId) && !mp.DidNotPlay)
                    .Select(mp => new { mp.Id, mp.PlayerId, mp.Team, mp.IsMvp })
                    .ToList(),
            })
            .ToListAsync(ct);

        var mpToPlayer = matchData
            .SelectMany(m => m.SelectedMPs)
            .GroupBy(mp => mp.Id)
            .ToDictionary(g => g.Key, g => g.First().PlayerId);

        var mpIdSet = mpToPlayer.Keys.ToHashSet();

        int wins = 0, draws = 0, losses = 0, goalsScored = 0, goalsConceded = 0;
        var playerNameMap = players.ToDictionary(p => p.Id, p => p.Name);

        // Quebra por jogador (#4)
        var pb = players.ToDictionary(p => p.Id, p => new PlayerBreakdownDto
        {
            Id = p.Id, Name = p.Name, IsGoalkeeper = p.IsGoalkeeper,
        });

        // Histórico da formação (#7)
        var formationMatches = new List<FormationMatchDto>(matchData.Count);

        foreach (var match in matchData)
        {
            var t1       = match.SelectedMPs.Count(mp => mp.Team == 1);
            var t2       = match.SelectedMPs.Count(mp => mp.Team == 2);
            var dominant = t1 >= t2 ? 1 : 2;

            var teamG = dominant == 1 ? (match.TeamAGoals ?? 0) : (match.TeamBGoals ?? 0);
            var oppG  = dominant == 1 ? (match.TeamBGoals ?? 0) : (match.TeamAGoals ?? 0);

            goalsScored   += teamG;
            goalsConceded += oppG;

            int result;
            if      (teamG > oppG)  { wins++;   result = 1;  }
            else if (teamG == oppG) { draws++;  result = 0;  }
            else                    { losses++; result = -1; }

            formationMatches.Add(new FormationMatchDto
            {
                MatchId         = match.Id,
                PlayedAt        = match.PlayedAt,
                GoalsFor        = teamG,
                GoalsAgainst    = oppG,
                PlaceName       = match.PlaceName,
                ColorForHex     = dominant == 1 ? match.TeamAColorHex : match.TeamBColorHex,
                ColorAgainstHex = dominant == 1 ? match.TeamBColorHex : match.TeamAColorHex,
                Result          = result,
            });

            // W/D/L por jogador (perspectiva do próprio time) + MVP
            foreach (var mp in match.SelectedMPs)
            {
                if (mp.Team != 1 && mp.Team != 2) continue;
                if (!pb.TryGetValue(mp.PlayerId, out var acc)) continue;

                var myG  = mp.Team == 1 ? (match.TeamAGoals ?? 0) : (match.TeamBGoals ?? 0);
                var advG = mp.Team == 1 ? (match.TeamBGoals ?? 0) : (match.TeamAGoals ?? 0);

                acc.Games++;
                if      (myG > advG)  acc.Wins++;
                else if (myG == advG) acc.Draws++;
                else                  acc.Losses++;
                if (mp.IsMvp == true) acc.Mvps++;
            }
        }

        // Todos os gols válidos das partidas (contagem por jogador + pares de assistência)
        var goals = await _db.Goals
            .Where(g => matchIds.Contains(g.MatchId) && !g.IsOwnGoal)
            .Select(g => new { g.ScorerMatchPlayerId, g.AssistMatchPlayerId })
            .ToListAsync(ct);

        var goalsScoredByPlayers = 0;
        foreach (var g in goals)
        {
            if (mpToPlayer.TryGetValue(g.ScorerMatchPlayerId, out var scorerPid)
                && pb.TryGetValue(scorerPid, out var sAcc))
            {
                sAcc.Goals++;
                goalsScoredByPlayers++;
            }
            if (g.AssistMatchPlayerId.HasValue
                && mpToPlayer.TryGetValue(g.AssistMatchPlayerId.Value, out var assistPid)
                && pb.TryGetValue(assistPid, out var aAcc))
            {
                aAcc.Assists++;
            }
        }

        // Assistências ENTRE os selecionados (ambos na seleção)
        var assistPairs = goals
            .Where(g => g.AssistMatchPlayerId.HasValue
                     && mpIdSet.Contains(g.ScorerMatchPlayerId)
                     && mpIdSet.Contains(g.AssistMatchPlayerId.Value))
            .GroupBy(g => (
                Assister: mpToPlayer[g.AssistMatchPlayerId!.Value],
                Scorer:   mpToPlayer[g.ScorerMatchPlayerId]))
            .Select(grp => new AssistPairDto
            {
                AssisterId   = grp.Key.Assister,
                AssisterName = playerNameMap.GetValueOrDefault(grp.Key.Assister, ""),
                ScorerId     = grp.Key.Scorer,
                ScorerName   = playerNameMap.GetValueOrDefault(grp.Key.Scorer, ""),
                Count        = grp.Count(),
            })
            .OrderByDescending(a => a.Count)
            .ToList();

        foreach (var acc in pb.Values)
            acc.WinRate = acc.Games > 0 ? acc.Wins / (double)acc.Games : 0.0;

        var playerBreakdown = pb.Values
            .OrderByDescending(p => p.Goals + p.Assists)
            .ThenByDescending(p => p.Wins)
            .ThenBy(p => p.Name)
            .ToList();

        return Result<TeamBuilderStatsDto>.Ok(new TeamBuilderStatsDto
        {
            NeverPlayedTogether  = false,
            TotalMatches         = matchIds.Count,
            Wins                 = wins,
            Draws                = draws,
            Losses               = losses,
            GoalsScored          = goalsScored,
            GoalsConceded        = goalsConceded,
            GoalsScoredByPlayers = goalsScoredByPlayers,
            AssistPairs          = assistPairs,
            Players              = players,
            PlayerBreakdown      = playerBreakdown,
            Matches              = formationMatches.OrderByDescending(m => m.PlayedAt).ToList(),
        });
    }
}
