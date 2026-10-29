using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static BratnavaFC.Application.TeamGeneration.StrategyHelpers;

namespace BratnavaFC.Application.TeamGeneration.Championship;

/// <summary>Um time sorteado: jogadores, goleiro e a soma da força (mesma medida da partida).</summary>
public sealed record ChampionshipDrawTeam(IReadOnlyList<Guid> PlayerIds, Guid? GoalkeeperPlayerId, double Strength);

/// <summary>Uma opção de sorteio. Quem ficou de fora (manual, goleiros fora do sorteio) vai em Unassigned.</summary>
public sealed record ChampionshipDrawOption(
    IReadOnlyList<ChampionshipDrawTeam> Teams,
    IReadOnlyList<Guid> UnassignedPlayerIds,
    double StrengthDifference);

/// <summary>
/// Sorteio do campeonato com os mesmos métodos da partida (Manual, Aleatório, Algoritmo,
/// Por vitórias e Por perfil) e a quantidade de times como mais um parâmetro.
///
/// Não mexe no sorteio da partida: com 2 times usa as próprias estratégias dela; com 3 ou mais
/// aplica a mesma ideia de cada método para N times. Os goleiros são os marcados antes do
/// sorteio; com "incluir goleiros", cada time recebe um antes dos demais.
/// </summary>
public sealed class ChampionshipTeamDrawService
{
    private const int MaximumOptions = 10;

    private readonly IPlayerStatsService _stats;
    private readonly TeamGenerationService _twoTeams;
    private readonly MultiTeamGenerationService _multiTeam;

    public ChampionshipTeamDrawService(IPlayerStatsService stats, ILoggerFactory? loggerFactory = null)
    {
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _twoTeams = new TeamGenerationService(stats, loggerFactory ?? NullLoggerFactory.Instance);
        _multiTeam = new MultiTeamGenerationService(stats);
    }

