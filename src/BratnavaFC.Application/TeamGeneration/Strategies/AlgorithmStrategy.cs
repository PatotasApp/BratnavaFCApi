using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BratnavaFC.Application.TeamGeneration;

/// <summary>
/// Team generator that tries multiple "seed pairs" (two initial players, one per team),
/// runs the draft for each seed pair, and picks the outcome with the smallest WinRate sum difference
/// (with small penalties for GK imbalance and small bonus for synergy).
///
/// All knobs are constants (edit this file to tune).
///
/// CHANGE: Players with fewer than X matches are treated as neutral:
/// - Their effective winrate becomes 0.50
/// - Their synergy becomes NeutralSynergy (0.50)
/// </summary>
public sealed class AlgorithmStrategy : ITeamGenerationStrategy
{
    // -----------------------------
    // TUNING KNOBS (edit here)
    // -----------------------------

    // Primary goal: minimize WinRate sum difference between teams (smaller is better).
    private const double BalanceWeight = 1.00;

    // GK imbalance penalty (difference in GK counts between teams).
    private const double GoalkeeperWeight = 0.60;

    // Synergy bonus (bigger synergy lowers score). Keep small so it never breaks balance too much.
    private const double SynergyWeight = 0.25;

    // If we don't know synergy, assume neutral.
    private const double NeutralSynergy = 0.50;

    // Seed pool: how many top WinRate players we consider when building seed pairs.
    // Set to int.MaxValue to allow seeds from ALL players.
    private const int SeedPoolTopN = int.MaxValue;

    // How many seed pairs we evaluate at most.
    // Set to int.MaxValue to test ALL possible pairs from the seed pool.
    // Minimum effective is 1.
    private const int MaxSeedPairsToEvaluate = int.MaxValue;

    // If true, also evaluates swapping the pair orientation (A->TeamA,B->TeamB AND B->TeamA,A->TeamB).
    private const bool EvaluateBothOrientations = true;

    // Tie-breaker: very small factor to prefer taking a higher WinRate when costs are equal.
    private const double TinyPreferHigherWinRate = 0.0001;

    // tolerância aceitável de desequilíbrio (winrate)
    private const double BalanceTolerance = 0.05;

    // -----------------------------
    // NEUTRAL RULE (insufficient sample)
    // -----------------------------
    // Se o jogador tiver menos que X partidas, ele é tratado como "neutro".
    private const int MinMatchesToBeNonNeutral = 3;

    // WinRate neutro (empate perfeito).
    private const double NeutralWinRate = 0.50;

    private static int TotalMatches(PlayerStats s) => (s?.Wins ?? 0) + (s?.Ties ?? 0) + (s?.Losses ?? 0);
    private static bool IsNeutral(PlayerStats s) => TotalMatches(s) < MinMatchesToBeNonNeutral;
    private static double EffectiveWinRate(PlayerStats s) => IsNeutral(s) ? NeutralWinRate : s.WinRate;

    // -----------------------------
    // DEPENDENCIES
    // -----------------------------
    private readonly IPlayerStatsService _statsService;
    private readonly ILogger _logger;

    public AlgorithmStrategy(
        IPlayerStatsService statsService,
        ILogger<AlgorithmStrategy> logger = null)
    {
        _statsService = statsService;
        _logger = logger;
    }

    public async Task<TeamsResultDto> GenerateTeamsAsync(List<PlayerEntity> players, TeamGenerationSettings settings)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (settings.PlayersPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(settings.PlayersPerTeam));

        var result = new TeamsResultDto([], [], []);
        if (players.Count == 0) return result;

        // candidates: optionally exclude goalkeepers
        var candidatePlayers = settings.IncludeGoalkeepers
            ? players.ToList()
            : players.Where(p => !p.IsGoalkeeper).ToList();

        var perTeam = settings.PlayersPerTeam;
        var maxAssignable = Math.Min(candidatePlayers.Count, perTeam * 2);

