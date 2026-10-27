using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.TeamGeneration.MultiTeam;

/// <summary>
/// Divide todos os jogadores informados em N times. O serviço não persiste nem conhece campeonato;
/// ele usa somente a força calculada por IPlayerStatsService.
/// </summary>
public sealed class MultiTeamGenerationService
{
    private const double DefaultStrength = 0.50;
    private const int MinimumRatedMatches = 3;
    private const int MaximumOptions = 20;
    private const double Epsilon = 1e-12;

    private readonly IPlayerStatsService _statsService;

    public MultiTeamGenerationService(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

    public async Task<Result<MultiTeamDrawResultDto>> GenerateAsync(
        List<PlayerRequestDto> players,
        int teamCount,
        MultiTeamStrategy strategy,
        IReadOnlyCollection<Guid> goalkeeperIds,
        int optionsCount = 3,
        int? seed = null,
        CancellationToken ct = default)
    {
        Result<MultiTeamDrawResultDto>? invalid = Validate(
            players, teamCount, strategy, goalkeeperIds, optionsCount);
        if (invalid is not null)
            return invalid;

        ct.ThrowIfCancellationRequested();
        Result<List<PlayerStats>> statsResult = await _statsService
            .EnrichPlayersAsync(players, ct)
            .ConfigureAwait(false);

        if (!statsResult.Success)
        {
            return Result<MultiTeamDrawResultDto>.Fail(
                statsResult.Error ?? "Não foi possível calcular a força dos jogadores.",
                statsResult.Status,
                statsResult.Errors);
        }

        Dictionary<Guid, double> strengths = ResolveStrengths(players, statsResult.Data ?? []);
        HashSet<Guid> goalkeeperSet = goalkeeperIds.ToHashSet();
        List<Guid> startingGoalkeepers = players
            .Where(player => goalkeeperSet.Contains(player.Id))
            .OrderByDescending(player => strengths[player.Id])
            .ThenBy(player => player.Id)
            .Take(teamCount)
            .Select(player => player.Id)
            .ToList();

        int[] capacities = BuildCapacities(players.Count, teamCount);
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        List<MultiTeamDrawOptionDto> options = strategy switch
        {
            MultiTeamStrategy.Random => GenerateRandomOptions(
                players, startingGoalkeepers, strengths, capacities, optionsCount, random, ct),
            MultiTeamStrategy.Balanced => GenerateBalancedOptions(
                players, startingGoalkeepers, strengths, capacities, optionsCount, random, ct),
            _ => [],
        };

        return Result<MultiTeamDrawResultDto>.Ok(
            new MultiTeamDrawResultDto(options, strengths));
    }

    private static Result<MultiTeamDrawResultDto>? Validate(
        List<PlayerRequestDto>? players,
        int teamCount,
        MultiTeamStrategy strategy,
        IReadOnlyCollection<Guid>? goalkeeperIds,
        int optionsCount)
    {
        if (players is null)
            return Result<MultiTeamDrawResultDto>.Fail("A lista de jogadores é obrigatória.");
        if (goalkeeperIds is null)
            return Result<MultiTeamDrawResultDto>.Fail("A lista de goleiros é obrigatória.");
        if (teamCount < 2)
            return Result<MultiTeamDrawResultDto>.Fail("A quantidade de times deve ser pelo menos 2.");
        if (players.Count < teamCount)
            return Result<MultiTeamDrawResultDto>.Fail("Há menos jogadores confirmados do que times.");
        if (optionsCount is < 1 or > MaximumOptions)
            return Result<MultiTeamDrawResultDto>.Fail($"A quantidade de opções deve ficar entre 1 e {MaximumOptions}.");
        if (!Enum.IsDefined(strategy))
            return Result<MultiTeamDrawResultDto>.Fail("Estratégia de sorteio inválida.");
        if (players.Select(player => player.Id).Distinct().Count() != players.Count)
            return Result<MultiTeamDrawResultDto>.Fail("A lista de jogadores contém IDs repetidos.");

        HashSet<Guid> playerIds = players.Select(player => player.Id).ToHashSet();
        if (goalkeeperIds.Any(goalkeeperId => !playerIds.Contains(goalkeeperId)))
            return Result<MultiTeamDrawResultDto>.Fail("A lista de goleiros contém jogador não confirmado.");

        return null;
    }

    private static Dictionary<Guid, double> ResolveStrengths(
        IReadOnlyCollection<PlayerRequestDto> players,
        IReadOnlyCollection<PlayerStats> stats)
    {
        Dictionary<Guid, PlayerStats> statsById = stats
            .GroupBy(item => item.PlayerId)
            .ToDictionary(group => group.Key, group => group.First());

        var known = new Dictionary<Guid, double>();
        foreach (PlayerRequestDto player in players)
        {
            if (!statsById.TryGetValue(player.Id, out PlayerStats? playerStats))
                continue;

            int matches = playerStats.Wins + playerStats.Ties + playerStats.Losses;
            if (matches >= MinimumRatedMatches)
                known[player.Id] = Math.Clamp(playerStats.WinRate, 0, 1);
            else if (playerStats.NeutralOverride.HasValue)
                known[player.Id] = Math.Clamp(playerStats.NeutralOverride.Value, 0, 1);
        }

        double groupAverage = known.Count == 0 ? DefaultStrength : known.Values.Average();
        return players.ToDictionary(
            player => player.Id,
            player => known.GetValueOrDefault(player.Id, groupAverage));
    }

    private static int[] BuildCapacities(int playerCount, int teamCount)
    {
        int minimumSize = playerCount / teamCount;
        int largerTeams = playerCount % teamCount;
        return Enumerable.Range(0, teamCount)
            .Select(index => minimumSize + (index < largerTeams ? 1 : 0))
            .ToArray();
    }

    private static List<MultiTeamDrawOptionDto> GenerateRandomOptions(
        IReadOnlyList<PlayerRequestDto> players,
        IReadOnlyList<Guid> startingGoalkeepers,
        IReadOnlyDictionary<Guid, double> strengths,
        int[] capacities,
        int optionsCount,
        Random random,
        CancellationToken ct)
    {
        var options = new List<MultiTeamDrawOptionDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int attempts = Math.Max(30, optionsCount * 20);

        for (int attempt = 0; attempt < attempts && options.Count < optionsCount; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            List<TeamState> teams = CreateEmptyTeams(capacities);
            List<Guid> keepers = startingGoalkeepers.ToList();
            Shuffle(keepers, random);
            AssignStartingGoalkeepers(teams, keepers);

            HashSet<Guid> startingSet = startingGoalkeepers.ToHashSet();
            List<Guid> field = players
                .Select(player => player.Id)
                .Where(playerId => !startingSet.Contains(playerId))
                .ToList();
            Shuffle(field, random);
            FillRoundRobin(teams, field, random.Next(teams.Count));

            string key = BuildOptionKey(teams);
            if (seen.Add(key))
                options.Add(ToOption(teams, strengths, baselineDifference: StrengthDifference(teams, strengths)));
        }

        return options.OrderBy(option => option.StrengthDifference).ToList();
    }

    private static List<MultiTeamDrawOptionDto> GenerateBalancedOptions(
        IReadOnlyList<PlayerRequestDto> players,
        IReadOnlyList<Guid> startingGoalkeepers,
        IReadOnlyDictionary<Guid, double> strengths,
        int[] capacities,
        int optionsCount,
        Random random,
        CancellationToken ct)
    {
        var options = new List<MultiTeamDrawOptionDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int attempts = Math.Max(20, optionsCount * 12);

        for (int attempt = 0; attempt < attempts && options.Count < optionsCount; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            List<TeamState> teams = CreateEmptyTeams(capacities);
            List<Guid> keepers = startingGoalkeepers.ToList();
            if (attempt > 0)
                Shuffle(keepers, random);
            AssignStartingGoalkeepers(teams, keepers);

            HashSet<Guid> startingSet = startingGoalkeepers.ToHashSet();
            List<Guid> field = players
                .Select(player => player.Id)
                .Where(playerId => !startingSet.Contains(playerId))
                .ToList();

            if (attempt == 0)
            {
                field = field
                    .OrderByDescending(playerId => strengths[playerId])
                    .ThenBy(playerId => playerId)
                    .ToList();
                FillSnake(teams, field);
            }
            else
            {
                Shuffle(field, random);
                FillRoundRobin(teams, field, random.Next(teams.Count));
            }

            double baselineDifference = StrengthDifference(teams, strengths);
            ImproveBySwapping(teams, strengths, ct);
            string key = BuildOptionKey(teams);
            if (seen.Add(key))
                options.Add(ToOption(teams, strengths, baselineDifference));
        }

        return options
            .OrderBy(option => option.StrengthDifference)
            .ThenBy(BuildOptionKey)
            .ToList();
    }

    private static List<TeamState> CreateEmptyTeams(IReadOnlyList<int> capacities) =>
        capacities.Select(capacity => new TeamState(capacity)).ToList();

    private static void AssignStartingGoalkeepers(
        IReadOnlyList<TeamState> teams,
        IReadOnlyList<Guid> goalkeeperIds)
    {
        for (int index = 0; index < goalkeeperIds.Count; index++)
        {
            teams[index].Players.Add(goalkeeperIds[index]);
            teams[index].GoalkeeperId = goalkeeperIds[index];
        }
    }

    private static void FillSnake(IReadOnlyList<TeamState> teams, IReadOnlyList<Guid> players)
    {
        int teamIndex = 0;
        int direction = 1;
        foreach (Guid playerId in players)
        {
            int checkedTeams = 0;
            while (teams[teamIndex].Players.Count >= teams[teamIndex].Capacity)
            {
                AdvanceSnakeCursor(ref teamIndex, ref direction, teams.Count);
                if (++checkedTeams > teams.Count * 2)
                    throw new InvalidOperationException("Não há vaga disponível para distribuir o jogador.");
            }

            teams[teamIndex].Players.Add(playerId);
            AdvanceSnakeCursor(ref teamIndex, ref direction, teams.Count);
        }
    }

    private static void AdvanceSnakeCursor(ref int teamIndex, ref int direction, int teamCount)
    {
        if (teamCount == 1)
            return;

        if (direction > 0 && teamIndex == teamCount - 1)
            direction = -1;
        else if (direction < 0 && teamIndex == 0)
            direction = 1;

        teamIndex += direction;
    }

    private static void FillRoundRobin(
        IReadOnlyList<TeamState> teams,
        IReadOnlyList<Guid> players,
        int startIndex)
    {
        int index = startIndex;
        foreach (Guid playerId in players)
        {
            while (teams[index].Players.Count >= teams[index].Capacity)
                index = (index + 1) % teams.Count;
            teams[index].Players.Add(playerId);
            index = (index + 1) % teams.Count;
        }
    }

    private static void ImproveBySwapping(
        IReadOnlyList<TeamState> teams,
        IReadOnlyDictionary<Guid, double> strengths,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            double currentDifference = StrengthDifference(teams, strengths);
            Swap? bestSwap = null;
            double bestDifference = currentDifference;

            for (int firstTeam = 0; firstTeam < teams.Count - 1; firstTeam++)
            {
                for (int secondTeam = firstTeam + 1; secondTeam < teams.Count; secondTeam++)
                {
                    for (int firstPlayer = 0; firstPlayer < teams[firstTeam].Players.Count; firstPlayer++)
                    {
                        Guid firstId = teams[firstTeam].Players[firstPlayer];
                        if (firstId == teams[firstTeam].GoalkeeperId)
                            continue;

                        for (int secondPlayer = 0; secondPlayer < teams[secondTeam].Players.Count; secondPlayer++)
                        {
                            Guid secondId = teams[secondTeam].Players[secondPlayer];
                            if (secondId == teams[secondTeam].GoalkeeperId)
                                continue;

                            double difference = DifferenceAfterSwap(
                                teams, strengths, firstTeam, firstId, secondTeam, secondId);
                            if (difference + Epsilon < bestDifference)
                            {
                                bestDifference = difference;
                                bestSwap = new Swap(firstTeam, firstPlayer, secondTeam, secondPlayer);
                            }
                        }
                    }
                }
            }

            if (bestSwap is null)
                return;

            SwapPlayers(teams, bestSwap);
        }
    }