    public async Task<Result<List<ChampionshipDrawOption>>> GenerateAsync(
        List<PlayerRequestDto> players,
        int teamCount,
        StrategyType strategy,
        bool includeGoalkeepers,
        int optionsCount = 3,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(players);
        if (teamCount < 2)
            return Fail("A quantidade de times deve ser pelo menos 2.");
        if (!Enum.IsDefined(strategy))
            return Fail("Método de sorteio inválido.");
        if (players.Select(p => p.Id).Distinct().Count() != players.Count)
            return Fail("A lista de jogadores contém IDs repetidos.");
        optionsCount = Math.Clamp(optionsCount, 1, MaximumOptions);

        if (strategy == StrategyType.Manual)
        {
            // Como na partida: todos ficam sem time e o admin monta na etapa Times.
            var empty = Enumerable.Range(0, teamCount)
                .Select(_ => new ChampionshipDrawTeam([], null, 0)).ToList();
            return Result<List<ChampionshipDrawOption>>.Ok(
                [new ChampionshipDrawOption(empty, players.Select(p => p.Id).ToList(), 0)]);
        }

        var pool = includeGoalkeepers ? players : players.Where(p => !p.IsGoalkeeper).ToList();
        if (pool.Count < teamCount)
            return Fail("Há menos jogadores para sortear do que times.");

        var statsResult = await LoadStatsByPlayerId(_stats, players, ct).ConfigureAwait(false);
        if (!statsResult.Success)
            return Fail(statsResult.Error ?? "Não foi possível calcular a força dos jogadores.");
        var statsById = statsResult.Data!;
        PlayerStats StatsOf(PlayerRequestDto p) => GetOrCreateStats(statsById, p.Id, p.Name);

        if (strategy == StrategyType.Profile)
        {
            var unrated = pool.Where(p => !p.IsGoalkeeper).FirstOrDefault(p =>
            {
                var s = StatsOf(p);
                return !s.AttackRatingNorm.HasValue || !s.DefenseRatingNorm.HasValue || !s.PhysicalRatingNorm.HasValue;
            });
            if (unrated is not null)
                return Fail($"Por perfil precisa das notas de ataque, defesa e físico de todos os jogadores de linha (falta {unrated.Name}).");
        }

        List<List<List<Guid>>> byOption = teamCount == 2
            ? await TwoTeamsAsync(pool, strategy, optionsCount, ct).ConfigureAwait(false)
            : strategy switch
            {
                StrategyType.Random => RandomTeams(pool, teamCount, optionsCount),
                StrategyType.Algorithm => await BalancedTeamsAsync(pool, teamCount, optionsCount, ct).ConfigureAwait(false),
                StrategyType.GroupByWins => ByWinsTeams(pool, teamCount, optionsCount, StatsOf),
                StrategyType.Profile => ByProfileTeams(pool, teamCount, optionsCount, StatsOf),
                _ => [],
            };

        if (byOption.Count == 0 || byOption.All(o => o.All(t => t.Count == 0)))
            return Fail("Não foi possível montar uma opção de times com esse método.");

        var playersById = players.ToDictionary(p => p.Id);
        double WeightOf(Guid id) => EffectiveWeight(StatsOf(playersById[id]));

        var seen = new HashSet<string>();
        var options = new List<ChampionshipDrawOption>();
        foreach (var option in byOption)
        {
            if (includeGoalkeepers)
                SpreadGoalkeepers(option, id => playersById[id].IsGoalkeeper, WeightOf);
            if (!seen.Add(Key(option))) continue;

            var assigned = option.SelectMany(t => t).ToHashSet();
            var teamsDto = option.Select(team => new ChampionshipDrawTeam(
                team,
                team.Cast<Guid?>().FirstOrDefault(id => playersById[id!.Value].IsGoalkeeper),
                Math.Round(team.Sum(WeightOf), 4))).ToList();
            var strengths = teamsDto.Select(t => t.Strength).ToList();
            options.Add(new ChampionshipDrawOption(
                teamsDto,
                players.Where(p => !assigned.Contains(p.Id)).Select(p => p.Id).ToList(),
                Math.Round(strengths.Max() - strengths.Min(), 4)));
        }

        // O Algoritmo já vem ordenado pelo critério dele; os outros, pelo equilíbrio.
        if (strategy != StrategyType.Algorithm)
            options = options.OrderBy(o => o.StrengthDifference).ToList();
        return Result<List<ChampionshipDrawOption>>.Ok(options.Take(optionsCount).ToList());
    }

    // ── 2 times: as estratégias da partida ──────────────────────────────────

    private async Task<List<List<List<Guid>>>> TwoTeamsAsync(
        List<PlayerRequestDto> pool, StrategyType strategy, int optionsCount, CancellationToken ct)
    {
        // Como o app da partida: metade para baixo; o Algoritmo já põe o ímpar no time mais forte.
        int perTeam = Math.Max(1, pool.Count / 2);

        // O Algoritmo da partida exige exatamente 1 goleiro por time e quebra com 3 ou mais;
        // os goleiros além dos 2 primeiros entram como jogadores de linha.
        var keptGoalkeepers = pool.Where(p => p.IsGoalkeeper).Take(2).Select(p => p.Id).ToHashSet();
        var input = pool.Select(p => p.IsGoalkeeper && !keptGoalkeepers.Contains(p.Id) ? p with { IsGoalkeeper = false } : p).ToList();

        TeamsOptionsResultDto? generated;
        try
        {
            generated = (await _twoTeams.GenerateAsync(input, strategy, perTeam, includeGoalkeepers: true, optionsCount, ct)
                .ConfigureAwait(false)).Data;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Sem combinação possível na busca da partida: cai no equilíbrio para N times.
            return await BalancedTeamsAsync(pool, 2, optionsCount, ct).ConfigureAwait(false);
        }
        var options = (generated?.Options ?? [])
            .Select(o => new List<List<Guid>>
            {
                o.TeamA.Select(p => p.PlayerId).ToList(),
                o.TeamB.Select(p => p.PlayerId).ToList(),
            })
            .ToList();

        // Quem sobrou (o ímpar, ou os goleiros que o "Por perfil" deixa de fora) entra no time
        // com menos jogadores: no campeonato todo mundo do sorteio joga.
        foreach (var option in options)
        {
            foreach (var left in pool.Where(p => !option.Any(t => t.Contains(p.Id))).OrderByDescending(p => p.IsGoalkeeper))
                option.OrderBy(t => t.Count).ThenBy(t => t.Count(id => pool.First(p => p.Id == id).IsGoalkeeper)).First().Add(left.Id);
        }
        return options;
    }

