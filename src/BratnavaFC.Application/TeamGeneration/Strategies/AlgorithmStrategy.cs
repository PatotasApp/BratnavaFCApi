using System.Text;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

/// <summary>
/// Team generator that evaluates multiple "seed pairs" (two starting anchors, one per team),
/// runs an exhaustive draft search for each, and returns the top N outcomes ranked by:
///
///   Primary   : smallest BalanceDiff (W_base sum difference between teams)
///   Tiebreaker: largest SynergyTotal
///
/// Goalkeeper distribution is enforced as a hard structural constraint, not a score component:
/// when ≥ 2 eligible goalkeepers are present, each team receives exactly one.
///
/// <see cref="SearchBestDraftExhaustive"/> explores every valid branch of the assignment tree,
/// evaluating exactly C(n, k) terminal states per seed pair, where n is the count of remaining
/// players and k is the number of open spots in each team.
///
/// Each seed pair contributes its top-K terminal outcomes to a shared pool.
/// Duplicate team compositions across all pairs are removed via canonical key deduplication,
/// and the best <c>optionsCount</c> unique outcomes are returned.
///
/// Players with fewer than <see cref="MinMatchesToBeNonNeutral"/> matches are treated as neutral
/// (effective weight → 0.50, or their guest star-rating override when set).
/// </summary>
public sealed class AlgorithmStrategy : ITeamGenerationStrategy
{
    // ── Scoring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Score = BalanceDiff (W_base sum difference between teams).
    /// Synergy is used only as an external tiebreaker via OrderBy; it does not enter the score.
    /// Goalkeeper distribution is a structural constraint, not a score component.
    /// </summary>
    private const double BalanceWeight = 1.00;

    // ── Search tuning ─────────────────────────────────────────────────────────

    /// <summary>
    /// Synergy returned when a pair lacks sufficient shared history.
    /// 0.0 = no bonus/penalty ("no data", not "average performance").
    /// </summary>
    private const double NeutralSynergy = 0.0;

    /// <summary>How many top-weight candidates are eligible as seeds. <see cref="int.MaxValue"/> = all.</summary>
    private const int SeedPoolTopN = int.MaxValue;

    /// <summary>Maximum seed pairs to search. <see cref="int.MaxValue"/> = every possible pair.</summary>
    private const int MaxSeedPairsToEvaluate = int.MaxValue;

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
        if (players is null)              throw new ArgumentNullException(nameof(players));
        if (settings is null)             throw new ArgumentNullException(nameof(settings));
        if (settings.PlayersPerTeam <= 0) throw new ArgumentOutOfRangeException(nameof(settings.PlayersPerTeam));

        // Synergy is always active: it is a structural tiebreaker, not an optional score component.
        const bool considerSynergy = true;
        optionsCount = Math.Max(1, optionsCount);

        if (players.Count == 0)
            return new TeamsOptionsResultDto([]);

        List<PlayerRequestDto>        candidates    = FilterCandidates(players, settings);
        int                           perTeam       = settings.PlayersPerTeam;
        int                           maxAssignable = Math.Min(candidates.Count, perTeam * 2);

        if (maxAssignable == 0)
            return BuildEmptyResult(players);

        Dictionary<Guid, PlayerStats> statsById = await LoadStatsByPlayerId(_statsService, candidates, cancellationToken);

        // Rank candidates by effective weight descending; break ties by Id for determinism.
        List<CandidatePlayer> candidatesByStrength = candidates
            .Select(p => new CandidatePlayer(p, GetOrCreateStats(statsById, p.Id, p.Name)))
            .OrderByDescending(x => EffectiveWeight(x.Stats))
            .ThenBy(x => x.Player.Id)
            .ToList();

        // Number of eligible GKs drives the hard goalkeeper-distribution constraint.
        int eligibleGkCount = candidatesByStrength.Count(p => p.Player.IsGoalkeeper);

        // Local delegate: captures considerSynergy — no shared mutable field, fully thread-safe.
        Func<PlayerStats, Guid, PlayerStats, double> pairSynergyFn =
            (a, bid, b) => ComputePairSynergy(a, bid, b, considerSynergy);

        List<CandidatePlayer>                                seedPool     = candidatesByStrength.Take(Math.Min(SeedPoolTopN, candidatesByStrength.Count)).ToList();
        List<(CandidatePlayer SeedA, CandidatePlayer SeedB)> allSeedPairs = BuildAllPairs(seedPool);

        LogSearchSetup(seedPool, settings, maxAssignable);

