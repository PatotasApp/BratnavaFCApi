using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BratnavaFC.Application.Services;

public sealed class PlayerStatsService : IPlayerStatsService
{
    private readonly AppDbContext _context;

    public PlayerStatsService(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Result<List<PlayerStats>>> EnrichPlayersAsync(
        List<PlayerRequestDto> players,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (players.Count == 0) return Result<List<PlayerStats>>.Ok(new List<PlayerStats>());

        var playerIds = players.Select(p => p.Id).ToHashSet();

        // Only the last 20 finalized matches that include at least one of the players requested.
        var matches = await LoadRecentFinalizedMatchesAsync(playerIds, cancellationToken);

        // Carrega ratings diretamente do banco para garantir o valor mais atualizado
        var playerRatingMap = await _context.Players
            .AsNoTracking()
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.GuestStarRating, p.AttackRating, p.DefenseRating, p.OverallRating })
            .ToDictionaryAsync(p => p.Id, p => p, cancellationToken);

        var perPlayer = InitializePlayerAccumulators(playerIds);
        var pairTotals = new Dictionary<PairKey, PairAccumulator>();

        foreach (var match in matches)
        {
            ProcessMatchForBasicStats(match, playerIds, perPlayer, pairTotals);
        }

        // Pre-compute Bayesian WR for each player (used for W_base and synergy baseline)
        var winRateAdjById = perPlayer.ToDictionary(
            kv => kv.Key,
            kv => BayesianWinRate(kv.Value.Wins, kv.Value.Ties, kv.Value.MatchesPlayed));

        // Group-average goal contribution (null = no scoring data → neutral GoalContrib)
        double? groupAvgGC = ComputeGroupAvgGC(perPlayer.Values);

        var result = new List<PlayerStats>(players.Count);

        const int minMatchesNonNeutral = 3;

        foreach (var pl in players)
        {
            var acc = perPlayer.TryGetValue(pl.Id, out var a) ? a : PlayerAccumulator.Empty;

            double winRateAdj     = winRateAdjById.TryGetValue(pl.Id, out var wra) ? wra : BayesianWinRate(0, 0, 0);
            double goalContribNorm = (acc.MatchesPlayed == 0 || !groupAvgGC.HasValue)
                ? 0.5
                : ComputeGoalContribNorm(acc, groupAvgGC.Value);

            // ── Dimension ratings (ataque, defesa, físico) ───────────────────
            // Os ratings não afetam o W_base. São usados pelo AlgorithmStrategy
            // como restrições dimensionais: distribui atacantes, defensores e jogadores
            // físicos de forma equilibrada entre os times.
            var dbRow = playerRatingMap.TryGetValue(pl.Id, out var row) ? row : null;
            double? attackRatingNorm   = dbRow?.AttackRating.HasValue  == true ? dbRow.AttackRating.Value  / 10.0 : (double?)null;
            double? defenseRatingNorm  = dbRow?.DefenseRating.HasValue == true ? dbRow.DefenseRating.Value / 10.0 : (double?)null;
            double? physicalRatingNorm = dbRow?.OverallRating.HasValue == true ? dbRow.OverallRating.Value / 10.0 : (double?)null;

            // W_base: histórico de resultados + contribuição ofensiva
            double wBase = 0.70 * winRateAdj + 0.30 * goalContribNorm;

            var synergy = BuildSynergyMap(pl.Id, winRateAdjById, players, pairTotals);

            // ── NeutralOverride (jogadores com < 3 partidas) ─────────────────
            // Prioridade: OverallRating/10 > GuestStarRating > null
            double? neutralOverride = null;
            if (acc.MatchesPlayed < minMatchesNonNeutral)
            {
                if (dbRow?.OverallRating.HasValue == true)
                    neutralOverride = dbRow.OverallRating.Value / 10.0;
                else if (dbRow?.GuestStarRating.HasValue == true)
                    neutralOverride = (dbRow.GuestStarRating.Value - 1) * 0.25;
            }

            result.Add(new PlayerStats
            {
                PlayerId        = pl.Id,
                Name            = pl.Name,
                Wins            = acc.Wins,
                Ties            = acc.Ties,
                Losses          = acc.Losses,
                WinRate            = wBase,
                Goals              = acc.Goals,
                Assists            = acc.Assists,
                SynergyWith        = synergy,
                NeutralOverride    = neutralOverride,
                AttackRatingNorm   = attackRatingNorm,
                DefenseRatingNorm  = defenseRatingNorm,
                PhysicalRatingNorm = physicalRatingNorm,
            });
        }

