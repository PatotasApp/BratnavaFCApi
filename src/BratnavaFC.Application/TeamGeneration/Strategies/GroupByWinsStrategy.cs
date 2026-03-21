using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

public sealed class GroupByWinsStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    public GroupByWinsStrategy(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

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

        List<PlayerRequestDto>        candidates    = FilterCandidates(players, settings);
        int                           perTeam       = settings.PlayersPerTeam;
        int                           maxAssignable = Math.Min(candidates.Count, perTeam * 2);

        if (maxAssignable == 0)
            return BuildEmptyResult(players);

        var statsResult = await LoadStatsByPlayerId(_statsService, candidates, cancellationToken);
        if (!statsResult.Success)
            return BuildEmptyResult(players);
        Dictionary<Guid, PlayerStats> statsById = statsResult.Data!;

        // Sort by Wins desc → EffectiveWeight desc → Id asc (deterministic tie-break)
        List<CandidatePlayer> ordered = candidates
            .Select(p => new CandidatePlayer(p, GetOrCreateStats(statsById, p.Id, p.Name)))
            .OrderByDescending(x => x.Stats.Wins)
            .ThenByDescending(x => EffectiveWeight(x.Stats))
            .ThenBy(x => x.Player.Id)
            .ToList();

        // Build up to 3 options with different assignment patterns
        List<Outcome> outcomes = new List<Outcome>
        {
            BuildOutcome(ordered, players, perTeam, maxAssignable, AssignMode.AlternateStartA)
        };

        if (optionsCount > 1)
            outcomes.Add(BuildOutcome(ordered, players, perTeam, maxAssignable, AssignMode.AlternateStartB));

        if (optionsCount > 2)
            outcomes.Add(BuildOutcome(ordered, players, perTeam, maxAssignable, AssignMode.SnakeABBA));

        // Deduplicate (A-vs-B == B-vs-A) and convert to DTOs
        HashSet<string>     seen    = new HashSet<string>();
        List<TeamOptionDto> options = new List<TeamOptionDto>();

        foreach (Outcome o in outcomes)
        {
            if (!seen.Add(BuildTeamsKey(o.TeamA, o.TeamB))) continue;

            options.Add(ToOptionDto(o, settings));
            if (options.Count >= optionsCount) break;
        }

        return new TeamsOptionsResultDto(options.OrderBy(x => x.Score).ToList());
    }

    // ── Outcome building ──────────────────────────────────────────────────────

    private static Outcome BuildOutcome(
        List<CandidatePlayer> ordered,
        List<PlayerRequestDto> allPlayers,
        int perTeam,
        int maxAssignable,
        AssignMode mode)
    {
        List<Guid> teamA = new List<Guid>(perTeam);
        List<Guid> teamB = new List<Guid>(perTeam);

        AssignByMode(ordered, teamA, teamB, perTeam, maxAssignable, mode);

        HashSet<Guid>                    assigned   = new HashSet<Guid>(teamA.Concat(teamB));
        List<Guid>                       unassigned = allPlayers.Where(p => !assigned.Contains(p.Id)).Select(p => p.Id).ToList();
        Dictionary<Guid, CandidatePlayer> map       = ordered.ToDictionary(x => x.Player.Id, x => x);

        return new Outcome(teamA, teamB, unassigned, map);
    }

    private static void AssignByMode(
        List<CandidatePlayer> ordered,
        List<Guid> teamA,
        List<Guid> teamB,
        int perTeam,
        int maxAssignable,
        AssignMode mode)
    {
        int assigned = 0;

        for (int i = 0; i < ordered.Count && assigned < maxAssignable; i++)
        {
            Guid id    = ordered[i].Player.Id;
            bool pickA = mode switch
            {
                AssignMode.AlternateStartA => i % 2 == 0,   // A, B, A, B …
                AssignMode.AlternateStartB => i % 2 != 0,   // B, A, B, A …
                AssignMode.SnakeABBA       => IsSnakeA(i),  // A, B, B, A repeating
                _                          => i % 2 == 0
            };

            if (pickA)
            {
                if      (teamA.Count < perTeam) { teamA.Add(id); assigned++; }
                else if (teamB.Count < perTeam) { teamB.Add(id); assigned++; }
            }
            else
            {
                if      (teamB.Count < perTeam) { teamB.Add(id); assigned++; }
                else if (teamA.Count < perTeam) { teamA.Add(id); assigned++; }
            }

            if (teamA.Count >= perTeam && teamB.Count >= perTeam) break;
        }
    }

    // ABBA pattern: positions 0, 3 → A;  positions 1, 2 → B
    private static bool IsSnakeA(int i) => i % 4 is 0 or 3;

    private static TeamOptionDto ToOptionDto(Outcome o, TeamGenerationSettings settings)
    {
        double WeightOf(Guid id)
            => o.Map.TryGetValue(id, out CandidatePlayer? p) ? EffectiveWeight(p.Stats) : NeutralWinRate;

        bool IsGk(Guid id)
            => o.Map.TryGetValue(id, out CandidatePlayer? p) && p.Player.IsGoalkeeper;

        List<PlayerWeightDto> teamA      = o.TeamA.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();
        List<PlayerWeightDto> teamB      = o.TeamB.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();
        List<PlayerWeightDto> unassigned = o.Unassigned.Distinct().Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();

        double teamAWeight = teamA.Sum(x => x.Weight);
        double teamBWeight = teamB.Sum(x => x.Weight);
        double balanceDiff = Math.Abs(teamAWeight - teamBWeight);

        return new TeamOptionDto(
            TeamA: teamA,
            TeamB: teamB,
            Unassigned: unassigned,
            TeamAWeight: teamAWeight,
            TeamBWeight: teamBWeight,
            BalanceDiff: balanceDiff,
            SynergyTotal: 0.0,
            Score: balanceDiff
        );
    }

    // ── Internal types ────────────────────────────────────────────────────────

    private enum AssignMode { AlternateStartA, AlternateStartB, SnakeABBA }

    private sealed class Outcome(
        List<Guid> teamA,
        List<Guid> teamB,
        List<Guid> unassigned,
        Dictionary<Guid, CandidatePlayer> map)
    {
        public List<Guid>                        TeamA      { get; } = teamA;
        public List<Guid>                        TeamB      { get; } = teamB;
        public List<Guid>                        Unassigned { get; } = unassigned;
        public Dictionary<Guid, CandidatePlayer> Map        { get; } = map;
    }
}
