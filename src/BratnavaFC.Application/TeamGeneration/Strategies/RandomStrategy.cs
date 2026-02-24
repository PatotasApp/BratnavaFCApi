using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

public sealed class RandomStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    private const int MinMatchesToBeNonNeutral = 3;
    private const double NeutralWinRate = 0.50;

    public RandomStrategy(IPlayerStatsService statsService)
    {
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
    }

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

        if (players.Count == 0)
            return new TeamsOptionsResultDto(new List<TeamOptionDto>());

        // pool (candidatos alocáveis)
        var pool = settings.IncludeGoalkeepers
            ? players.ToList()
            : players.Where(p => !p.IsGoalkeeper).ToList();

        var totalSlots = settings.PlayersPerTeam * 2;
        var maxAssignable = Math.Min(pool.Count, totalSlots);

        // Stats só dos candidatos (padrão)
        var statsById = await LoadStatsByPlayerId(pool, cancellationToken).ConfigureAwait(false);

        double WeightOf(Guid id)
        {
            if (statsById.TryGetValue(id, out var s))
                return EffectiveWinRate(s);

            return NeutralWinRate;
        }

        bool IsGk(Guid id)
            => players.FirstOrDefault(p => p.Id == id)?.IsGoalkeeper == true;

        var rnd = new Random();

        var seen = new HashSet<string>();
        var options = new List<TeamOptionDto>();

        // tenta gerar até optionsCount opções únicas (com limite pra não loopar infinito)
        var attemptsLimit = Math.Max(20, optionsCount * 10);
        for (int attempt = 0; attempt < attemptsLimit && options.Count < optionsCount; attempt++)
        {
            var shuffled = pool.OrderBy(_ => rnd.Next()).ToList();

            var chosen = shuffled.Take(maxAssignable).ToList();

            var teamAIds = chosen.Take(settings.PlayersPerTeam).Select(p => p.Id).ToList();
            var teamBIds = chosen.Skip(settings.PlayersPerTeam).Take(settings.PlayersPerTeam).Select(p => p.Id).ToList();

            var key = BuildTeamsKey(teamAIds, teamBIds);
            if (!seen.Add(key)) continue;

            var assigned = new HashSet<Guid>(teamAIds.Concat(teamBIds));

            // unassigned: quem não entrou + (se IncludeGoalkeepers=false) os GKs também (já vão ficar fora do pool)
            var unassignedIds = players
                .Where(p => !assigned.Contains(p.Id))
                .Select(p => p.Id)
                .ToList();

            if (!settings.IncludeGoalkeepers)
            {
                foreach (var gk in players.Where(p => p.IsGoalkeeper).Select(p => p.Id))
                    if (!unassignedIds.Contains(gk))
                        unassignedIds.Add(gk);
            }

            var teamA = teamAIds.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();
            var teamB = teamBIds.Select(id => new PlayerWeightDto(id, WeightOf(id))).ToList();

            var unassigned = unassignedIds
                .Distinct()
                .Select(id =>
                {
                    // se não carregamos stats (ex: GK quando IncludeGoalkeepers=false), neutro
                    return new PlayerWeightDto(id, WeightOf(id));
                })
                .ToList();

            var teamAWeight = teamA.Sum(x => x.Weight);
            var teamBWeight = teamB.Sum(x => x.Weight);

            var balanceDiff = Math.Abs(teamAWeight - teamBWeight);

            var gkDiff = settings.IncludeGoalkeepers
                ? Math.Abs(teamAIds.Count(IsGk) - teamBIds.Count(IsGk))
                : 0;

            var synergyTotal = 0.0;
            var score = balanceDiff;

            options.Add(new TeamOptionDto(
                TeamA: teamA,
                TeamB: teamB,
                Unassigned: unassigned,
                TeamAWeight: teamAWeight,
                TeamBWeight: teamBWeight,
                BalanceDiff: balanceDiff,
                GoalkeeperDiff: gkDiff,
                SynergyTotal: synergyTotal,
                Score: score
            ));
        }

        // ordena opções pela melhor (menor diff)
        options = options.OrderBy(o => o.Score).ToList();

        // se por algum motivo não gerou nada (pool vazio etc), devolve 1 opção vazia com tudo unassigned
        if (options.Count == 0)
        {
            var allUn = players.Select(p => new PlayerWeightDto(p.Id, WeightOf(p.Id))).ToList();
            options.Add(new TeamOptionDto(
                TeamA: new(),
                TeamB: new(),
                Unassigned: allUn,
                TeamAWeight: 0,
                TeamBWeight: 0,
                BalanceDiff: 0,
                GoalkeeperDiff: 0,
                SynergyTotal: 0,
                Score: 0
            ));
        }

        return new TeamsOptionsResultDto(options);
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
        var statsList = await _statsService.EnrichPlayersAsync(players, cancellationToken).ConfigureAwait(false);
        return statsList.ToDictionary(s => s.PlayerId, s => s);
    }
}