    private static double DifferenceAfterSwap(
        IReadOnlyList<TeamState> teams,
        IReadOnlyDictionary<Guid, double> strengths,
        int firstTeam,
        Guid firstId,
        int secondTeam,
        Guid secondId)
    {
        double minimum = double.MaxValue;
        double maximum = double.MinValue;

        for (int index = 0; index < teams.Count; index++)
        {
            double strength = teams[index].Players.Sum(playerId => strengths[playerId]);
            if (index == firstTeam)
                strength += strengths[secondId] - strengths[firstId];
            else if (index == secondTeam)
                strength += strengths[firstId] - strengths[secondId];

            minimum = Math.Min(minimum, strength);
            maximum = Math.Max(maximum, strength);
        }

        return maximum - minimum;
    }

    private static void SwapPlayers(IReadOnlyList<TeamState> teams, Swap swap)
    {
        Guid first = teams[swap.FirstTeam].Players[swap.FirstPlayer];
        teams[swap.FirstTeam].Players[swap.FirstPlayer] = teams[swap.SecondTeam].Players[swap.SecondPlayer];
        teams[swap.SecondTeam].Players[swap.SecondPlayer] = first;
    }

    private static MultiTeamDrawOptionDto ToOption(
        IReadOnlyList<TeamState> teams,
        IReadOnlyDictionary<Guid, double> strengths,
        double baselineDifference)
    {
        List<MultiTeamDto> result = teams.Select(team => new MultiTeamDto(
            team.Players.ToList(),
            team.GoalkeeperId,
            team.Players.Sum(playerId => strengths[playerId]))).ToList();
        double difference = result.Max(team => team.Strength) - result.Min(team => team.Strength);
        return new MultiTeamDrawOptionDto(result, difference, baselineDifference);
    }