        if (allSeedPairs.Count == 0)
        {
            _logger.LogInformation("[TeamGen] No seed pairs — running single unseeded exhaustive draft.");
            List<DraftOutcome> unseededOutcomes = SearchBestDraftExhaustive(
                candidatesByStrength, perTeam, null, null, maxAssignable, eligibleGkCount,
                pairSynergyFn, optionsCount, out int unseededScenarios);
            _logger.LogInformation("[TeamGen] Unseeded draft | scenarios={Scenarios}", unseededScenarios);
            return new TeamsOptionsResultDto(
                unseededOutcomes.Select(o => BuildTeamOption(o, players, settings, considerSynergy)).ToList());
        }

        List<(CandidatePlayer SeedA, CandidatePlayer SeedB)> selectedPairs =
            SelectPairsToSearch(allSeedPairs, seedPool);

        _logger.LogInformation(
            "[TeamGen] SeedPairs total={Total} | evaluating={Eval}",
            allSeedPairs.Count, selectedPairs.Count);

        List<DraftOutcome> uniqueOutcomes = EvaluateSeedPairs(
            candidatesByStrength, selectedPairs, perTeam, maxAssignable, eligibleGkCount,
            pairSynergyFn, optionsCount);

        // Balance is the primary criterion; synergy is the tiebreaker.
        List<DraftOutcome> bestOutcomes = uniqueOutcomes
            .OrderBy(o => o.BalanceDiff)
            .ThenByDescending(o => o.SynergyTotal)
            .Take(optionsCount)
            .ToList();

        List<TeamOptionDto> teamOptions = bestOutcomes
            .Select(o => BuildTeamOption(o, players, settings, considerSynergy))
            .ToList();

        LogBestResult(bestOutcomes[0]);