        return Result<List<PlayerStats>>.Ok(result);
    }

    public async Task<PlayerVisualStatsReport> GetVisualReportAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        // (mantem do jeito que voce ja tinha; aqui usamos PlayerEntity do banco, ok)
        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && !p.IsGuest)
            .ToListAsync(cancellationToken);

        if (players.Count == 0)
        {
            return new PlayerVisualStatsReport
            {
                GroupId = groupId,
                TotalMatchesConsidered = 0,
                TotalFinalizedMatches = 0,
                TotalMatchesWithScore = 0,
                Players = new List<PlayerVisualStatsItem>()
            };
        }

        var playerIds = players.Select(p => p.Id).ToHashSet();

        var matches = await _context.Matches
            .AsNoTracking()
            .AsSplitQuery()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .Include(m => m.Votes)
            .ToListAsync(cancellationToken);

        var matchesFiltered = matches
            .Select(m =>
            {
                var (teamA, teamB) = GetTeams(m, groupId, playerIds);
                return new { Match = m, TeamA = teamA, TeamB = teamB };
            })
            .Where(x => x.TeamA.Count > 0 || x.TeamB.Count > 0)
            .ToList();

        var totalMatches = matchesFiltered.Count;
        var totalFinalized = matchesFiltered.Count(x => x.Match.Status == MatchStatus.Finalized);
        var totalWithScore = matchesFiltered.Count(x => x.Match.TeamAGoals.HasValue && x.Match.TeamBGoals.HasValue);

        var perPlayer = InitializePlayerAccumulators(playerIds);
        var mvpCounts = playerIds.ToDictionary(id => id, _ => 0);
        var mvpVoteCounts = playerIds.ToDictionary(id => id, _ => 0);
        var pairTotals = new Dictionary<PairKey, PairAccumulator>();

        foreach (var x in matchesFiltered)
        {
            var mpIdToPlayerId = x.Match.Players
                .Where(mp => playerIds.Contains(mp.PlayerId) && !mp.DidNotPlay)
                .ToDictionary(mp => mp.Id, mp => mp.PlayerId);

            foreach (var vote in x.Match.Votes ?? [])
            {
                if (mpIdToPlayerId.TryGetValue(vote.VotedForId, out var votedPlayerId)
                    && mvpVoteCounts.ContainsKey(votedPlayerId))
                    mvpVoteCounts[votedPlayerId]++;
            }
        }

        foreach (var x in matchesFiltered)
        {
            ProcessMatchForVisual(
                x.Match,
                x.TeamA,
                x.TeamB,
                playerIds,
                perPlayer,
                pairTotals,
                mvpCounts);
        }

        var playerNameById = players.ToDictionary(p => p.Id, p => p.Name);

        var items = new List<PlayerVisualStatsItem>(players.Count);

        foreach (var pl in players)
        {
            var acc = perPlayer.TryGetValue(pl.Id, out var a) ? a : PlayerAccumulator.Empty;

            var winRate = acc.MatchesPlayed == 0
                ? 0.0
                : acc.Wins / (double)acc.MatchesPlayed;

            var synergies = BuildSynergyVisual(pl.Id, playerIds, playerNameById, pairTotals);

            items.Add(new PlayerVisualStatsItem
            {
                PlayerId = pl.Id,
                Name = pl.Name,
                Status = pl.Status,
                IsGoalkeeper = pl.IsGoalkeeper,

                GamesPlayed = acc.MatchesPlayed,
                Wins = acc.Wins,
                Ties = acc.Ties,
                Losses = acc.Losses,
                WinRate = winRate,
                Points = acc.Wins * 3 + acc.Ties,

                Mvps = mvpCounts.TryGetValue(pl.Id, out var mvps) ? mvps : 0,
                MvpVotes = mvpVoteCounts.TryGetValue(pl.Id, out var votes) ? votes : 0,

                Goals    = acc.Goals,
                Assists  = acc.Assists,
                OwnGoals = acc.OwnGoals,

                Synergies = synergies
                    .OrderByDescending(s => s.MatchesTogether)
                    .ThenByDescending(s => s.WinRateTogether)
                    .ToList()
            });
        }

        items = ApplyVisualRanks(items);

        items = items
            .OrderByDescending(p => p.WinRate)
            .ThenByDescending(p => p.GamesPlayed)
            .ThenByDescending(p => p.Mvps)
            .ThenBy(p => p.Name)
            .ToList();

        return new PlayerVisualStatsReport
        {
            GroupId = groupId,
            TotalMatchesConsidered = totalMatches,
            TotalFinalizedMatches = totalFinalized,
            TotalMatchesWithScore = totalWithScore,
            Players = items
        };
    }

    public async Task<PlayerSpotlightReport> GetSpotlightReportAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        var players = await _context.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && p.Status == Domain.Enums.Status.Active)
            .ToListAsync(cancellationToken);

        if (players.Count == 0)
            return new PlayerSpotlightReport { GroupId = groupId };

        var playerIds = players.Select(p => p.Id).ToHashSet();

        var matches = await _context.Matches
            .AsNoTracking()
            .AsSplitQuery()
            .Where(m => m.GroupId == groupId && m.Status == MatchStatus.Finalized)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .ToListAsync(cancellationToken);

        // Accumulators
        var perPlayer      = InitializePlayerAccumulators(playerIds);
        var pairTotals     = new Dictionary<PairKey, PairAccumulator>();
        var opponentTotals = new Dictionary<PairKey, SpotlightOpponentAccumulator>();
        var assistPairs    = new Dictionary<(Guid scorer, Guid assister), int>();
        var mvpCounts      = playerIds.ToDictionary(id => id, _ => 0);

        foreach (var match in matches)
        {
            var (teamA, teamB) = GetTeams(match, groupId, playerIds);
            if (teamA.Count == 0 && teamB.Count == 0) continue;

            // ── MVP ──
            foreach (var mp in teamA.Concat(teamB))
                if (mp.IsMvp == true && mvpCounts.ContainsKey(mp.PlayerId))
                    mvpCounts[mp.PlayerId]++;

            var outcome = GetMatchOutcome(match);

            // ── Per-player W/L/T ──
            foreach (var mp in teamA.Concat(teamB).GroupBy(p => p.PlayerId).Select(g => g.First()))
            {
                var pid = mp.PlayerId;
                if (!perPlayer.TryGetValue(pid, out var acc)) continue;
                acc.MatchesPlayed++;
                if (outcome.HasScore)
                {
                    if (outcome.IsTie) acc.Ties++;
                    else
                    {
                        bool inA = teamA.Any(x => x.PlayerId == pid);
                        if (inA && outcome.WinningTeam == MatchWinningTeam.TeamA) acc.Wins++;
                        else if (!inA && teamB.Any(x => x.PlayerId == pid) && outcome.WinningTeam == MatchWinningTeam.TeamB) acc.Wins++;
                        else acc.Losses++;
                    }
                }
                perPlayer[pid] = acc;
            }

            // ── Synergy (same team) ──
            bool isTie = outcome.HasScore && outcome.IsTie;
            AddTeamSynergy(teamA, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamA, isTie, pairTotals);
            AddTeamSynergy(teamB, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamB, isTie, pairTotals);

            // ── Head-to-head (opposite teams) ──
            if (outcome.HasScore && !outcome.IsTie)
            {
                var idsA = teamA.Select(p => p.PlayerId).Distinct().ToList();
                var idsB = teamB.Select(p => p.PlayerId).Distinct().ToList();
                bool aWon = outcome.WinningTeam == MatchWinningTeam.TeamA;

                foreach (var pidA in idsA)
                    foreach (var pidB in idsB)
                    {
                        var key = PairKey.Create(pidA, pidB);
                        if (!opponentTotals.TryGetValue(key, out var oa))
                            oa = new SpotlightOpponentAccumulator();
                        oa.Matches++;
                        // WinsForSmallerId = wins for the player whose GUID is "smaller" (key.A)
                        if (aWon)
                        {
                            if (pidA == key.A) oa.WinsForA++; else oa.WinsForB++;
                        }
                        else
                        {
                            if (pidB == key.A) oa.WinsForA++; else oa.WinsForB++;
                        }
                        opponentTotals[key] = oa;
                    }
            }

            // ── Goals / assists ──
            var mpIdToPlayerId = match.Players
                .Where(mp => playerIds.Contains(mp.PlayerId) && !mp.DidNotPlay)
                .ToDictionary(mp => mp.Id, mp => mp.PlayerId);

            foreach (var goal in match.Goals ?? [])
            {
                if (goal.IsOwnGoal)
                {
                    if (mpIdToPlayerId.TryGetValue(goal.ScorerMatchPlayerId, out var pid)
                        && perPlayer.TryGetValue(pid, out var acc))
                    { acc.OwnGoals++; perPlayer[pid] = acc; }
                    continue;
                }

                if (mpIdToPlayerId.TryGetValue(goal.ScorerMatchPlayerId, out var scorerPid)
                    && perPlayer.TryGetValue(scorerPid, out var gAcc))
                { gAcc.Goals++; perPlayer[scorerPid] = gAcc; }

                if (goal.AssistMatchPlayerId.HasValue
                    && mpIdToPlayerId.TryGetValue(goal.AssistMatchPlayerId.Value, out var assistPid)
                    && perPlayer.TryGetValue(assistPid, out var aAcc))
                {
                    aAcc.Assists++; perPlayer[assistPid] = aAcc;

                    // track per-pair assist
                    if (mpIdToPlayerId.TryGetValue(goal.ScorerMatchPlayerId, out var sp))
                    {
                        var apKey = (scorer: sp, assister: assistPid);
                        assistPairs[apKey] = assistPairs.TryGetValue(apKey, out var c) ? c + 1 : 1;
                    }
                }
            }
        }

        var playerNameById = players.ToDictionary(p => p.Id, p => p.Name);
        const int minTogether = 1;
        const int minAgainst  = 1;

        var items = new List<PlayerSpotlightItem>(players.Count);

        foreach (var pl in players)
        {
            var acc = perPlayer.TryGetValue(pl.Id, out var a) ? a : PlayerAccumulator.Empty;
            double winRate = acc.MatchesPlayed == 0 ? 0.0 : acc.Wins / (double)acc.MatchesPlayed;

            // ── Partners (synergy) ──
            var partnerList = new List<(Guid id, int matches, double wr)>();
            foreach (var otherId in playerIds)
            {
                if (otherId == pl.Id) continue;
                var key = PairKey.Create(pl.Id, otherId);
                if (!pairTotals.TryGetValue(key, out var pa) || pa.MatchesTogether < minTogether) continue;
                double wr = pa.WinsTogether / (double)pa.MatchesTogether;
                partnerList.Add((otherId, pa.MatchesTogether, wr));
            }

            var bestPartners = partnerList
                .OrderByDescending(x => x.wr).ThenByDescending(x => x.matches)
                .Take(3)
                .Select(x => new SpotlightRelation { PlayerId = x.id, Name = playerNameById.GetValueOrDefault(x.id, ""), Count = x.matches, Rate = x.wr })
                .ToList();

            var worstPartners = partnerList
                .OrderBy(x => x.wr).ThenByDescending(x => x.matches)
                .Take(3)
                .Select(x => new SpotlightRelation { PlayerId = x.id, Name = playerNameById.GetValueOrDefault(x.id, ""), Count = x.matches, Rate = x.wr })
                .ToList();

            // ── Head-to-head (opponents) ──
            var h2hList = new List<(Guid id, int matches, double myWinRate)>();
            foreach (var otherId in playerIds)
            {
                if (otherId == pl.Id) continue;
                var key = PairKey.Create(pl.Id, otherId);
                if (!opponentTotals.TryGetValue(key, out var oa) || oa.Matches < minAgainst) continue;
                int myWins = (pl.Id == key.A) ? oa.WinsForA : oa.WinsForB;
                double myWR = myWins / (double)oa.Matches;
                h2hList.Add((otherId, oa.Matches, myWR));
            }

            // Who beats me most = lowest myWinRate (= highest opponent win rate)
            var mostBeatenBy = h2hList
                .OrderBy(x => x.myWinRate).ThenByDescending(x => x.matches)
                .Take(3)
                .Select(x => new SpotlightRelation { PlayerId = x.id, Name = playerNameById.GetValueOrDefault(x.id, ""), Count = x.matches, Rate = 1.0 - x.myWinRate })
                .ToList();

            // Who I beat most = highest myWinRate
            var leastBeatenBy = h2hList
                .OrderByDescending(x => x.myWinRate).ThenByDescending(x => x.matches)
                .Take(3)
                .Select(x => new SpotlightRelation { PlayerId = x.id, Name = playerNameById.GetValueOrDefault(x.id, ""), Count = x.matches, Rate = x.myWinRate })
                .ToList();

            // ── Assist relationships ──
            var mostAssistedBy = assistPairs
                .Where(kv => kv.Key.scorer == pl.Id)
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => new SpotlightRelation { PlayerId = kv.Key.assister, Name = playerNameById.GetValueOrDefault(kv.Key.assister, ""), Count = kv.Value, Rate = 0 })
                .ToList();

            var mostAssistedTo = assistPairs
                .Where(kv => kv.Key.assister == pl.Id)
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => new SpotlightRelation { PlayerId = kv.Key.scorer, Name = playerNameById.GetValueOrDefault(kv.Key.scorer, ""), Count = kv.Value, Rate = 0 })
                .ToList();

            items.Add(new PlayerSpotlightItem
            {
                PlayerId    = pl.Id,
                Name        = pl.Name,
                IsGoalkeeper = pl.IsGoalkeeper,
                IsGuest      = pl.IsGuest,
                GamesPlayed  = acc.MatchesPlayed,
                Wins         = acc.Wins,
                Losses       = acc.Losses,
                Ties         = acc.Ties,
                WinRate      = winRate,
                Goals        = acc.Goals,
                Assists      = acc.Assists,
                Mvps         = mvpCounts.TryGetValue(pl.Id, out var m) ? m : 0,
                BestPartners  = bestPartners,
                WorstPartners = worstPartners,
                MostBeatenBy  = mostBeatenBy,
                LeastBeatenBy = leastBeatenBy,
                MostAssistedBy = mostAssistedBy,
                MostAssistedTo = mostAssistedTo,
            });
        }

        // Sort: active players with most games first
        items = items
            .OrderByDescending(p => p.GamesPlayed)
            .ThenByDescending(p => p.WinRate)
            .ThenBy(p => p.Name)
            .ToList();

        return new PlayerSpotlightReport { GroupId = groupId, Players = items };
    }

    // ----------------- internals -----------------

    private Task<List<MatchEntity>> LoadFinalizedMatchesAsync(HashSet<Guid> playerIds, CancellationToken cancellationToken)
    {
        return _context.Matches
            .AsNoTracking()
            .Where(m => m.Status == MatchStatus.Finalized)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the last 20 finalized matches that include at least one of the requested players,
    /// ordered newest first. Used by <see cref="EnrichPlayersAsync"/> to keep the stat window
    /// recent and to bound query cost regardless of total match history.
    /// </summary>
    private Task<List<MatchEntity>> LoadRecentFinalizedMatchesAsync(HashSet<Guid> playerIds, CancellationToken cancellationToken)
    {
        return _context.Matches
            .AsNoTracking()
            .Where(m => m.Status == MatchStatus.Finalized
                     && m.Players.Any(mp => playerIds.Contains(mp.PlayerId)))
            .OrderByDescending(m => m.PlayedAt)
            .Take(20)
            .Include(m => m.Players)
            .Include(m => m.Goals)
            .ToListAsync(cancellationToken);
    }

    private static Dictionary<Guid, PlayerAccumulator> InitializePlayerAccumulators(HashSet<Guid> playerIds)
    {
        var dict = new Dictionary<Guid, PlayerAccumulator>(playerIds.Count);
        foreach (var id in playerIds)
            dict[id] = PlayerAccumulator.Empty;
        return dict;
    }

    private static void ProcessMatchForBasicStats(
        MatchEntity match,
        HashSet<Guid> trackedPlayerIds,
        Dictionary<Guid, PlayerAccumulator> perPlayer,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var (teamA, teamB) = GetTeams(match, match.GroupId, trackedPlayerIds);

        var participants = teamA.Concat(teamB)
            .GroupBy(p => p.PlayerId)
            .Select(g => g.First())
            .ToList();

        if (participants.Count == 0)
            return;

        var outcome = GetMatchOutcome(match);

        foreach (var mp in participants)
        {
            var playerId = mp.PlayerId;
            if (!perPlayer.TryGetValue(playerId, out var acc))
                continue;

            acc.MatchesPlayed++;

            if (outcome.HasScore)
            {
                if (outcome.IsTie)
                {
                    acc.Ties++;
                }
                else
                {
                    var isInA = teamA.Any(x => x.PlayerId == playerId);
                    var isInB = !isInA && teamB.Any(x => x.PlayerId == playerId);

                    if (isInA && outcome.WinningTeam == MatchWinningTeam.TeamA) acc.Wins++;
                    else if (isInB && outcome.WinningTeam == MatchWinningTeam.TeamB) acc.Wins++;
                    else acc.Losses++;
                }
            }

            perPlayer[playerId] = acc;
        }

        // ── Goals / assists for W_base GoalContrib component ─────────────────
        var mpIdToPlayerId = match.Players
            .Where(mp => trackedPlayerIds.Contains(mp.PlayerId) && !mp.DidNotPlay)
            .ToDictionary(mp => mp.Id, mp => mp.PlayerId);

        foreach (var goal in match.Goals ?? [])
        {
            if (goal.IsOwnGoal) continue;   // own goals not counted in W_base

            if (mpIdToPlayerId.TryGetValue(goal.ScorerMatchPlayerId, out var pid)
                && perPlayer.TryGetValue(pid, out var gAcc))
            {
                gAcc.Goals++;
            }

            if (goal.AssistMatchPlayerId.HasValue
                && mpIdToPlayerId.TryGetValue(goal.AssistMatchPlayerId.Value, out var aPid)
                && perPlayer.TryGetValue(aPid, out var aAcc))
            {
                aAcc.Assists++;
            }
        }

        bool isTieBasic = outcome.HasScore && outcome.IsTie;
        AddTeamSynergy(teamA, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamA, isTieBasic, pairTotals);
        AddTeamSynergy(teamB, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamB, isTieBasic, pairTotals);
    }

    private static void ProcessMatchForVisual(
        MatchEntity match,
        IReadOnlyList<MatchPlayerEntity> teamA,
        IReadOnlyList<MatchPlayerEntity> teamB,
        HashSet<Guid> trackedPlayerIds,
        Dictionary<Guid, PlayerAccumulator> perPlayer,
        Dictionary<PairKey, PairAccumulator> pairTotals,
        Dictionary<Guid, int> mvpCounts)
    {
        var participants = teamA.Concat(teamB)
            .GroupBy(p => p.PlayerId)
            .Select(g => g.First())
            .ToList();

        if (participants.Count == 0)
            return;

        foreach (var mp in participants)
        {
            if (mp.IsMvp == true && mvpCounts.ContainsKey(mp.PlayerId))
                mvpCounts[mp.PlayerId]++;
        }

        var outcome = GetMatchOutcome(match);

        foreach (var mp in participants)
        {
            var playerId = mp.PlayerId;
            if (!perPlayer.TryGetValue(playerId, out var acc))
                continue;

            acc.MatchesPlayed++;

            if (outcome.HasScore)
            {
                if (outcome.IsTie)
                {
                    acc.Ties++;
                }
                else
                {
                    var isInA = teamA.Any(x => x.PlayerId == playerId);
                    var isInB = !isInA && teamB.Any(x => x.PlayerId == playerId);

                    if (isInA && outcome.WinningTeam == MatchWinningTeam.TeamA) acc.Wins++;
                    else if (isInB && outcome.WinningTeam == MatchWinningTeam.TeamB) acc.Wins++;
                    else acc.Losses++;
                }
            }

            perPlayer[playerId] = acc;
        }

        bool isTieVisual = outcome.HasScore && outcome.IsTie;
        AddTeamSynergy(teamA, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamA, isTieVisual, pairTotals);
        AddTeamSynergy(teamB, outcome.HasScore && !outcome.IsTie && outcome.WinningTeam == MatchWinningTeam.TeamB, isTieVisual, pairTotals);

        // ── Gols / assistências / gols-contra ─────────────────────────────
        // mapeia MatchPlayerEntity.Id → PlayerId para os jogadores rastreados (excluindo DidNotPlay)
        var mpIdToPlayerId = match.Players
            .Where(mp => trackedPlayerIds.Contains(mp.PlayerId) && !mp.DidNotPlay)
            .ToDictionary(mp => mp.Id, mp => mp.PlayerId);

        foreach (var goal in match.Goals ?? [])
        {
            if (goal.IsOwnGoal)
            {
                if (mpIdToPlayerId.TryGetValue(goal.ScorerMatchPlayerId, out var pid)
                    && perPlayer.TryGetValue(pid, out var acc))
                {
                    acc.OwnGoals++;
                    perPlayer[pid] = acc;
                }
            }
            else
            {
                if (mpIdToPlayerId.TryGetValue(goal.ScorerMatchPlayerId, out var pid)
                    && perPlayer.TryGetValue(pid, out var acc))
                {
                    acc.Goals++;
                    perPlayer[pid] = acc;
                }

                if (goal.AssistMatchPlayerId.HasValue
                    && mpIdToPlayerId.TryGetValue(goal.AssistMatchPlayerId.Value, out var aPid)
                    && perPlayer.TryGetValue(aPid, out var aAcc))
                {
                    aAcc.Assists++;
                    perPlayer[aPid] = aAcc;

                    // Track assist direction between this pair (both must be tracked players)
                    if (perPlayer.ContainsKey(pid) && aPid != pid)
                    {
                        var pairKey = PairKey.Create(aPid, pid);
                        if (!pairTotals.TryGetValue(pairKey, out var pairAcc))
                            pairAcc = PairAccumulator.Empty;
                        // aPid gave the assist TO pid
                        if (aPid == pairKey.A) pairAcc.AssistsAtoB++;
                        else                   pairAcc.AssistsBtoA++;
                        pairTotals[pairKey] = pairAcc;
                    }
                }
            }
        }
    }

    private static void AddTeamSynergy(
        IReadOnlyList<MatchPlayerEntity> teamPlayers,
        bool teamWon,
        bool isTie,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var ids = teamPlayers.Select(p => p.PlayerId).Distinct().ToList();
        if (ids.Count < 2) return;

        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                var key = PairKey.Create(ids[i], ids[j]);

                if (!pairTotals.TryGetValue(key, out var pairAcc))
                    pairAcc = PairAccumulator.Empty;

                pairAcc.MatchesTogether++;
                if (teamWon) pairAcc.WinsTogether++;
                if (isTie)   pairAcc.TiesTogether++;

                pairTotals[key] = pairAcc;
            }
    }

    private static (List<MatchPlayerEntity> TeamA, List<MatchPlayerEntity> TeamB) GetTeams(
        MatchEntity match,
        Guid groupId,
        HashSet<Guid> trackedPlayerIds)
    {
        var teamA = (match.TeamAPlayers ?? new List<MatchPlayerEntity>())
            .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId) && !mp.DidNotPlay)
            .ToList();

        var teamB = (match.TeamBPlayers ?? new List<MatchPlayerEntity>())
            .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId) && !mp.DidNotPlay)
            .ToList();

        if (teamA.Count == 0 && teamB.Count == 0 && match.Players is not null && match.Players.Count > 0)
        {
            teamA = match.Players
                .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId) && mp.Team == 1 && !mp.DidNotPlay)
                .ToList();

            teamB = match.Players
                .Where(mp => mp.GroupId == groupId && trackedPlayerIds.Contains(mp.PlayerId) && mp.Team == 2 && !mp.DidNotPlay)
                .ToList();
        }

        return (teamA, teamB);
    }

    private static MatchOutcome GetMatchOutcome(MatchEntity match)
    {
        if (!match.TeamAGoals.HasValue || !match.TeamBGoals.HasValue)
            return MatchOutcome.NoScore;

        if (match.TeamAGoals.Value == match.TeamBGoals.Value)
            return MatchOutcome.Tie;

        return match.TeamAGoals.Value > match.TeamBGoals.Value
            ? MatchOutcome.Win(MatchWinningTeam.TeamA)
            : MatchOutcome.Win(MatchWinningTeam.TeamB);
    }

    /// <summary>
    /// Builds the Synergy_eff map for a player.
    /// Synergy_eff = Confidence × (WR_together_adj − baseline), range ≈ [−0.5, +0.5].
    /// Zero means no shared history (no bonus/penalty).
    /// </summary>
    private static Dictionary<Guid, double> BuildSynergyMap(
        Guid playerId,
        Dictionary<Guid, double> winRateAdjById,
        List<PlayerRequestDto> allPlayers,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var map = new Dictionary<Guid, double>(Math.Max(0, allPlayers.Count - 1));

        double playerWRAdj = winRateAdjById.TryGetValue(playerId, out var pwr) ? pwr : BayesianWinRate(0, 0, 0);

        foreach (var other in allPlayers)
        {
            if (other.Id == playerId) continue;

            var key = PairKey.Create(playerId, other.Id);

            if (!pairTotals.TryGetValue(key, out var pairAcc) || pairAcc.MatchesTogether == 0)
            {
                map[other.Id] = 0.0;    // no shared history → neutral (no bonus/penalty)
                continue;
            }

            double otherWRAdj          = winRateAdjById.TryGetValue(other.Id, out var owr) ? owr : BayesianWinRate(0, 0, 0);
            double effectiveWinsTogether = pairAcc.WinsTogether + 0.5 * pairAcc.TiesTogether;
            double wrTogetherAdj       = (effectiveWinsTogether + 1.0) / (pairAcc.MatchesTogether + 2.0);
            double baseline            = (playerWRAdj + otherWRAdj) / 2.0;
            double confidence          = 1.0 - Math.Exp(-pairAcc.MatchesTogether / 5.0);
            map[other.Id]              = confidence * (wrTogetherAdj - baseline);
        }

        return map;
    }

    // ── W_base helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Bayesian win rate: shrinks extreme values toward 0.50 with prior α = 1.5.
    /// Ties count as half a win: effectiveWins = wins + 0.5 × ties.
    /// Formula: (effectiveWins + α) / (matches + 2α).
    /// </summary>
    private static double BayesianWinRate(int wins, int ties, int totalMatches)
    {
        const double alpha = 1.5;
        double effectiveWins = wins + 0.5 * ties;
        return (effectiveWins + alpha) / (totalMatches + 2.0 * alpha);
    }

    /// <summary>
    /// Returns the group-average raw goal contribution per game,
    /// or null when no player has scored (→ neutral GoalContrib for everyone).
    /// gc_raw(p) = (goals + 0.6 × assists) / matchesPlayed  (players with ≥ 1 match only).
    /// </summary>
    private static double? ComputeGroupAvgGC(IEnumerable<PlayerAccumulator> accs)
    {
        var eligible = accs.Where(a => a.MatchesPlayed >= 1).ToList();
        if (eligible.Count == 0) return null;

        double avg = eligible.Average(a => (a.Goals + 0.6 * a.Assists) / (double)a.MatchesPlayed);
        return avg > 0.0 ? avg : null;  // null = no one has scored → neutral for all
    }

    /// <summary>
    /// Normalised goal contribution with a Bayesian prior weight of 3 matches at the group average.
    /// Result is clamped to [0, 1] via: clamp(gcAdj / groupAvgGC / 2, 0, 1).
    /// Pre-condition: groupAvgGC > 0.
    /// </summary>
    private static double ComputeGoalContribNorm(PlayerAccumulator acc, double groupAvgGC)
    {
        double rawGC  = acc.Goals + 0.6 * acc.Assists;
        double gcAdj  = (rawGC + groupAvgGC * 3.0) / (acc.MatchesPlayed + 3.0);
        double normed = gcAdj / groupAvgGC;
        return Math.Clamp(normed / 2.0, 0.0, 1.0);
    }

    private static List<PlayerSynergyItem> BuildSynergyVisual(
        Guid playerId,
        HashSet<Guid> allPlayerIds,
        Dictionary<Guid, string> playerNameById,
        Dictionary<PairKey, PairAccumulator> pairTotals)
    {
        var list = new List<PlayerSynergyItem>(Math.Max(0, allPlayerIds.Count - 1));

        foreach (var otherId in allPlayerIds)
        {
            if (otherId == playerId) continue;

            var key = PairKey.Create(playerId, otherId);

            if (!pairTotals.TryGetValue(key, out var pairAcc) || pairAcc.MatchesTogether == 0)
            {
                list.Add(new PlayerSynergyItem
                {
                    WithPlayerId = otherId,
                    WithPlayerName = playerNameById.TryGetValue(otherId, out var n) ? n : otherId.ToString(),
                    MatchesTogether = 0,
                    WinsTogether = 0,
                    WinRateTogether = 0.0
                });

                continue;
            }

            bool playerIsA = playerId.CompareTo(otherId) <= 0;
            list.Add(new PlayerSynergyItem
            {
                WithPlayerId = otherId,
                WithPlayerName = playerNameById.TryGetValue(otherId, out var name) ? name : otherId.ToString(),
                MatchesTogether = pairAcc.MatchesTogether,
                WinsTogether = pairAcc.WinsTogether,
                WinRateTogether = pairAcc.MatchesTogether == 0
                    ? 0.0
                    : pairAcc.WinsTogether / (double)pairAcc.MatchesTogether,
                AssistsGiven    = playerIsA ? pairAcc.AssistsAtoB : pairAcc.AssistsBtoA,
                AssistsReceived = playerIsA ? pairAcc.AssistsBtoA : pairAcc.AssistsAtoB,
            });
        }

        return list;
    }

    private static List<PlayerVisualStatsItem> ApplyVisualRanks(List<PlayerVisualStatsItem> items)
    {
        var classificationRanks = BuildRanks(items, p => p.Points, p => p.Wins, p => p.GamesPlayed);
        var goalsRanks = BuildRanks(items, p => p.Goals);
        var assistsRanks = BuildRanks(items, p => p.Assists);
        var mvpsRanks = BuildRanks(items, p => p.Mvps);
        var mvpVotesRanks = BuildRanks(items, p => p.MvpVotes);
        var ownGoalsRanks = BuildRanks(items, p => p.OwnGoals);

        return items.Select(p => new PlayerVisualStatsItem
        {
            PlayerId = p.PlayerId,
            Name = p.Name,
            Status = p.Status,
            IsGoalkeeper = p.IsGoalkeeper,
            GamesPlayed = p.GamesPlayed,
            Wins = p.Wins,
            Ties = p.Ties,
            Losses = p.Losses,
            WinRate = p.WinRate,
            Points = p.Points,
            ClassificationRank = classificationRanks[p.PlayerId],
            Mvps = p.Mvps,
            MvpVotes = p.MvpVotes,
            MvpsRank = mvpsRanks[p.PlayerId],
            MvpVotesRank = mvpVotesRanks[p.PlayerId],
            Goals = p.Goals,
            Assists = p.Assists,
            OwnGoals = p.OwnGoals,
            GoalsRank = goalsRanks[p.PlayerId],
            AssistsRank = assistsRanks[p.PlayerId],
            OwnGoalsRank = p.OwnGoals > 0 ? ownGoalsRanks[p.PlayerId] : null,
            Synergies = p.Synergies
        }).ToList();
    }

    private static Dictionary<Guid, int> BuildRanks(
        IReadOnlyCollection<PlayerVisualStatsItem> items,
        Func<PlayerVisualStatsItem, int> primary,
        Func<PlayerVisualStatsItem, int>? secondary = null,
        Func<PlayerVisualStatsItem, int>? tertiary = null)
    {
        var ordered = items
            .OrderByDescending(primary)
            .ThenByDescending(secondary ?? (_ => 0))
            .ThenByDescending(tertiary ?? (_ => 0))
            .ThenBy(p => p.Name)
            .ToList();

        var ranks = new Dictionary<Guid, int>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var tied = i > 0
                && primary(current) == primary(ordered[i - 1])
                && (secondary?.Invoke(current) ?? 0) == (secondary?.Invoke(ordered[i - 1]) ?? 0)
                && (tertiary?.Invoke(current) ?? 0) == (tertiary?.Invoke(ordered[i - 1]) ?? 0);

            ranks[current.PlayerId] = tied ? ranks[ordered[i - 1].PlayerId] : i + 1;
        }

        return ranks;
    }

    private readonly record struct PairKey(Guid A, Guid B)
    {
        public static PairKey Create(Guid x, Guid y)
            => x.CompareTo(y) <= 0 ? new PairKey(x, y) : new PairKey(y, x);
    }

    private sealed class PlayerAccumulator
    {
        public int MatchesPlayed;
        public int Wins;
        public int Ties;
        public int Losses;
        public int Goals;
        public int Assists;
        public int OwnGoals;

        public static PlayerAccumulator Empty => new PlayerAccumulator();
    }

    private sealed class PairAccumulator
    {
        public int MatchesTogether;
        public int WinsTogether;
        public int TiesTogether;
        public int AssistsAtoB;  // A gave an assist to B
        public int AssistsBtoA;  // B gave an assist to A

        public static PairAccumulator Empty => new PairAccumulator();
    }

    private sealed class SpotlightOpponentAccumulator
    {
        public int Matches;
        public int WinsForA;
        public int WinsForB;
    }

    private enum MatchWinningTeam : byte
    {
        None = 0,
        TeamA = 1,
        TeamB = 2
    }

    private readonly record struct MatchOutcome(bool HasScore, bool IsTie, MatchWinningTeam WinningTeam)
    {
        public static MatchOutcome NoScore => new(false, false, MatchWinningTeam.None);
        public static MatchOutcome Tie => new(true, true, MatchWinningTeam.None);
        public static MatchOutcome Win(MatchWinningTeam winningTeam) => new(true, false, winningTeam);
    }
}