    private static double StrengthDifference(
        IReadOnlyList<TeamState> teams,
        IReadOnlyDictionary<Guid, double> strengths)
    {
        IEnumerable<double> totals = teams.Select(team => team.Players.Sum(playerId => strengths[playerId]));
        double[] values = totals.ToArray();
        return values.Max() - values.Min();
    }

    private static string BuildOptionKey(MultiTeamDrawOptionDto option) =>
        BuildOptionKey(option.Teams.Select(team => team.PlayerIds));

    private static string BuildOptionKey(IReadOnlyList<TeamState> teams) =>
        BuildOptionKey(teams.Select(team => (IReadOnlyList<Guid>)team.Players));

    private static string BuildOptionKey(IEnumerable<IReadOnlyList<Guid>> teams) =>
        string.Join("|", teams
            .Select(team => string.Join(",", team.OrderBy(playerId => playerId)))
            .OrderBy(key => key, StringComparer.Ordinal));

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int target = random.Next(index + 1);
            (values[index], values[target]) = (values[target], values[index]);
        }
    }

    private sealed class TeamState(int capacity)
    {
        public int Capacity { get; } = capacity;
        public List<Guid> Players { get; } = [];
        public Guid? GoalkeeperId { get; set; }
    }

    private sealed record Swap(int FirstTeam, int FirstPlayer, int SecondTeam, int SecondPlayer);
}
