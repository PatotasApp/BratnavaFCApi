using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.TeamGeneration.Strategies;

public sealed class ManualStrategy : ITeamGenerationStrategy
{
    private readonly IPlayerStatsService _statsService;

    // Mantém alinhado com as outras strategies (peso = EffectiveWinRate)
    private const int MinMatchesToBeNonNeutral = 3;
    private const double NeutralWinRate = 0.50;

    public ManualStrategy(IPlayerStatsService statsService)
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

        if (players.Count == 0)
            return new TeamsOptionsResultDto(new List<TeamOptionDto>());

        // Aqui eu sigo a regra do "IncludeGoalkeepers":
        // - se false, GKs continuam vindo no unassigned (de qualquer forma já seriam)
        // - mas para calcular peso, vamos enriquecer stats só dos candidates (mesmo padrão)
        var candidatePlayers = settings.IncludeGoalkeepers
            ? players.ToList()
            : players.Where(p => !p.IsGoalkeeper).ToList();

        var statsById = await LoadStatsByPlayerId(candidatePlayers, cancellationToken).ConfigureAwait(false);

        // Unassigned = todos (manual não escala)
        var unassigned = players
            .Select(p =>
            {
                // se não carregamos stats (ex: GK quando IncludeGoalkeepers=false), peso = 0.50 neutro
                if (statsById.TryGetValue(p.Id, out var s))
                    return new PlayerWeightDto(p.Id, EffectiveWinRate(s));

                return new PlayerWeightDto(p.Id, NeutralWinRate);
            })
            .ToList();

        var opt = new TeamOptionDto(
            TeamA: new List<PlayerWeightDto>(),
            TeamB: new List<PlayerWeightDto>(),
            Unassigned: unassigned,
            TeamAWeight: 0,
            TeamBWeight: 0,
            BalanceDiff: 0,
            GoalkeeperDiff: 0,
            SynergyTotal: 0,
            Score: 0
        );

        // Manual => 1 opção apenas (mesmo se pedirem 3)
        return new TeamsOptionsResultDto(new List<TeamOptionDto> { opt });
    }

    private async Task<Dictionary<Guid, PlayerStats>> LoadStatsByPlayerId(
        List<PlayerRequestDto> players,
        CancellationToken cancellationToken)
    {
        var statsList = await _statsService.EnrichPlayersAsync(players, cancellationToken).ConfigureAwait(false);
        return statsList.ToDictionary(s => s.PlayerId, s => s);
    }
}