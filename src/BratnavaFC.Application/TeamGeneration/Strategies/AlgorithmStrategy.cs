using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

/// <summary>
/// Team generator that evaluates multiple "seed pairs" (two initial players, one per team),
/// runs a greedy draft for each, and returns the top N outcomes ranked by composite score:
///   Score = BalanceDiff × <see cref="BalanceWeight"/>
///         + GKDiff      × <see cref="GoalkeeperWeight"/>
///         - Synergy     × <see cref="SynergyWeight"/>
///
/// All tuning knobs are constants at the top of this file.
/// Players with fewer than <see cref="MinMatchesToBeNonNeutral"/> matches are treated as neutral
/// (effective WinRate → 0.50, or their star-rating override if set).
/// </summary>
public sealed class AlgorithmStrategy : ITeamGenerationStrategy
{
    // ── Tuning knobs (edit here) ──────────────────────────────────────────────

    /// <summary>Primary goal: minimise WinRate sum difference (lower is better).</summary>
    private const double BalanceWeight = 1.00;

    /// <summary>Penalty per unit of GK-count imbalance between teams.</summary>
    private const double GoalkeeperWeight = 0.60;

    /// <summary>Synergy bonus (larger synergy lowers score). Kept small so it never overrides balance.</summary>
    private const double SynergyWeight = 0.25;

    /// <summary>Neutral synergy value when pair history is insufficient.</summary>
    private const double NeutralSynergy = 0.50;

    /// <summary>How many top-WinRate players form the seed pool. <see cref="int.MaxValue"/> = all players.</summary>
    private const int SeedPoolTopN = int.MaxValue;

    /// <summary>Maximum seed pairs to evaluate. <see cref="int.MaxValue"/> = every possible pair.</summary>
    private const int MaxSeedPairsToEvaluate = int.MaxValue;

    /// <summary>When true, also evaluates swapping the pair orientation (A↔B).</summary>
    private const bool EvaluateBothOrientations = true;

    /// <summary>Tiny preference for higher WinRate when costs are equal (tie-breaker only).</summary>
    private const double TinyPreferHigherWinRate = 0.0001;

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IPlayerStatsService        _statsService;
    private readonly ILogger<AlgorithmStrategy> _logger;