        if (maxAssignable == 0)
        {
            result.Unassigned.AddRange(players.Select(p => p.Id));
            return result;
        }

        // load stats
        var statsById = await LoadStatsByPlayerId(candidatePlayers).ConfigureAwait(false);

        // build ordered candidates (ORDER BY effective winrate, not raw winrate)
        var candidates = candidatePlayers
            .Select(p => new PlayerWithStats(p, GetOrCreateStats(statsById, p.Id)))
            .OrderByDescending(x => EffectiveWinRate(x.Stats))
            .ThenBy(x => x.PlayerEntity.Id)
            .ToList();

        // seed pool (TopN, or all if TopN is huge)
        var seedPoolCount = Math.Min(SeedPoolTopN, candidates.Count);
        var seedPool = candidates.Take(seedPoolCount).ToList();

        LogSeedPool(seedPool, settings, maxAssignable);

        // build seed pairs and choose which ones to evaluate (all or subset)
        var allPairs = BuildAllSeedPairs(seedPool);

        if (allPairs.Count == 0)
        {
            _logger?.LogInformation("[TeamGen] No seed pairs available. Using single greedy draft.");
            var single = RunGreedyDraft(candidates, perTeam, seedA: null, seedB: null, maxAssignable);
            FillResult(result, single, players, settings);
            return result;
        }

        var pairsToEvaluate = PickPairsToEvaluate(allPairs, seedPool);

        _logger?.LogInformation(
            "[TeamGen] SeedPairs total={TotalPairs} | evaluating={EvalPairs} | SeedPoolTopN={TopN} | MaxSeedPairsToEvaluate={MaxEval}",
            allPairs.Count, pairsToEvaluate.Count, SeedPoolTopN, MaxSeedPairsToEvaluate);

        DraftOutcome? best = null;

        foreach (var (a, b) in pairsToEvaluate)
        {
            // A -> TeamA, B -> TeamB
            var out1 = RunGreedyDraft(candidates, perTeam, a, b, maxAssignable);
            LogOutcome("Seed(A->A,B->B)", a, b, out1);
            best = PickBetter(best, out1);

            if (EvaluateBothOrientations)
            {
                // B -> TeamA, A -> TeamB
                var out2 = RunGreedyDraft(candidates, perTeam, b, a, maxAssignable);
                LogOutcome("Seed(B->A,A->B)", b, a, out2);
                best = PickBetter(best, out2);
            }
        }

        best ??= RunGreedyDraft(candidates, perTeam, seedA: null, seedB: null, maxAssignable);

        LogBest(best);

