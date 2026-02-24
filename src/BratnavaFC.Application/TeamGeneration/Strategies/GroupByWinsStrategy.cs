using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.TeamGeneration;

public sealed class GroupByWinsStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    public GroupByWinsStrategy(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

    // --- NEUTRAL RULE (igual sua ideia do AlgorithmStrategy) ---
    private const int MinMatchesToBeNonNeutral = 3;
    private const double NeutralWinRate = 0.50;

    private static int TotalMatches(PlayerStats s) => (s?.Wins ?? 0) + (s?.Ties ?? 0) + (s?.Losses ?? 0);
    private static bool IsNeutral(PlayerStats s) => TotalMatches(s) < MinMatchesToBeNonNeutral;
    private static double EffectiveWinRate(PlayerStats s) => IsNeutral(s) ? NeutralWinRate : s.WinRate;

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

        // candidates: optionally exclude goalkeepers
        var candidatePlayers = settings.IncludeGoalkeepers
            ? players.ToList()
            : players.Where(p => !p.IsGoalkeeper).ToList();

        var perTeam = settings.PlayersPerTeam;
        var maxAssignable = Math.Min(candidatePlayers.Count, perTeam * 2);

        if (maxAssignable == 0)
        {
            var allUnassigned = players.Select(p => new PlayerWeightDto(p.Id, 0.0)).ToList();

            var opt = new TeamOptionDto(
                TeamA: new(),
                TeamB: new(),
                Unassigned: allUnassigned,
                TeamAWeight: 0,
                TeamBWeight: 0,
                BalanceDiff: 0,
                GoalkeeperDiff: 0,
                SynergyTotal: 0,
                Score: 0
            );

            return new TeamsOptionsResultDto(new List<TeamOptionDto> { opt });
        }

        var statsByPlayerId = await LoadStatsByPlayerId(candidatePlayers, cancellationToken);

        // Ordena por Wins desc (porque é a strategy), desempata por EffectiveWinRate e Id
        var ordered = candidatePlayers
            .Select(p => new PlayerWithStats(p, GetOrCreateStats(statsByPlayerId, p.Id, p.Name)))
            .OrderByDescending(x => x.Stats.Wins)
            .ThenByDescending(x => EffectiveWinRate(x.Stats))
            .ThenBy(x => x.Player.Id)
            .ToList();

        var outcomes = new List<Outcome>();

        // Opção 1: alternando a partir do TeamA
        outcomes.Add(BuildOutcome(ordered, players, settings, perTeam, maxAssignable, AssignMode.AlternateStartA));

        if (optionsCount > 1)
            // Opção 2: alternando a partir do TeamB
            outcomes.Add(BuildOutcome(ordered, players, settings, perTeam, maxAssignable, AssignMode.AlternateStartB));

        if (optionsCount > 2)
            // Opção 3: snake ABBA
            outcomes.Add(BuildOutcome(ordered, players, settings, perTeam, maxAssignable, AssignMode.SnakeABBA));

        // dedupe (normaliza times invertidos)
        var seen = new HashSet<string>();
        var options = new List<TeamOptionDto>();

        foreach (var o in outcomes)
        {
            var key = BuildTeamsKey(o.TeamA, o.TeamB);
            if (!seen.Add(key)) continue;

            options.Add(BuildOptionDto(o, settings));
            if (options.Count >= optionsCount) break;
        }

        // menor score = melhor
        options = options.OrderBy(x => x.Score).ToList();

        return new TeamsOptionsResultDto(options);
    }

    private Outcome BuildOutcome(
        List<PlayerWithStats> ordered,
        List<PlayerRequestDto> allPlayers,
        TeamGenerationSettings settings,
        int perTeam,
        int maxAssignable,
        AssignMode mode)
    {
        var teamA = new List<Guid>(perTeam);
        var teamB = new List<Guid>(perTeam);

        AssignPlayersByMode(ordered, teamA, teamB, perTeam, maxAssignable, mode);

        var assigned = new HashSet<Guid>(teamA.Concat(teamB));

        var unassigned = allPlayers
            .Where(p => !assigned.Contains(p.Id))
            .Select(p => p.Id)
            .ToList();

        if (!settings.IncludeGoalkeepers)
        {
            foreach (var gk in allPlayers.Where(p => p.IsGoalkeeper).Select(p => p.Id))
                if (!unassigned.Contains(gk))
                    unassigned.Add(gk);
        }

        var map = ordered.ToDictionary(x => x.Player.Id, x => x);

        return new Outcome(teamA, teamB, unassigned, map);
    }

    private void AssignPlayersByMode(
        List<PlayerWithStats> ordered,
        List<Guid> teamA,
        List<Guid> teamB,
        int perTeam,
        int maxAssignable,
        AssignMode mode)
    {
        if (ordered.Count == 0) return;

        int assigned = 0;

        for (int i = 0; i < ordered.Count && assigned < maxAssignable; i++)
        {
            var id = ordered[i].Player.Id;

            bool pickA = mode switch
            {
                AssignMode.AlternateStartA => (i % 2 == 0),        // A,B,A,B...
                AssignMode.AlternateStartB => (i % 2 != 0),        // B,A,B,A... (logo pickA é o inverso)
                AssignMode.SnakeABBA => SnakePickA(i),        // ABBA ABBA...
                _ => (i % 2 == 0)
            };

            if (pickA)
            {
                if (teamA.Count < perTeam) { teamA.Add(id); assigned++; }
                else if (teamB.Count < perTeam) { teamB.Add(id); assigned++; }
            }
            else
            {
                if (teamB.Count < perTeam) { teamB.Add(id); assigned++; }
                else if (teamA.Count < perTeam) { teamA.Add(id); assigned++; }
            }

            // se os dois lotaram, para
            if (teamA.Count >= perTeam && teamB.Count >= perTeam) break;
        }
    }

    // padrão ABBA: índices 0..3 => A,B,B,A (repete)
    private static bool SnakePickA(int index)
    {
        var m = index % 4;
        return m == 0 || m == 3;
    }

    private TeamOptionDto BuildOptionDto(Outcome o, TeamGenerationSettings settings)
    {
        double WeightOf(Guid id)
            => o.Map.TryGetValue(id, out var p) ? EffectiveWinRate(p.Stats) : 0.0;

        bool IsGk(Guid id)
            => o.Map.TryGetValue(id, out var p) && p.Player.IsGoalkeeper;

        var teamA = o.TeamA.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();
        var teamB = o.TeamB.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();
        var unassigned = o.Unassigned.Distinct().Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();

        var teamAWeight = teamA.Sum(x => x.Weight);
        var teamBWeight = teamB.Sum(x => x.Weight);

        var balanceDiff = Math.Abs(teamAWeight - teamBWeight);

        var gkDiff = settings.IncludeGoalkeepers
            ? Math.Abs(o.TeamA.Count(IsGk) - o.TeamB.Count(IsGk))
            : 0;

        // sem sinergia nessa strategy
        var synergyTotal = 0.0;

        // score simples: balanceDiff
        var score = balanceDiff;

        return new TeamOptionDto(
            TeamA: teamA,
            TeamB: teamB,
            Unassigned: unassigned,
            TeamAWeight: teamAWeight,
            TeamBWeight: teamBWeight,
            BalanceDiff: balanceDiff,
            GoalkeeperDiff: gkDiff,
            SynergyTotal: synergyTotal,
            Score: score
        );
    }

    private static string BuildTeamsKey(List<Guid> teamA, List<Guid> teamB)
    {
        var a = teamA.OrderBy(x => x).ToArray();
        var b = teamB.OrderBy(x => x).ToArray();

        var key1 = "A:" + string.Join(",", a) + "|B:" + string.Join(",", b);
        var key2 = "A:" + string.Join(",", b) + "|B:" + string.Join(",", a);

        return string.CompareOrdinal(key1, key2) <= 0 ? key1 : key2;
    }

    private async Task<Dictionary<Guid, PlayerStats>> LoadStatsByPlayerId(
        List<PlayerRequestDto> players,
        CancellationToken cancellationToken)
    {
        var statsList = await _statsService.EnrichPlayersAsync(players, cancellationToken);
        return statsList.ToDictionary(s => s.PlayerId, s => s);
    }

    private static PlayerStats GetOrCreateStats(
        Dictionary<Guid, PlayerStats> statsByPlayerId,
        Guid playerId,
        string? name)
        => statsByPlayerId.TryGetValue(playerId, out var s)
            ? s
            : new PlayerStats
            {
                PlayerId = playerId,
                Name = name ?? string.Empty,
                Wins = 0,
                Ties = 0,
                Losses = 0,
                WinRate = 0.0,
                SynergyWith = new Dictionary<Guid, double>()
            };

    private enum AssignMode
    {
        AlternateStartA = 0,
        AlternateStartB = 1,
        SnakeABBA = 2
    }

    private sealed class PlayerWithStats
    {
        public PlayerRequestDto Player { get; }
        public PlayerStats Stats { get; }

        public PlayerWithStats(PlayerRequestDto player, PlayerStats stats)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }
    }

    private sealed class Outcome
    {
        public List<Guid> TeamA { get; }
        public List<Guid> TeamB { get; }
        public List<Guid> Unassigned { get; }
        public Dictionary<Guid, PlayerWithStats> Map { get; }

        public Outcome(List<Guid> a, List<Guid> b, List<Guid> un, Dictionary<Guid, PlayerWithStats> map)
        {
            TeamA = a;
            TeamB = b;
            Unassigned = un;
            Map = map;
        }
    }
}