    public AlgorithmStrategy(
        IPlayerStatsService statsService,
        ILogger<AlgorithmStrategy>? logger = null)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
        _logger       = logger ?? NullLogger<AlgorithmStrategy>.Instance;
    }

    // ── Public entry-point ────────────────────────────────────────────────────

    public async Task<TeamsOptionsResultDto> GenerateTeamsAsync(
        List<PlayerRequestDto> players,
        TeamGenerationSettings settings,
        int optionsCount = 3,
        CancellationToken cancellationToken = default)
    {
        if (players is null) throw new ArgumentNullException(nameof(players));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (settings.PlayersPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(settings.PlayersPerTeam));

        optionsCount = Math.Max(1, optionsCount);

        if (players.Count == 0)
            return new TeamsOptionsResultDto([]);

        List<PlayerRequestDto>        candidates    = FilterCandidates(players, settings);
        int                           perTeam       = settings.PlayersPerTeam;
        int                           maxAssignable = Math.Min(candidates.Count, perTeam * 2);

        if (maxAssignable == 0)
            return BuildEmptyResult(players);

        Dictionary<Guid, PlayerStats> statsById = await LoadStatsByPlayerId(_statsService, candidates, cancellationToken);

        // Order by effective WinRate desc, then Id asc for deterministic tie-breaking
        List<CandidatePlayer> ranked = candidates
            .Select(p => new CandidatePlayer(p, GetOrCreateStats(statsById, p.Id, p.Name)))
            .OrderByDescending(x => EffectiveWinRate(x.Stats))
            .ThenBy(x => x.Player.Id)
            .ToList();

        List<CandidatePlayer>                    seedPool = ranked.Take(Math.Min(SeedPoolTopN, ranked.Count)).ToList();
        List<(CandidatePlayer A, CandidatePlayer B)> allPairs = BuildAllSeedPairs(seedPool);

        LogSeedPool(seedPool, settings, maxAssignable);

        if (allPairs.Count == 0)
        {
            _logger.LogInformation("[TeamGen] No seed pairs available — using single greedy draft.");
            DraftOutcome single = RunGreedyDraft(ranked, perTeam, seedA: null, seedB: null, maxAssignable);
            return new TeamsOptionsResultDto([BuildOption(single, players, settings)]);
        }

        List<(CandidatePlayer A, CandidatePlayer B)> pairsToEvaluate = PickPairsToEvaluate(allPairs, seedPool);

        _logger.LogInformation(
            "[TeamGen] SeedPairs total={Total} | evaluating={Eval} | SeedPoolTopN={TopN} | MaxPairs={Max}",
            allPairs.Count, pairsToEvaluate.Count, SeedPoolTopN, MaxSeedPairsToEvaluate);

        // Evaluate all selected pairs (and their swapped orientations)
        HashSet<string>    seen     = new HashSet<string>();
        List<DraftOutcome> outcomes = new List<DraftOutcome>();

        void TryAdd(DraftOutcome o)
        {
            string key = BuildTeamsKey(
                o.TeamA.Select(x => x.Player.Id),
                o.TeamB.Select(x => x.Player.Id));

            if (seen.Add(key)) outcomes.Add(o);
        }

        foreach ((CandidatePlayer a, CandidatePlayer b) in pairsToEvaluate)
        {
            TryAdd(RunGreedyDraft(ranked, perTeam, a, b, maxAssignable));

            if (EvaluateBothOrientations)
                TryAdd(RunGreedyDraft(ranked, perTeam, b, a, maxAssignable));
        }

        if (outcomes.Count == 0)
            outcomes.Add(RunGreedyDraft(ranked, perTeam, seedA: null, seedB: null, maxAssignable));

        // Return the top N outcomes (lowest Score is best)
        List<DraftOutcome>  top     = outcomes.OrderBy(o => o.Score).Take(optionsCount).ToList();
        List<TeamOptionDto> options = top.Select(o => BuildOption(o, players, settings)).ToList();

        LogBest(top[0]);

        return new TeamsOptionsResultDto(options);
    }

    // ── DTO builder ───────────────────────────────────────────────────────────

    private TeamOptionDto BuildOption(
        DraftOutcome best,
        List<PlayerRequestDto> allPlayers,
        TeamGenerationSettings settings)
    {
        List<PlayerWeightDto> teamA = best.TeamA.Select(x => new PlayerWeightDto(x.Player.Id, EffectiveWinRate(x.Stats))).ToList();
        List<PlayerWeightDto> teamB = best.TeamB.Select(x => new PlayerWeightDto(x.Player.Id, EffectiveWinRate(x.Stats))).ToList();

        // Unassigned = waiting-list players + GKs excluded from candidates
        HashSet<Guid> unassignedIds = best.Waiting.Select(x => x.Player.Id).ToHashSet();
        if (!settings.IncludeGoalkeepers)
            foreach (PlayerRequestDto gk in allPlayers.Where(p => p.IsGoalkeeper))
                unassignedIds.Add(gk.Id);

        Dictionary<Guid, PlayerStats> knownStats = best.TeamA.Concat(best.TeamB).Concat(best.Waiting)
            .ToDictionary(x => x.Player.Id, x => x.Stats);

        List<PlayerWeightDto> unassigned = unassignedIds
            .Select(id => knownStats.TryGetValue(id, out PlayerStats? s)
                ? new PlayerWeightDto(id, EffectiveWinRate(s))
                : new PlayerWeightDto(id, NeutralWinRate))   // GK excluded from draft → neutral weight
            .DistinctBy(x => x.PlayerId)
            .ToList();

        return new TeamOptionDto(
            TeamA: teamA,
            TeamB: teamB,
            Unassigned: unassigned,
            TeamAWeight: teamA.Sum(x => x.Weight),
            TeamBWeight: teamB.Sum(x => x.Weight),
            BalanceDiff: best.BalanceDiff,
            GoalkeeperDiff: best.GoalkeeperDiff,
            SynergyTotal: best.SynergyTotal,
            Score: best.Score
        );
    }

    // ── Seed pairs ────────────────────────────────────────────────────────────

    private static List<(CandidatePlayer A, CandidatePlayer B)> BuildAllSeedPairs(
        List<CandidatePlayer> seedPool)
    {
        List<(CandidatePlayer, CandidatePlayer)> pairs = new List<(CandidatePlayer, CandidatePlayer)>();

        for (int i = 0; i < seedPool.Count; i++)
            for (int j = i + 1; j < seedPool.Count; j++)
                pairs.Add((seedPool[i], seedPool[j]));

        return pairs;
    }

    /// <summary>
    /// When <see cref="MaxSeedPairsToEvaluate"/> is large enough, evaluates all pairs.
    /// Otherwise selects a deterministic coverage subset: extremes first, then adjacent, then fill.
    /// </summary>
    private static List<(CandidatePlayer A, CandidatePlayer B)> PickPairsToEvaluate(
        List<(CandidatePlayer A, CandidatePlayer B)> allPairs,
        List<CandidatePlayer> sortedByWinRateDesc)
    {
        int maxEval = Math.Max(1, MaxSeedPairsToEvaluate);

        if (maxEval >= allPairs.Count || maxEval == int.MaxValue)
            return allPairs;

        List<(CandidatePlayer, CandidatePlayer)> selected = new List<(CandidatePlayer, CandidatePlayer)>();
        HashSet<(Guid, Guid)>                    used     = new HashSet<(Guid, Guid)>();

        void AddPair(CandidatePlayer x, CandidatePlayer y)
        {
            if (x.Player.Id == y.Player.Id) return;

            (Guid, Guid) key = x.Player.Id.CompareTo(y.Player.Id) < 0
                ? (x.Player.Id, y.Player.Id)
                : (y.Player.Id, x.Player.Id);

            if (used.Add(key))
                selected.Add((x, y));
        }

        int n = sortedByWinRateDesc.Count;

        // Extremes: best vs worst, second-best vs second-worst, …
        for (int i = 0; i < n / 2 && selected.Count < maxEval; i++)
            AddPair(sortedByWinRateDesc[i], sortedByWinRateDesc[n - 1 - i]);

        // Adjacent: (0,1), (1,2), …
        for (int i = 0; i < n - 1 && selected.Count < maxEval; i++)
            AddPair(sortedByWinRateDesc[i], sortedByWinRateDesc[i + 1]);

        // Fill remainder from the full list
        foreach ((CandidatePlayer A, CandidatePlayer B) p in allPairs)
        {
            if (selected.Count >= maxEval) break;

            (Guid, Guid) key = p.A.Player.Id.CompareTo(p.B.Player.Id) < 0
                ? (p.A.Player.Id, p.B.Player.Id)
                : (p.B.Player.Id, p.A.Player.Id);

            if (used.Add(key))
                selected.Add(p);
        }

        return selected;
    }

    // ── Greedy draft ──────────────────────────────────────────────────────────

    private DraftOutcome RunGreedyDraft(
        List<CandidatePlayer> ranked,
        int perTeam,
        CandidatePlayer? seedA,
        CandidatePlayer? seedB,
        int maxAssignable)
    {
        List<CandidatePlayer> waiting = ranked.ToList();
        List<CandidatePlayer> teamA   = new List<CandidatePlayer>(perTeam);
        List<CandidatePlayer> teamB   = new List<CandidatePlayer>(perTeam);

        // Place seeds
        PlaceSeed(seedA, teamA, waiting);
        PlaceSeed(seedB, teamB, waiting);

        // If only one team has a seed, give the other the best remaining
        if      (teamA.Count == 0 && teamB.Count > 0 && waiting.Count > 0)
            { teamA.Add(waiting[0]); waiting.RemoveAt(0); }
        else if (teamB.Count == 0 && teamA.Count > 0 && waiting.Count > 0)
            { teamB.Add(waiting[0]); waiting.RemoveAt(0); }
        else if (teamA.Count == 0 && teamB.Count == 0 && waiting.Count > 0)
        {
            // No seeds at all: split top-2
            teamA.Add(waiting[0]); waiting.RemoveAt(0);
            if (waiting.Count > 0) { teamB.Add(waiting[0]); waiting.RemoveAt(0); }
        }

        // Greedy fill — always pick for the weaker / smaller team
        while (waiting.Count > 0 && teamA.Count + teamB.Count < maxAssignable)
        {
            bool                  pickForA = PickForTeamA(teamA, teamB, perTeam);
            List<CandidatePlayer> target   = pickForA ? teamA : teamB;
            List<CandidatePlayer> other    = pickForA ? teamB : teamA;

            if (target.Count >= perTeam) target = other;

            int             idx    = BestCandidateIndex(waiting, target, other);
            CandidatePlayer chosen = waiting[idx];
            target.Add(chosen);
            waiting.RemoveAt(idx);
        }

        return ToOutcome(teamA, teamB, waiting);
    }

    private static void PlaceSeed(
        CandidatePlayer? seed,
        List<CandidatePlayer> team,
        List<CandidatePlayer> waiting)
    {
        if (seed is null) return;

        CandidatePlayer? found = waiting.FirstOrDefault(x => x.Player.Id == seed.Player.Id);
        if (found is not null) { team.Add(found); waiting.Remove(found); }
    }

    private static bool PickForTeamA(
        List<CandidatePlayer> teamA,
        List<CandidatePlayer> teamB,
        int perTeam)
    {
        if (teamA.Count != teamB.Count)
            return teamA.Count < teamB.Count;

        // Equal size: pick for the weaker team
        return teamA.Sum(x => EffectiveWinRate(x.Stats)) <= teamB.Sum(x => EffectiveWinRate(x.Stats));
    }

    private int BestCandidateIndex(
        List<CandidatePlayer> waiting,
        List<CandidatePlayer> target,
        List<CandidatePlayer> other)
    {
        double sumTarget = target.Sum(x => EffectiveWinRate(x.Stats));
        double sumOther  = other.Sum(x => EffectiveWinRate(x.Stats));
        int    gkTarget  = target.Count(x => x.Player.IsGoalkeeper);
        int    gkOther   = other.Count(x => x.Player.IsGoalkeeper);

        double bestCost = double.PositiveInfinity;
        int    bestIdx  = 0;

        for (int i = 0; i < waiting.Count; i++)
        {
            CandidatePlayer c   = waiting[i];
            double          cWr = EffectiveWinRate(c.Stats);

            double cost = (BalanceWeight    * Math.Abs(sumTarget + cWr - sumOther))
                        + (GoalkeeperWeight * Math.Abs(gkTarget + (c.Player.IsGoalkeeper ? 1 : 0) - gkOther))
                        - (SynergyWeight    * SynergyGain(c, target))
                        - (cWr             * TinyPreferHigherWinRate);  // tie-breaker

            if (cost < bestCost) { bestCost = cost; bestIdx = i; }
        }

        return bestIdx;
    }

    private DraftOutcome ToOutcome(
        List<CandidatePlayer> teamA,
        List<CandidatePlayer> teamB,
        List<CandidatePlayer> waiting)
    {
        double balanceDiff  = Math.Abs(
            teamA.Sum(x => EffectiveWinRate(x.Stats)) -
            teamB.Sum(x => EffectiveWinRate(x.Stats)));

        int gkDiff          = Math.Abs(
            teamA.Count(x => x.Player.IsGoalkeeper) -
            teamB.Count(x => x.Player.IsGoalkeeper));

        double synergyTotal = TeamSynergy(teamA) + TeamSynergy(teamB);

        double score        = BalanceWeight    * balanceDiff
                            + GoalkeeperWeight * gkDiff
                            - SynergyWeight    * synergyTotal;

        return new DraftOutcome(teamA, teamB, waiting, score, balanceDiff, gkDiff, synergyTotal);
    }

    // ── Synergy ───────────────────────────────────────────────────────────────

    private double SynergyGain(CandidatePlayer candidate, List<CandidatePlayer> team)
        => team.Sum(member => PairSynergy(candidate.Stats, member.Player.Id, member.Stats));

    private double TeamSynergy(List<CandidatePlayer> team)
    {
        double sum = 0.0;

        for (int i = 0; i < team.Count; i++)
            for (int j = i + 1; j < team.Count; j++)
                sum += PairSynergy(team[i].Stats, team[j].Player.Id, team[j].Stats);

        return sum;
    }

    private double PairSynergy(PlayerStats a, Guid bId, PlayerStats b)
    {
        if (IsNeutral(a) || IsNeutral(b)) return NeutralSynergy;

        if (a.SynergyWith?.TryGetValue(bId, out double v1)        == true) return Math.Clamp(v1, 0.0, 1.0);
        if (b.SynergyWith?.TryGetValue(a.PlayerId, out double v2) == true) return Math.Clamp(v2, 0.0, 1.0);

        return NeutralSynergy;
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private void LogSeedPool(List<CandidatePlayer> pool, TeamGenerationSettings settings, int maxAssignable)
    {
        if (_logger == NullLogger<AlgorithmStrategy>.Instance) return;

        _logger.LogInformation(
            "[TeamGen] SeedPool({Size}) | PerTeam={PerTeam} IncludeGK={IncGK} MaxAssignable={Max} | {Pool}",
            pool.Count, settings.PlayersPerTeam, settings.IncludeGoalkeepers, maxAssignable,
            string.Join(", ", pool.Select(Fmt)));
    }

    private void LogBest(DraftOutcome best)
    {
        if (_logger == NullLogger<AlgorithmStrategy>.Instance) return;

        _logger.LogInformation(
            "[TeamGen] BEST | Score={Score:0.000} Balance={Bal:0.000} GKDiff={GK} Synergy={Syn:0.000}",
            best.Score, best.BalanceDiff, best.GoalkeeperDiff, best.SynergyTotal);

        _logger.LogInformation("[TeamGen] TeamA: {A}", FmtTeam(best.TeamA));
        _logger.LogInformation("[TeamGen] TeamB: {B}", FmtTeam(best.TeamB));
    }

    private static string Fmt(CandidatePlayer p)
    {
        double wr      = EffectiveWinRate(p.Stats);
        int    m       = TotalMatches(p.Stats);
        string neutral = IsNeutral(p.Stats) ? ",N" : "";
        string gk      = p.Player.IsGoalkeeper ? ",GK" : "";
        return $"{p.Player.Name}(wr={wr:0.00},m={m}{neutral}{gk})";
    }

    private static string FmtTeam(List<CandidatePlayer> team)
        => string.Join(", ", team.Select(Fmt));

    // ── Internal types ────────────────────────────────────────────────────────

    private sealed class DraftOutcome(
        List<CandidatePlayer> teamA,
        List<CandidatePlayer> teamB,
        List<CandidatePlayer> waiting,
        double score,
        double balanceDiff,
        int goalkeeperDiff,
        double synergyTotal)
    {
        public List<CandidatePlayer> TeamA          { get; } = teamA;
        public List<CandidatePlayer> TeamB          { get; } = teamB;
        public List<CandidatePlayer> Waiting        { get; } = waiting;
        public double                Score          { get; } = score;
        public double                BalanceDiff    { get; } = balanceDiff;
        public int                   GoalkeeperDiff { get; } = goalkeeperDiff;
        public double                SynergyTotal   { get; } = synergyTotal;
    }
}