    // ── 3+ times ─────────────────────────────────────────────────────────────

    private static List<List<List<Guid>>> RandomTeams(List<PlayerRequestDto> pool, int teamCount, int optionsCount)
    {
        var options = new List<List<List<Guid>>>();
        for (var attempt = 0; attempt < Math.Max(20, optionsCount * 10) && options.Count < optionsCount * 3; attempt++)
        {
            var shuffled = pool.OrderBy(_ => Random.Shared.Next()).ToList();
            options.Add(Distribute(shuffled, teamCount, i => i % teamCount));
        }
        return options;
    }

    private async Task<List<List<List<Guid>>>> BalancedTeamsAsync(
        List<PlayerRequestDto> pool, int teamCount, int optionsCount, CancellationToken ct)
    {
        var goalkeepers = pool.Where(p => p.IsGoalkeeper).Select(p => p.Id).ToList();
        var draw = await _multiTeam.GenerateAsync(pool, teamCount, MultiTeamStrategy.Balanced, goalkeepers, optionsCount, ct: ct)
            .ConfigureAwait(false);
        return (draw.Data?.Options ?? [])
            .Select(o => o.Teams.Select(t => t.PlayerIds.ToList()).ToList())
            .ToList();
    }

    /// <summary>Ordena por vitórias (como na partida) e distribui em padrões diferentes.</summary>
    private static List<List<List<Guid>>> ByWinsTeams(
        List<PlayerRequestDto> pool, int teamCount, int optionsCount, Func<PlayerRequestDto, PlayerStats> statsOf)
    {
        var ordered = pool
            .OrderByDescending(p => statsOf(p).Wins)
            .ThenByDescending(p => EffectiveWeight(statsOf(p)))
            .ThenBy(p => p.Id)
            .ToList();
        Func<int, int>[] patterns =
        [
            i => i % teamCount,                                  // 1, 2, 3, 1, 2, 3…
            i => (i + 1) % teamCount,                            // começa pelo segundo time
            i => (i / teamCount) % 2 == 0                         // serpentina: 1, 2, 3, 3, 2, 1…
                ? i % teamCount
                : teamCount - 1 - i % teamCount,
        ];
        return patterns.Take(Math.Max(optionsCount, 1)).Select(p => Distribute(ordered, teamCount, p)).ToList();
    }