        FillResult(result, best, players, settings);
        return result;
    }

    // -----------------------------
    // SEEDS / PAIRS
    // -----------------------------

    private static List<(PlayerWithStats A, PlayerWithStats B)> BuildAllSeedPairs(List<PlayerWithStats> seedPool)
    {
        var pairs = new List<(PlayerWithStats, PlayerWithStats)>();
        if (seedPool.Count < 2) return pairs;

        for (int i = 0; i < seedPool.Count; i++)
            for (int j = i + 1; j < seedPool.Count; j++)
                pairs.Add((seedPool[i], seedPool[j]));

        return pairs;
    }

    /// <summary>
    /// Selects which seed pairs to evaluate.
    /// - If MaxSeedPairsToEvaluate is big enough, evaluates all.
    /// - Otherwise chooses a deterministic subset that "makes sense":
    ///   extremes (top vs bottom), second-top vs second-bottom, etc, then adjacent pairs.
    /// </summary>
    private static List<(PlayerWithStats A, PlayerWithStats B)> PickPairsToEvaluate(
        List<(PlayerWithStats A, PlayerWithStats B)> allPairs,
        List<PlayerWithStats> seedPoolSortedByWinRateDesc)
    {
        var maxEval = Math.Max(1, MaxSeedPairsToEvaluate);

        if (maxEval >= allPairs.Count || maxEval == int.MaxValue)
            return allPairs;

        // Build a "good coverage" list (extremes + adjacent)
        var selected = new List<(PlayerWithStats, PlayerWithStats)>();
        var used = new HashSet<(Guid, Guid)>();

        void AddPair(PlayerWithStats x, PlayerWithStats y)
        {
            if (x.PlayerEntity.Id == y.PlayerEntity.Id) return;

            // normalize key
            var a = x.PlayerEntity.Id;
            var b = y.PlayerEntity.Id;
            var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);

            if (used.Add(key))
                selected.Add((x, y));
        }

        // extremes: top vs bottom, second-top vs second-bottom...
        int n = seedPoolSortedByWinRateDesc.Count;
        for (int i = 0; i < n / 2 && selected.Count < maxEval; i++)
        {
            AddPair(seedPoolSortedByWinRateDesc[i], seedPoolSortedByWinRateDesc[n - 1 - i]);
        }

        // adjacent: (0,1), (1,2), (2,3)...
        for (int i = 0; i < n - 1 && selected.Count < maxEval; i++)
        {
            AddPair(seedPoolSortedByWinRateDesc[i], seedPoolSortedByWinRateDesc[i + 1]);
        }

        // if still not enough (small pool / duplicates), just fill from allPairs
        if (selected.Count < maxEval)
        {
            foreach (var p in allPairs)
            {
                if (selected.Count >= maxEval) break;

                var a = p.A.PlayerEntity.Id;
                var b = p.B.PlayerEntity.Id;
                var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                if (used.Add(key))
                    selected.Add(p);
            }
        }

        return selected;
    }

    // -----------------------------
    // DRAFT
    // -----------------------------

    private DraftOutcome RunGreedyDraft(
        List<PlayerWithStats> orderedCandidates,
        int perTeam,
        PlayerWithStats? seedA,
        PlayerWithStats? seedB,
        int maxAssignable)
    {
        var waiting = orderedCandidates.ToList();

        var teamA = new List<PlayerWithStats>(perTeam);
        var teamB = new List<PlayerWithStats>(perTeam);

        // apply seeds
        if (seedA is not null)
        {
            var s = waiting.FirstOrDefault(x => x.PlayerEntity.Id == seedA.PlayerEntity.Id);
            if (s is not null) { teamA.Add(s); waiting.Remove(s); }
        }

        if (seedB is not null)
        {
            var s = waiting.FirstOrDefault(x => x.PlayerEntity.Id == seedB.PlayerEntity.Id);
            if (s is not null) { teamB.Add(s); waiting.Remove(s); }
        }

        // if only one team has a seed, give the other team the best remaining to avoid a weird start
        if (teamA.Count == 0 && teamB.Count > 0 && waiting.Count > 0)
        {
            teamA.Add(waiting[0]);
            waiting.RemoveAt(0);
        }
        else if (teamB.Count == 0 && teamA.Count > 0 && waiting.Count > 0)
        {
            teamB.Add(waiting[0]);
            waiting.RemoveAt(0);
        }
        else if (teamA.Count == 0 && teamB.Count == 0 && waiting.Count > 0)
        {
            // no seeds: start with top 2 split
            teamA.Add(waiting[0]); waiting.RemoveAt(0);
            if (waiting.Count > 0) { teamB.Add(waiting[0]); waiting.RemoveAt(0); }
        }

        // draft until both teams filled or no more assignable
        while (waiting.Count > 0 && (teamA.Count + teamB.Count) < maxAssignable)
        {
            var pickForA = ShouldPickForTeamA(teamA, teamB, perTeam);
            var target = pickForA ? teamA : teamB;
            var other = pickForA ? teamB : teamA;

            if (target.Count >= perTeam)
                target = other;

            var bestIdx = SelectBestCandidateIndex(waiting, target, other);
            var chosen = waiting[bestIdx];

            target.Add(chosen);
            waiting.RemoveAt(bestIdx);
        }

        // compute outcome stats (use effective winrate)
        var sumA = teamA.Sum(x => EffectiveWinRate(x.Stats));
        var sumB = teamB.Sum(x => EffectiveWinRate(x.Stats));

        var outcome = new DraftOutcome(teamA, teamB, waiting)
        {
            BalanceDiff = Math.Abs(sumA - sumB),
            GoalkeeperDiff = Math.Abs(teamA.Count(x => x.PlayerEntity.IsGoalkeeper) - teamB.Count(x => x.PlayerEntity.IsGoalkeeper)),
            SynergyTotal = ComputeTeamSynergyTotal(teamA) + ComputeTeamSynergyTotal(teamB)
        };

        // Score: lower is better
        outcome.Score = (BalanceWeight * outcome.BalanceDiff)
                        + (GoalkeeperWeight * outcome.GoalkeeperDiff)
                        - (SynergyWeight * outcome.SynergyTotal);

        return outcome;
    }

    private static bool ShouldPickForTeamA(List<PlayerWithStats> teamA, List<PlayerWithStats> teamB, int perTeam)
    {
        if (teamA.Count < teamB.Count) return true;
        if (teamB.Count < teamA.Count) return false;

        var sumA = teamA.Sum(x => EffectiveWinRate(x.Stats));
        var sumB = teamB.Sum(x => EffectiveWinRate(x.Stats));

        return sumA <= sumB;
    }

    private int SelectBestCandidateIndex(
        List<PlayerWithStats> waiting,
        List<PlayerWithStats> targetTeam,
        List<PlayerWithStats> otherTeam)
    {
        var sumTarget = targetTeam.Sum(x => EffectiveWinRate(x.Stats));
        var sumOther = otherTeam.Sum(x => EffectiveWinRate(x.Stats));

        var gkTarget = targetTeam.Count(x => x.PlayerEntity.IsGoalkeeper);
        var gkOther = otherTeam.Count(x => x.PlayerEntity.IsGoalkeeper);

        double bestCost = double.PositiveInfinity;
        int bestIdx = 0;

        for (int i = 0; i < waiting.Count; i++)
        {
            var c = waiting[i];

            var cWr = EffectiveWinRate(c.Stats);

            var newSumTarget = sumTarget + cWr;
            var balanceAfter = Math.Abs(newSumTarget - sumOther);

            var synergyGain = ComputeSynergyGain(c, targetTeam);

            var newGkTarget = gkTarget + (c.PlayerEntity.IsGoalkeeper ? 1 : 0);
            var gkImbalanceAfter = Math.Abs(newGkTarget - gkOther);

            // Lower cost is better
            var cost = (BalanceWeight * balanceAfter)
                       + (GoalkeeperWeight * gkImbalanceAfter)
                       - (SynergyWeight * synergyGain);

            // tiny preference for higher WinRate when equal (use effective winrate)
            cost -= (cWr * TinyPreferHigherWinRate);

            if (cost < bestCost)
            {
                bestCost = cost;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    private double ComputeSynergyGain(PlayerWithStats candidate, List<PlayerWithStats> team)
    {
        if (team.Count == 0) return 0.0;

        double sum = 0;
        foreach (var member in team)
            sum += GetPairSynergy(candidate.Stats, member.PlayerEntity.Id, member.Stats);

        return sum;
    }

    private double GetPairSynergy(PlayerStats candidateStats, Guid memberId, PlayerStats memberStats)
    {
        // If any side has insufficient sample, treat synergy as neutral.
        if (IsNeutral(candidateStats) || IsNeutral(memberStats))
            return NeutralSynergy;

        if (candidateStats.SynergyWith is not null && candidateStats.SynergyWith.TryGetValue(memberId, out var v))
            return Clamp01(v);

        if (memberStats.SynergyWith is not null && memberStats.SynergyWith.TryGetValue(candidateStats.PlayerId, out var v2))
            return Clamp01(v2);

        return NeutralSynergy;
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private double ComputeTeamSynergyTotal(List<PlayerWithStats> team)
    {
        if (team.Count < 2) return 0.0;

        double sum = 0.0;
        for (int i = 0; i < team.Count; i++)
            for (int j = i + 1; j < team.Count; j++)
                sum += GetPairSynergy(team[i].Stats, team[j].PlayerEntity.Id, team[j].Stats);

        return sum;
    }

    private static DraftOutcome PickBetter(
            DraftOutcome? current,
            DraftOutcome challenger)
    {
        if (current is null)
            return challenger;

        // 1️⃣ Menor BalanceDiff SEMPRE vence
        if (challenger.BalanceDiff < current.BalanceDiff - BalanceTolerance)
            return challenger;

        if (current.BalanceDiff < challenger.BalanceDiff - BalanceTolerance)
            return current;

        // 2️⃣ Balance praticamente igual → menor GKDiff
        if (challenger.GoalkeeperDiff < current.GoalkeeperDiff)
            return challenger;

        if (current.GoalkeeperDiff < challenger.GoalkeeperDiff)
            return current;

        // 3️⃣ Sinergia maior vence
        if (challenger.SynergyTotal > current.SynergyTotal)
            return challenger;

        if (current.SynergyTotal > challenger.SynergyTotal)
            return current;

        // 4️⃣ Último desempate: soma total de winrate maior (effective winrate)
        var sumCurrent =
            current.TeamA.Sum(x => EffectiveWinRate(x.Stats)) +
            current.TeamB.Sum(x => EffectiveWinRate(x.Stats));

        var sumChallenger =
            challenger.TeamA.Sum(x => EffectiveWinRate(x.Stats)) +
            challenger.TeamB.Sum(x => EffectiveWinRate(x.Stats));

        return sumChallenger > sumCurrent ? challenger : current;
    }

    // -----------------------------
    // STATS LOADING
    // -----------------------------

    private async Task<Dictionary<Guid, PlayerStats>> LoadStatsByPlayerId(List<PlayerEntity> players)
    {
        var statsList = await _statsService.EnrichPlayersAsync(players).ConfigureAwait(false);
        return statsList.ToDictionary(s => s.PlayerId, s => s);
    }

    private static PlayerStats GetOrCreateStats(Dictionary<Guid, PlayerStats> statsByPlayerId, Guid playerId)
        => statsByPlayerId.TryGetValue(playerId, out var s)
            ? s
            : new PlayerStats { PlayerId = playerId, Wins = 0, Ties = 0, Losses = 0, WinRate = 0.0, SynergyWith = new() };

    // -----------------------------
    // RESULT FILL
    // -----------------------------

    private static void FillResult(TeamsResultDto result, DraftOutcome best, List<PlayerEntity> allPlayers, TeamGenerationSettings settings)
    {
        result.TeamA.Clear();
        result.TeamB.Clear();
        result.Unassigned.Clear();

        result.TeamA.AddRange(best.TeamA.Select(x => x.PlayerEntity.Id));
        result.TeamB.AddRange(best.TeamB.Select(x => x.PlayerEntity.Id));
        result.Unassigned.AddRange(best.Waiting.Select(x => x.PlayerEntity.Id));

        if (!settings.IncludeGoalkeepers)
            result.Unassigned.AddRange(allPlayers.Where(p => p.IsGoalkeeper).Select(p => p.Id));
    }

    // -----------------------------
    // LOGGING
    // -----------------------------

    private void LogSeedPool(List<PlayerWithStats> seedPool, TeamGenerationSettings settings, int maxAssignable)
    {
        if (_logger is null) return;

        var pool = string.Join(", ",
            seedPool.Select(p =>
            {
                var wr = EffectiveWinRate(p.Stats);
                var m = TotalMatches(p.Stats);
                var neutral = IsNeutral(p.Stats) ? ",NEUTRAL" : "";
                return $"{p.PlayerEntity.Name}(wr={wr:0.000},m={m}{neutral}{(p.PlayerEntity.IsGoalkeeper ? ",GK" : "")})";
            }));

        _logger.LogInformation(
            "[TeamGen] SeedPool Top{TopN} | PlayersPerTeam={PerTeam} IncludeGoalkeepers={IncGK} MaxAssignable={MaxAssign} | Pool: {Pool}",
            seedPool.Count, settings.PlayersPerTeam, settings.IncludeGoalkeepers, maxAssignable, pool);
    }

    private void LogOutcome(string label, PlayerWithStats? seedA, PlayerWithStats? seedB, DraftOutcome outcome)
    {
        if (_logger is null) return;

        var seedTxt = $"{FmtSeed(seedA)} vs {FmtSeed(seedB)}";

        _logger.LogInformation(
            "[TeamGen] {Label} | Seeds: {Seeds} | Score={Score:0.000} | BalanceDiff={Balance:0.000} | GKDiff={GKDiff} | SynergyTotal={Syn:0.000}",
            label, seedTxt, outcome.Score, outcome.BalanceDiff, outcome.GoalkeeperDiff, outcome.SynergyTotal);

        _logger.LogInformation("[TeamGen] TeamA: {TeamA}", FormatTeam(outcome.TeamA));
        _logger.LogInformation("[TeamGen] TeamB: {TeamB}", FormatTeam(outcome.TeamB));
        if (outcome.Waiting.Count > 0)
            _logger.LogInformation("[TeamGen] Unassigned: {Unassigned}", FormatTeam(outcome.Waiting));
    }

    private void LogBest(DraftOutcome best)
    {
        if (_logger is null) return;

        _logger.LogInformation(
            "[TeamGen] BEST CHOSEN | Score={Score:0.000} | BalanceDiff={Balance:0.000} | GKDiff={GKDiff} | SynergyTotal={Syn:0.000}",
            best.Score, best.BalanceDiff, best.GoalkeeperDiff, best.SynergyTotal);

        _logger.LogInformation("[TeamGen] BEST TeamA: {TeamA}", FormatTeam(best.TeamA));
        _logger.LogInformation("[TeamGen] BEST TeamB: {TeamB}", FormatTeam(best.TeamB));
    }

    private static string FmtSeed(PlayerWithStats? s)
    {
        if (s is null) return "null";

        var wr = EffectiveWinRate(s.Stats);
        var m = TotalMatches(s.Stats);
        var neutral = IsNeutral(s.Stats) ? ",NEUTRAL" : "";
        return $"{s.PlayerEntity.Name}(wr={wr:0.000},m={m}{neutral}{(s.PlayerEntity.IsGoalkeeper ? ",GK" : "")})";
    }

    private static string FormatTeam(List<PlayerWithStats> team)
        => string.Join(", ", team.Select(p =>
        {
            var wr = EffectiveWinRate(p.Stats);
            var m = TotalMatches(p.Stats);
            var neutral = IsNeutral(p.Stats) ? ",NEUTRAL" : "";
            return $"{p.PlayerEntity.Name}(wr={wr:0.000},m={m}{neutral}{(p.PlayerEntity.IsGoalkeeper ? ",GK" : "")})";
        }));

    // -----------------------------
    // INTERNAL TYPES
    // -----------------------------

    private sealed class PlayerWithStats
    {
        public PlayerEntity PlayerEntity { get; }
        public PlayerStats Stats { get; }

        public PlayerWithStats(PlayerEntity playerEntity, PlayerStats stats)
        {
            PlayerEntity = playerEntity ?? throw new ArgumentNullException(nameof(playerEntity));
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }
    }

    private sealed class DraftOutcome
    {
        public List<PlayerWithStats> TeamA { get; }
        public List<PlayerWithStats> TeamB { get; }
        public List<PlayerWithStats> Waiting { get; }

        public double Score { get; set; }
        public double BalanceDiff { get; set; }
        public int GoalkeeperDiff { get; set; }
        public double SynergyTotal { get; set; }

        public DraftOutcome(List<PlayerWithStats> a, List<PlayerWithStats> b, List<PlayerWithStats> waiting)
        {
            TeamA = a;
            TeamB = b;
            Waiting = waiting;
        }
    }
}