        return new TeamsOptionsResultDto(teamOptions);
    }

    // ── Seed-pair evaluation ──────────────────────────────────────────────────

    /// <summary>
    /// Searches each seed pair exhaustively, collecting up to <paramref name="optionsCount"/> distinct
    /// outcomes per pair. All outcomes across all pairs are deduplicated by canonical team key and
    /// returned as a flat list, ready for the caller to sort and take the best N.
    ///
    /// Falls back to an unseeded search when all pairs converge to the same composition.
    ///
    /// Note: only one orientation per pair is searched because <see cref="BuildTeamsKey"/>
    /// already canonicalises A↔B — swapping the seeds would always produce a duplicate.
    /// </summary>
    private List<DraftOutcome> EvaluateSeedPairs(
        List<CandidatePlayer> candidatesByStrength,
        List<(CandidatePlayer SeedA, CandidatePlayer SeedB)> pairs,
        int perTeam,
        int maxAssignable,
        int eligibleGkCount,
        Func<PlayerStats, Guid, PlayerStats, double> pairSynergyFn,
        int optionsCount)
    {
        var evaluatedKeys  = new HashSet<string>();
        var uniqueOutcomes = new List<DraftOutcome>();
        int globalScenarios  = 0;
        int globalDuplicates = 0;

        foreach ((CandidatePlayer seedA, CandidatePlayer seedB) in pairs)
        {
            List<DraftOutcome> outcomes = SearchBestDraftExhaustive(
                candidatesByStrength, perTeam, seedA, seedB, maxAssignable, eligibleGkCount,
                pairSynergyFn, optionsCount, out int pairScenarios);

            globalScenarios += pairScenarios;

            int addedThisPair = 0;
            foreach (DraftOutcome outcome in outcomes)
            {
                string key = BuildTeamsKey(
                    outcome.TeamA.Select(x => x.Player.Id),
                    outcome.TeamB.Select(x => x.Player.Id));

                if (evaluatedKeys.Add(key))
                {
                    uniqueOutcomes.Add(outcome);
                    addedThisPair++;
                }
                else
                {
                    globalDuplicates++;
                }
            }

            _logger.LogInformation(
                "[TeamGen] Pair {A}/{B} | scenarios={S} | candidates={C} | added={Added}",
                seedA.Player.Name, seedB.Player.Name, pairScenarios, outcomes.Count, addedThisPair);
        }

        _logger.LogInformation(
            "[TeamGen] Search complete | pairs={Pairs} | totalScenarios={Total} | unique={Unique} | duplicates={Dup}",
            pairs.Count, globalScenarios, uniqueOutcomes.Count, globalDuplicates);

        // Safety net: if every pair converged to the same composition, add an unseeded result.
        if (uniqueOutcomes.Count == 0)
        {
            List<DraftOutcome> unseeded = SearchBestDraftExhaustive(
                candidatesByStrength, perTeam, null, null, maxAssignable, eligibleGkCount,
                pairSynergyFn, optionsCount, out int unseededScenarios);

            _logger.LogInformation("[TeamGen] Safety net (unseeded) | scenarios={S}", unseededScenarios);

            foreach (DraftOutcome o in unseeded)
            {
                string key = BuildTeamsKey(o.TeamA.Select(x => x.Player.Id), o.TeamB.Select(x => x.Player.Id));
                if (evaluatedKeys.Add(key))
                    uniqueOutcomes.Add(o);
            }
        }

        return uniqueOutcomes;
    }

    // ── DTO builder ───────────────────────────────────────────────────────────

    private TeamOptionDto BuildTeamOption(
        DraftOutcome draft,
        List<PlayerRequestDto> allPlayers,
        TeamGenerationSettings settings,
        bool considerSynergy)
    {
        List<PlayerWeightDto> teamA = draft.TeamA.Select(x => new PlayerWeightDto(x.Player.Id, EffectiveWeight(x.Stats))).ToList();
        List<PlayerWeightDto> teamB = draft.TeamB.Select(x => new PlayerWeightDto(x.Player.Id, EffectiveWeight(x.Stats))).ToList();

        HashSet<Guid> unassignedPlayerIds = draft.Waiting.Select(x => x.Player.Id).ToHashSet();
        if (!settings.IncludeGoalkeepers)
            foreach (PlayerRequestDto gk in allPlayers.Where(p => p.IsGoalkeeper))
                unassignedPlayerIds.Add(gk.Id);

        Dictionary<Guid, PlayerStats> statsByPlayerId = draft.TeamA.Concat(draft.TeamB).Concat(draft.Waiting)
            .ToDictionary(x => x.Player.Id, x => x.Stats);

        List<PlayerWeightDto> unassigned = unassignedPlayerIds
            .Select(id => statsByPlayerId.TryGetValue(id, out PlayerStats? s)
                ? new PlayerWeightDto(id, EffectiveWeight(s))
                : new PlayerWeightDto(id, NeutralWinRate))
            .DistinctBy(x => x.PlayerId)
            .ToList();

        return new TeamOptionDto(
            TeamA: teamA,
            TeamB: teamB,
            Unassigned: unassigned,
            TeamAWeight: teamA.Sum(x => x.Weight),
            TeamBWeight: teamB.Sum(x => x.Weight),
            BalanceDiff: draft.BalanceDiff,
            SynergyTotal: draft.SynergyTotal,
            Score: draft.Score
        )
        {
            Explanation = BuildExplanation(draft, considerSynergy)
        };
    }

    // ── Seed-pair construction ────────────────────────────────────────────────

    /// <summary>Returns all distinct unordered pairs from <paramref name="seedPool"/>.</summary>
    private static List<(CandidatePlayer SeedA, CandidatePlayer SeedB)> BuildAllPairs(
        List<CandidatePlayer> seedPool)
    {
        var allPairs = new List<(CandidatePlayer SeedA, CandidatePlayer SeedB)>();

        for (int i = 0; i < seedPool.Count; i++)
            for (int j = i + 1; j < seedPool.Count; j++)
                allPairs.Add((seedPool[i], seedPool[j]));

        return allPairs;
    }

    /// <summary>
    /// Returns all pairs when the budget allows; otherwise selects a coverage-focused subset:
    /// extremes first (strongest vs weakest), then strength-adjacent pairs, then any remainder
    /// needed to reach the budget.
    /// </summary>
    private static List<(CandidatePlayer SeedA, CandidatePlayer SeedB)> SelectPairsToSearch(
        List<(CandidatePlayer SeedA, CandidatePlayer SeedB)> allPairs,
        List<CandidatePlayer> sortedByStrengthDesc)
    {
        int budget = Math.Max(1, MaxSeedPairsToEvaluate);

        if (budget >= allPairs.Count || budget == int.MaxValue)
            return allPairs;

        var selected     = new List<(CandidatePlayer SeedA, CandidatePlayer SeedB)>();
        var selectedKeys = new HashSet<(Guid, Guid)>();

        void AddPairIfNew(CandidatePlayer x, CandidatePlayer y)
        {
            if (x.Player.Id == y.Player.Id) return;

            (Guid, Guid) key = x.Player.Id.CompareTo(y.Player.Id) < 0
                ? (x.Player.Id, y.Player.Id)
                : (y.Player.Id, x.Player.Id);

            if (selectedKeys.Add(key))
                selected.Add((x, y));
        }

        int n = sortedByStrengthDesc.Count;

        // Extreme pairs: strongest player vs weakest player
        for (int i = 0; i < n / 2 && selected.Count < budget; i++)
            AddPairIfNew(sortedByStrengthDesc[i], sortedByStrengthDesc[n - 1 - i]);

        // Adjacent pairs: neighbours in strength order
        for (int i = 0; i < n - 1 && selected.Count < budget; i++)
            AddPairIfNew(sortedByStrengthDesc[i], sortedByStrengthDesc[i + 1]);

        // Fill remaining budget from the full pair list
        foreach ((CandidatePlayer pSeedA, CandidatePlayer pSeedB) in allPairs)
        {
            if (selected.Count >= budget) break;

            (Guid, Guid) key = pSeedA.Player.Id.CompareTo(pSeedB.Player.Id) < 0
                ? (pSeedA.Player.Id, pSeedB.Player.Id)
                : (pSeedB.Player.Id, pSeedA.Player.Id);

            if (selectedKeys.Add(key))
                selected.Add((pSeedA, pSeedB));
        }

        return selected;
    }

    // ── Exhaustive search ─────────────────────────────────────────────────────

    /// <summary>
    /// Exhaustive draft: explores every valid branch of the assignment tree without pruning,
    /// and returns the top <paramref name="topK"/> distinct outcomes ordered by ascending
    /// <see cref="DraftState.FinalScore"/>, with descending SynergyTotal as a tiebreaker.
    ///
    /// <para>Every valid terminal composition is scored and collected. Returning the top-K
    /// (instead of a single best) allows <see cref="EvaluateSeedPairs"/> to build a richer
    /// diversity pool across all seed pairs, ensuring that <c>optionsCount</c> truly different
    /// team compositions can be surfaced even when most seed pairs converge to the same global
    /// optimum.</para>
    /// </summary>
    /// <param name="scenariosEvaluated">Number of terminal states reached during exploration.</param>
    private List<DraftOutcome> SearchBestDraftExhaustive(
        List<CandidatePlayer> candidatesByStrength,
        int perTeam,
        CandidatePlayer? seedA,
        CandidatePlayer? seedB,
        int maxAssignable,
        int eligibleGkCount,
        Func<PlayerStats, Guid, PlayerStats, double> pairSynergyFn,
        int topK,
        out int scenariosEvaluated)
    {
        DraftState     initialState = CreateInitialState(candidatesByStrength, perTeam, seedA, seedB, maxAssignable, eligibleGkCount, pairSynergyFn);
        var            terminals    = new List<DraftState>();
        int            count        = 0;

        void Explore(DraftState state)
        {
            if (state.IsTerminal)
            {
                count++;
                terminals.Add(state);
                return;
            }

            foreach (DraftState child in state.Expand(pairSynergyFn))
                Explore(child);
        }

        Explore(initialState);

        scenariosEvaluated = count;

        return terminals
            .OrderBy(s => s.FinalScore)
            .ThenByDescending(s => s.SynergySumA + s.SynergySumB)
            .Take(topK)
            .Select(s => s.ToDraftOutcome())
            .ToList();
    }

    /// <summary>
    /// Builds the initial <see cref="DraftState"/> by placing the seed players (when provided)
    /// and computing all cached aggregates from scratch.
    /// </summary>
    private static DraftState CreateInitialState(
        List<CandidatePlayer> candidatesByStrength,
        int perTeam,
        CandidatePlayer? seedA,
        CandidatePlayer? seedB,
        int maxAssignable,
        int eligibleGkCount,
        Func<PlayerStats, Guid, PlayerStats, double> pairSynergyFn)
    {
        var waitingPlayers = candidatesByStrength.ToList();
        var teamA          = new List<CandidatePlayer>(perTeam);
        var teamB          = new List<CandidatePlayer>(perTeam);

        TryPlaceSeedPlayer(seedA, teamA, waitingPlayers);
        TryPlaceSeedPlayer(seedB, teamB, waitingPlayers);

        // Ensure each team starts with at least one player.
        if      (teamA.Count == 0 && teamB.Count > 0 && waitingPlayers.Count > 0)
            { teamA.Add(waitingPlayers[0]); waitingPlayers.RemoveAt(0); }
        else if (teamB.Count == 0 && teamA.Count > 0 && waitingPlayers.Count > 0)
            { teamB.Add(waitingPlayers[0]); waitingPlayers.RemoveAt(0); }
        else if (teamA.Count == 0 && teamB.Count == 0 && waitingPlayers.Count > 0)
        {
            teamA.Add(waitingPlayers[0]); waitingPlayers.RemoveAt(0);
            if (waitingPlayers.Count > 0) { teamB.Add(waitingPlayers[0]); waitingPlayers.RemoveAt(0); }
        }

        double weightSumA       = teamA.Sum(p => EffectiveWeight(p.Stats));
        double weightSumB       = teamB.Sum(p => EffectiveWeight(p.Stats));
        int    goalkeeperCountA = teamA.Count(p => p.Player.IsGoalkeeper);
        int    goalkeeperCountB = teamB.Count(p => p.Player.IsGoalkeeper);
        double synergySumA      = SumTeamPairSynergies(teamA, pairSynergyFn);
        double synergySumB      = SumTeamPairSynergies(teamB, pairSynergyFn);

        return new DraftState(
            teamA, teamB, waitingPlayers, perTeam, maxAssignable, eligibleGkCount,
            weightSumA, weightSumB,
            goalkeeperCountA, goalkeeperCountB,
            synergySumA, synergySumB);
    }

    /// <summary>
    /// Returns the sum of all distinct pair synergies within a team.
    /// Called only for seeded players during initial state construction.
    /// </summary>
    private static double SumTeamPairSynergies(
        List<CandidatePlayer> team,
        Func<PlayerStats, Guid, PlayerStats, double> pairSynergyFn)
    {
        double sum = 0.0;
        for (int i = 0; i < team.Count; i++)
            for (int j = i + 1; j < team.Count; j++)
                sum += pairSynergyFn(team[i].Stats, team[j].Player.Id, team[j].Stats);
        return sum;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void TryPlaceSeedPlayer(
        CandidatePlayer? seed,
        List<CandidatePlayer> team,
        List<CandidatePlayer> waitingPlayers)
    {
        if (seed is null) return;

        CandidatePlayer? found = waitingPlayers.FirstOrDefault(x => x.Player.Id == seed.Player.Id);
        if (found is not null) { team.Add(found); waitingPlayers.Remove(found); }
    }

    /// <summary>
    /// Computes pairwise synergy between two players.
    /// Returns <see cref="NeutralSynergy"/> when synergy is disabled, either player is neutral,
    /// or no shared history exists.
    /// Pure static function — no instance state, fully thread-safe.
    /// </summary>
    private static double ComputePairSynergy(
        PlayerStats playerA, Guid playerBId, PlayerStats playerB, bool considerSynergy)
    {
        if (!considerSynergy)                         return NeutralSynergy;
        if (IsNeutral(playerA) || IsNeutral(playerB)) return NeutralSynergy;

        if (playerA.SynergyWith?.TryGetValue(playerBId,          out double synergyA) == true) return Math.Clamp(synergyA, -0.5, 0.5);
        if (playerB.SynergyWith?.TryGetValue(playerA.PlayerId,   out double synergyB) == true) return Math.Clamp(synergyB, -0.5, 0.5);

        return NeutralSynergy;
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private void LogSearchSetup(List<CandidatePlayer> seedPool, TeamGenerationSettings settings, int maxAssignable)
    {
        if (_logger == NullLogger<AlgorithmStrategy>.Instance) return;

        _logger.LogInformation(
            "[TeamGen] SeedPool({Size}) | PerTeam={PerTeam} IncludeGK={IncGK} MaxAssignable={Max} | {Pool}",
            seedPool.Count, settings.PlayersPerTeam, settings.IncludeGoalkeepers, maxAssignable,
            string.Join(", ", seedPool.Select(FormatPlayer)));
    }

    private void LogBestResult(DraftOutcome best)
    {
        if (_logger == NullLogger<AlgorithmStrategy>.Instance) return;

        _logger.LogInformation(
            "[TeamGen] BEST | Score={Score:0.000} Balance={Bal:0.000} Synergy={Syn:0.000}",
            best.Score, best.BalanceDiff, best.SynergyTotal);

        _logger.LogInformation("[TeamGen] TeamA: {A}", FormatTeam(best.TeamA));
        _logger.LogInformation("[TeamGen] TeamB: {B}", FormatTeam(best.TeamB));
    }

    private static string FormatPlayer(CandidatePlayer p)
    {
        double weight  = EffectiveWeight(p.Stats);
        int    matches = TotalMatches(p.Stats);
        string neutral = IsNeutral(p.Stats) ? ",N" : "";
        string gk      = p.Player.IsGoalkeeper ? ",GK" : "";
        return $"{p.Player.Name}(w={weight:0.00},m={matches}{neutral}{gk})";
    }

    private static string FormatTeam(List<CandidatePlayer> team)
        => string.Join(", ", team.Select(FormatPlayer));

    // ── Explanation builder ───────────────────────────────────────────────────

    private static TeamOptionExplanationDto BuildExplanation(DraftOutcome draft, bool considerSynergy)
        => new()
        {
            Resumo       = BuildResumo(draft, considerSynergy),
            AnaliseTimeA = BuildTeamAnalysis("Time A", draft.TeamA, considerSynergy),
            AnaliseTimeB = BuildTeamAnalysis("Time B", draft.TeamB, considerSynergy),
            Conclusao    = BuildConclusao(draft, considerSynergy)
        };

    private static string BuildResumo(DraftOutcome draft, bool considerSynergy)
    {
        var sb = new StringBuilder();

        // Lead with balance
        string balanceDesc = draft.BalanceDiff < 0.05
            ? $"diferença de peso mínima de {draft.BalanceDiff:0.000} entre os times"
            : $"diferença de peso de {draft.BalanceDiff:0.000} entre os times";

        sb.Append($"Essa opção foi escolhida por apresentar {balanceDesc}");

        // Goalkeeper distribution (structural constraint guarantees 1 per team when ≥2 eligible)
        int gkA = draft.TeamA.Count(p => p.Player.IsGoalkeeper);
        int gkB = draft.TeamB.Count(p => p.Player.IsGoalkeeper);
        if (gkA + gkB > 0)
        {
            sb.Append(gkA == gkB
                ? ", com goleiros bem distribuídos"
                : ", com goleiro em apenas um dos times");
        }

        // Synergy — only when active and meaningful
        if (considerSynergy && draft.SynergyTotal > 0.05)
        {
            string synDesc = draft.SynergyTotal > 0.20
                ? $"e sinergia total elevada (+{draft.SynergyTotal:0.00})"
                : $"e boa sinergia total (+{draft.SynergyTotal:0.00})";
            sb.Append($", {synDesc}");
        }

        sb.Append('.');
        return sb.ToString();
    }

    private static string BuildTeamAnalysis(string teamName, List<CandidatePlayer> team, bool considerSynergy)
    {
        if (team.Count == 0) return $"{teamName} sem jogadores atribuídos.";

        var sb = new StringBuilder();

        // Average strength + strongest player
        double avgWeight = team.Average(p => EffectiveWeight(p.Stats));
        CandidatePlayer strongest = team.MaxBy(p => EffectiveWeight(p.Stats))!;
        sb.Append($"{teamName} tem força média de {avgWeight:0.00} e conta com {strongest.Player.Name} " +
                  $"como principal referência técnica, com peso {EffectiveWeight(strongest.Stats):0.00}.");

        // Top offensive contributor (non-neutral, with match history)
        var offensiveList = team
            .Where(p => !IsNeutral(p.Stats) && TotalMatches(p.Stats) > 0)
            .Select(p => (p.Player.Name, Metric: GoalContributionPerGame(p.Stats)))
            .Where(x => x.Metric > 0)
            .OrderByDescending(x => x.Metric)
            .ToList();

        if (offensiveList.Count > 0)
            sb.Append($" {offensiveList[0].Name} se destaca ofensivamente com média de " +
                      $"{offensiveList[0].Metric:0.00} participações em gol por jogo.");

        // Best synergy pair
        (string NameA, string NameB, double Syn)? bestPair = FindBestSynergyPair(team, considerSynergy);
        if (bestPair.HasValue)
            sb.Append($" A dupla {bestPair.Value.NameA} e {bestPair.Value.NameB} " +
                      $"representa o melhor entrosamento histórico neste time (+{bestPair.Value.Syn:0.00}).");

        return sb.ToString();
    }

    private static string BuildConclusao(DraftOutcome draft, bool considerSynergy)
    {
        bool goodSynergy = considerSynergy && draft.SynergyTotal > 0.1;

        string conclusion;
        if (draft.BalanceDiff < 0.05 && goodSynergy)
            conclusion = "Essa formação combina ótimo equilíbrio entre os lados com bom aproveitamento da sinergia entre jogadores.";
        else if (draft.BalanceDiff < 0.05)
            conclusion = "Essa opção prioriza principalmente o equilíbrio de força entre os dois times.";
        else if (draft.BalanceDiff < 0.15 && goodSynergy)
            conclusion = "Essa opção abre um pequeno espaço no balanceamento para aproveitar melhor combinações históricas entre jogadores.";
        else
            conclusion = "Essa formação ficou entre as melhores por manter um bom compromisso entre força total e distribuição geral do elenco.";

        return $"{conclusion} Diferença de peso: {draft.BalanceDiff:0.000}.";
    }

    // ── Explanation helpers ────────────────────────────────────────────────────

    /// <summary>
    /// (Goals + 0.6 × Assists) / TotalMatches.
    /// Returns 0 when TotalMatches is 0.
    /// </summary>
    private static double GoalContributionPerGame(PlayerStats stats)
    {
        int matches = TotalMatches(stats);
        return matches == 0 ? 0 : (stats.Goals + 0.6 * stats.Assists) / matches;
    }

    /// <summary>
    /// Returns the pair in <paramref name="team"/> with the highest pairwise synergy,
    /// or null when synergy is disabled, the team has fewer than two players,
    /// or the best pair synergy is ≤ 0.05.
    /// </summary>
    private static (string NameA, string NameB, double Syn)? FindBestSynergyPair(
        List<CandidatePlayer> team, bool considerSynergy)
    {
        if (!considerSynergy || team.Count < 2) return null;

        double bestSyn   = double.MinValue;
        string bestNameA = "";
        string bestNameB = "";

        for (int i = 0; i < team.Count; i++)
        for (int j = i + 1; j < team.Count; j++)
        {
            double s = ComputePairSynergy(team[i].Stats, team[j].Player.Id, team[j].Stats, considerSynergy);
            if (s > bestSyn)
            {
                bestSyn   = s;
                bestNameA = team[i].Player.Name;
                bestNameB = team[j].Player.Name;
            }
        }

        return bestSyn > 0.05 ? (bestNameA, bestNameB, bestSyn) : null;
    }

    // ── Internal types ────────────────────────────────────────────────────────

    /// <summary>
    /// An immutable snapshot of a partial team assignment, used as a node in the exhaustive search.
    ///
    /// <para><b>FinalScore</b> — official score for terminal states: BalanceDiff only.
    /// Synergy tiebreaking happens externally via <c>ThenByDescending(SynergyTotal)</c>.
    /// Goalkeeper distribution is enforced as a hard constraint in <see cref="Expand"/>, not here.</para>
    /// </summary>
    private sealed class DraftState
    {
        // ── Team composition ──────────────────────────────────────────────────

        public List<CandidatePlayer> TeamA   { get; }
        public List<CandidatePlayer> TeamB   { get; }

        /// <summary>Players not yet assigned; <c>Waiting[0]</c> is placed next.</summary>
        public List<CandidatePlayer> Waiting { get; }

        public int PerTeam        { get; }
        public int MaxAssignable  { get; }

        /// <summary>
        /// Total number of eligible goalkeepers in the search.
        /// When ≥ 2, <see cref="Expand"/> enforces the "one GK per team" constraint.
        /// </summary>
        public int EligibleGkCount { get; }

        // ── Incrementally maintained aggregates ───────────────────────────────

        /// <summary>Σ EffectiveWeight for every player in TeamA.</summary>
        public double WeightSumA    { get; }

        /// <summary>Σ EffectiveWeight for every player in TeamB.</summary>
        public double WeightSumB    { get; }

        public int GoalkeeperCountA { get; }
        public int GoalkeeperCountB { get; }

        /// <summary>Σ PairSynergy over all distinct pairs within TeamA.</summary>
        public double SynergySumA   { get; }

        /// <summary>Σ PairSynergy over all distinct pairs within TeamB.</summary>
        public double SynergySumB   { get; }

        // ── Constructor ───────────────────────────────────────────────────────

        public DraftState(
            List<CandidatePlayer> teamA,
            List<CandidatePlayer> teamB,
            List<CandidatePlayer> waiting,
            int perTeam, int maxAssignable, int eligibleGkCount,
            double weightSumA, double weightSumB,
            int goalkeeperCountA, int goalkeeperCountB,
            double synergySumA, double synergySumB)
        {
            TeamA = teamA; TeamB = teamB; Waiting = waiting;
            PerTeam = perTeam; MaxAssignable = maxAssignable; EligibleGkCount = eligibleGkCount;
            WeightSumA = weightSumA; WeightSumB = weightSumB;
            GoalkeeperCountA = goalkeeperCountA; GoalkeeperCountB = goalkeeperCountB;
            SynergySumA = synergySumA; SynergySumB = synergySumB;
        }

        // ── Termination ───────────────────────────────────────────────────────

        public bool IsTerminal =>
            Waiting.Count == 0 || TeamA.Count + TeamB.Count >= MaxAssignable;

        // ── Scoring ───────────────────────────────────────────────────────────

        /// <summary>
        /// Official score for terminal states: BalanceDiff only.
        /// Synergy tiebreaking happens externally. Lower is better.
        /// </summary>
        public double FinalScore
            => BalanceWeight * Math.Abs(WeightSumA - WeightSumB);

        // ── Expansion ─────────────────────────────────────────────────────────

        /// <summary>
        /// Yields up to two child states by placing <c>Waiting[0]</c> on TeamA (child A)
        /// or TeamB (child B). A child is omitted when:
        /// <list type="bullet">
        ///   <item>the target team is full, or</item>
        ///   <item>the total assigned count would exceed <see cref="MaxAssignable"/>, or</item>
        ///   <item>the goalkeeper constraint applies: when <see cref="EligibleGkCount"/> ≥ 2,
        ///         a GK may only go to a team that does not yet have one.</item>
        /// </list>
        /// </summary>
        public IEnumerable<DraftState> Expand(
            Func<PlayerStats, Guid, PlayerStats, double> pairSynergyFn)
        {
            if (IsTerminal) yield break;

            CandidatePlayer       candidate  = Waiting[0];
            List<CandidatePlayer> remaining  = Waiting.Count > 1
                ? Waiting.GetRange(1, Waiting.Count - 1)
                : new List<CandidatePlayer>(0);

            double weight       = WeightOf(candidate);
            bool   isGoalkeeper = candidate.Player.IsGoalkeeper;

            // When ≥2 eligible GKs exist, each team may receive at most one GK.
            bool gkConstraintActive = isGoalkeeper && EligibleGkCount >= 2;

            // Child A: place candidate on TeamA
            bool teamAOpen = TeamA.Count < PerTeam
                          && TeamA.Count + TeamB.Count < MaxAssignable
                          && (!gkConstraintActive || GoalkeeperCountA == 0);

            if (teamAOpen)
            {
                double synergyGain = TeamA.Sum(m => pairSynergyFn(candidate.Stats, m.Player.Id, m.Stats));

                yield return new DraftState(
                    new List<CandidatePlayer>(TeamA) { candidate },
                    new List<CandidatePlayer>(TeamB),
                    remaining,
                    PerTeam, MaxAssignable, EligibleGkCount,
                    WeightSumA + weight,                        WeightSumB,
                    GoalkeeperCountA + (isGoalkeeper ? 1 : 0), GoalkeeperCountB,
                    SynergySumA + synergyGain,                  SynergySumB);
            }

            // Child B: place candidate on TeamB
            bool teamBOpen = TeamB.Count < PerTeam
                          && TeamA.Count + TeamB.Count < MaxAssignable
                          && (!gkConstraintActive || GoalkeeperCountB == 0);

            if (teamBOpen)
            {
                double synergyGain = TeamB.Sum(m => pairSynergyFn(candidate.Stats, m.Player.Id, m.Stats));

                yield return new DraftState(
                    new List<CandidatePlayer>(TeamA),
                    new List<CandidatePlayer>(TeamB) { candidate },
                    remaining,
                    PerTeam, MaxAssignable, EligibleGkCount,
                    WeightSumA,                                 WeightSumB + weight,
                    GoalkeeperCountA,                           GoalkeeperCountB + (isGoalkeeper ? 1 : 0),
                    SynergySumA,                                SynergySumB + synergyGain);
            }
        }

        // ── Conversion ────────────────────────────────────────────────────────

        /// <summary>Converts a terminal state to the <see cref="DraftOutcome"/> returned by the search loop.</summary>
        public DraftOutcome ToDraftOutcome()
        {
            double balanceDiff  = Math.Abs(WeightSumA - WeightSumB);
            double synergyTotal = SynergySumA + SynergySumB;
            return new DraftOutcome(TeamA, TeamB, Waiting,
                FinalScore, balanceDiff, synergyTotal);
        }

        // ── Local helper ──────────────────────────────────────────────────────

        private static double WeightOf(CandidatePlayer p) => EffectiveWeight(p.Stats);
    }

    private sealed class DraftOutcome(
        List<CandidatePlayer> teamA,
        List<CandidatePlayer> teamB,
        List<CandidatePlayer> waiting,
        double score,
        double balanceDiff,
        double synergyTotal)
    {
        public List<CandidatePlayer> TeamA        { get; } = teamA;
        public List<CandidatePlayer> TeamB        { get; } = teamB;
        public List<CandidatePlayer> Waiting      { get; } = waiting;
        public double                Score        { get; } = score;
        public double                BalanceDiff  { get; } = balanceDiff;
        public double                SynergyTotal { get; } = synergyTotal;
    }
}