    /// <summary>
    /// Como o "Por perfil" da partida: ofensivos e defensivos espalhados entre os times,
    /// equilibrando a soma do físico. Goleiros entram um por time.
    /// </summary>
    private static List<List<List<Guid>>> ByProfileTeams(
        List<PlayerRequestDto> pool, int teamCount, int optionsCount, Func<PlayerRequestDto, PlayerStats> statsOf)
    {
        var options = new List<List<List<Guid>>>();
        var capacities = Capacities(pool.Count, teamCount);
        var goalkeepers = pool.Where(p => p.IsGoalkeeper).ToList();
        var field = pool.Where(p => !p.IsGoalkeeper).ToList();
        int Profile(PlayerRequestDto p)
        {
            var s = statsOf(p);
            return AttackOf(s) > DefenseOf(s) ? 0 : DefenseOf(s) > AttackOf(s) ? 1 : 2;
        }
        var ordered = field
            .OrderBy(Profile)
            .ThenByDescending(p => PhysicalOf(statsOf(p)))
            .ThenBy(p => p.Id)
            .ToList();

        for (var start = 0; start < teamCount && options.Count < optionsCount; start++)
        {
            var teams = Enumerable.Range(0, teamCount).Select(_ => new List<Guid>()).ToList();
            var physical = new double[teamCount];
            var profileCount = new int[teamCount, 3];
            for (var i = 0; i < goalkeepers.Count; i++)
                teams[(i + start) % teamCount].Add(goalkeepers[i].Id);

            foreach (var player in ordered)
            {
                var profile = Profile(player);
                var target = Enumerable.Range(0, teamCount)
                    .Select(i => (i + start) % teamCount)
                    .Where(i => teams[i].Count < capacities[i])
                    .OrderBy(i => profileCount[i, profile])
                    .ThenBy(i => physical[i])
                    .First();
                teams[target].Add(player.Id);
                physical[target] += PhysicalOf(statsOf(player));
                profileCount[target, profile]++;
            }
            options.Add(teams);
        }
        return options;
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    /// <summary>
    /// Goleiros primeiro, um por time; depois os demais na ordem dada, cada um no time que o
    /// padrão indicar (ou no próximo com vaga). Os tamanhos ficam iguais ou com 1 de diferença.
    /// </summary>
    private static List<List<Guid>> Distribute(List<PlayerRequestDto> ordered, int teamCount, Func<int, int> pattern)
    {
        var capacities = Capacities(ordered.Count, teamCount);
        var teams = Enumerable.Range(0, teamCount).Select(_ => new List<Guid>()).ToList();
        var goalkeepers = ordered.Where(p => p.IsGoalkeeper).Take(teamCount).ToList();
        for (var i = 0; i < goalkeepers.Count; i++)
            teams[i].Add(goalkeepers[i].Id);

        var rest = ordered.Where(p => !goalkeepers.Contains(p)).ToList();
        for (var i = 0; i < rest.Count; i++)
        {
            var wanted = pattern(i);
            var target = Enumerable.Range(0, teamCount)
                .Select(offset => (wanted + offset) % teamCount)
                .First(t => teams[t].Count < capacities[t]);
            teams[target].Add(rest[i].Id);
        }
        return teams;
    }

    private static int[] Capacities(int playerCount, int teamCount) =>
        Enumerable.Range(0, teamCount)
            .Select(i => playerCount / teamCount + (i < playerCount % teamCount ? 1 : 0))
            .ToArray();

    /// <summary>
    /// Garante um goleiro por time quando há goleiros para isso: um time com dois cede um
    /// para o time sem goleiro e recebe de volta o jogador de linha de força mais parecida.
    /// </summary>
    private static void SpreadGoalkeepers(List<List<Guid>> teams, Func<Guid, bool> isGoalkeeper, Func<Guid, double> weightOf)
    {
        while (true)
        {
            var without = teams.FirstOrDefault(t => t.Count > 0 && !t.Any(isGoalkeeper));
            var extra = teams.FirstOrDefault(t => t.Count(isGoalkeeper) > 1);
            if (without is null || extra is null) return;

            var goalkeeper = extra.Where(isGoalkeeper).OrderBy(weightOf).First();
            var swap = without.Where(id => !isGoalkeeper(id))
                .OrderBy(id => Math.Abs(weightOf(id) - weightOf(goalkeeper)))
                .FirstOrDefault();
            extra.Remove(goalkeeper);
            without.Add(goalkeeper);
            if (swap != Guid.Empty)
            {
                without.Remove(swap);
                extra.Add(swap);
            }
        }
    }

    private static string Key(IEnumerable<List<Guid>> teams) =>
        string.Join("|", teams.Select(t => string.Join(",", t.OrderBy(id => id))).OrderBy(k => k, StringComparer.Ordinal));

    private static Result<List<ChampionshipDrawOption>> Fail(string message) =>
        Result<List<ChampionshipDrawOption>>.Fail(message);
}
