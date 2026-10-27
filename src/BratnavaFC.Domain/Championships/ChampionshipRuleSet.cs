using System.Text.Json.Serialization;

namespace BratnavaFC.Domain.Championships;

/// <summary>Como o campeonato aparece no histórico e nas estatísticas gerais.</summary>
public enum HistoryMode
{
    Consolidated = 1,     // 1 card, conta como 1 partida
    SeparateGames = 2,    // cada jogo conta como uma partida
    ChampionshipOnly = 3  // fora do histórico e das estatísticas gerais
}

/// <summary>Que resultado (V/E/D) cada participante leva para as estatísticas.</summary>
public enum ResultMode
{
    PerGame = 1,      // V/E/D de cada jogo; só com SeparateGames
    Placement = 2,    // pela colocação final do time
    WinForAll = 3,    // "Vitória para todos"
    PresenceOnly = 4  // conta a presença, sem V/E/D
}

public enum PlacementResult
{
    Win = 1,
    Draw = 2,
    Loss = 3,
    None = 4
}

public enum MvpMode
{
    PerGame = 1,
    PerChampionship = 2,
    None = 3
}

public sealed record WinnerStaysRule(int ConsecutiveWinsLimit, WinnerStaysTieRule TieRule);

public sealed record GameEndRule(int? TimeLimitMinutes, int? GoalLimit);

public sealed record ChampionshipEndRule(ChampionshipEndMode Mode, int? TotalTimeMinutes, int? NumberOfGames);

public sealed record StandingsRule(
    StandingMode Mode,
    int WinPoints,
    int DrawPoints,
    IReadOnlyList<TiebreakerCriteria> Tiebreakers);

/// <summary>
/// Regras escolhidas pelo admin ao criar o campeonato. Também é o DTO do contrato
/// (ChampionshipRulesDto). As partes de formato viram as opções do motor.
/// </summary>
public sealed record ChampionshipRuleSet(
    HistoryMode HistoryMode,
    ResultMode ResultMode,
    IReadOnlyList<PlacementResult>? PlacementResults,
    MvpMode MvpMode,
    bool MvpCountsInGeneralStats,
    bool GoalsCountInGeneralStats,
    bool IncludeInTeamDraw,
    int MinGamesForPlacementResult,
    ChampionshipFormat Format,
    int RoundRobinTurns,
    WinnerStaysRule? WinnerStays,
    GameEndRule GameEnd,
    ChampionshipEndRule? ChampionshipEnd,
    StandingsRule Standings)
{
    /// <summary>Lança InvalidOperationException com a mensagem do primeiro problema.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(HistoryMode)) throw new InvalidOperationException("Modo de historico invalido.");
        if (!Enum.IsDefined(ResultMode)) throw new InvalidOperationException("Modo de resultado invalido.");
        if (!Enum.IsDefined(MvpMode)) throw new InvalidOperationException("Modo de MVP invalido.");
        if (!Enum.IsDefined(Format)) throw new InvalidOperationException("Formato invalido.");

        if (ResultMode == ResultMode.PerGame && HistoryMode != HistoryMode.SeparateGames)
            throw new InvalidOperationException("Resultado por jogo so vale quando cada jogo conta como partida.");

        if (ResultMode == ResultMode.Placement)
        {
            if (PlacementResults is null || PlacementResults.Count == 0)
                throw new InvalidOperationException("Informe o resultado de cada colocacao.");
            if (PlacementResults.Any(r => !Enum.IsDefined(r)))
                throw new InvalidOperationException("Resultado de colocacao invalido.");
        }

        if (MinGamesForPlacementResult < 0)
            throw new InvalidOperationException("O minimo de jogos nao pode ser negativo.");

        if (Format is ChampionshipFormat.RoundRobin or ChampionshipFormat.RoundRobinWithFinal
            && RoundRobinTurns is not (1 or 2))
            throw new InvalidOperationException("O numero de turnos deve ser 1 ou 2.");

        if (Format == ChampionshipFormat.WinnerStays && WinnerStays is null)
            throw new InvalidOperationException("Informe as regras do fica quem ganha.");

        // As opções do motor validam o restante (pontos, desempates, tempos e limites).
        _ = ToGameEndOptions();
        _ = ToStandingsRules();
        if (WinnerStays is not null) _ = ToWinnerStaysOptions();
        if (ChampionshipEnd is not null) _ = ToChampionshipEndOptions();
    }

    [JsonIgnore]
    public int MinimumTeams => Format == ChampionshipFormat.WinnerStays ? 3 : 2;

    public GameEndOptions ToGameEndOptions() => new(
        GameEnd.TimeLimitMinutes.HasValue ? TimeSpan.FromMinutes(GameEnd.TimeLimitMinutes.Value) : null,
        GameEnd.GoalLimit);

    public StandingsRules ToStandingsRules() => new(
        Standings.Mode, Standings.WinPoints, Standings.DrawPoints, Standings.Tiebreakers ?? []);

    public WinnerStaysOptions ToWinnerStaysOptions() =>
        WinnerStays is null
            ? throw new InvalidOperationException("Informe as regras do fica quem ganha.")
            : new WinnerStaysOptions(WinnerStays.ConsecutiveWinsLimit, WinnerStays.TieRule);

    public ChampionshipEndOptions? ToChampionshipEndOptions() => ChampionshipEnd?.Mode switch
    {
        null => null,
        ChampionshipEndMode.TotalTime => ChampionshipEndOptions.ByTotalTime(
            TimeSpan.FromMinutes(ChampionshipEnd.TotalTimeMinutes
                ?? throw new InvalidOperationException("Informe o tempo total do campeonato."))),
        ChampionshipEndMode.NumberOfGames => ChampionshipEndOptions.ByNumberOfGames(
            ChampionshipEnd.NumberOfGames
                ?? throw new InvalidOperationException("Informe o numero de jogos do campeonato.")),
        ChampionshipEndMode.EqualGamesForAllTeams => ChampionshipEndOptions.WhenAllTeamsHaveEqualGames(),
        _ => throw new InvalidOperationException("Regra de fim do campeonato invalida.")
    };

    /// <summary>Resultado de quem terminou em <paramref name="position"/> (1 = campeão).</summary>
    public PlacementResult ResultForPosition(int position)
    {
        if (PlacementResults is null || PlacementResults.Count == 0) return PlacementResult.None;
        var index = Math.Clamp(position - 1, 0, PlacementResults.Count - 1);
        return PlacementResults[index];
    }

    /// <summary>Selos curtos mostrados na listagem e no histórico.</summary>
    public IReadOnlyList<string> BuildBadges()
    {
        var badges = new List<string>
        {
            HistoryMode switch
            {
                HistoryMode.Consolidated => "Conta como 1 partida",
                HistoryMode.SeparateGames => "Cada jogo conta como partida",
                _ => "Fora das estatísticas"
            }
        };

        switch (ResultMode)
        {
            case ResultMode.PerGame: badges.Add("V/E/D por jogo"); break;
            case ResultMode.WinForAll: badges.Add("Vitória para todos"); break;
            case ResultMode.PresenceOnly: badges.Add("Só presença"); break;
            case ResultMode.Placement when PlacementResults is { Count: > 0 }:
                badges.Add(string.Join(" · ", PlacementResults.Select((r, i) =>
                    $"{i + 1}º{(i == PlacementResults.Count - 1 && i > 0 ? "+" : "")} {Short(r)}")));
                break;
        }

        badges.Add(MvpMode switch
        {
            MvpMode.PerGame => "MVP por jogo",
            MvpMode.PerChampionship => "MVP do campeonato",
            _ => "Sem MVP"
        });

        return badges;
    }

    private static string Short(PlacementResult result) => result switch
    {
        PlacementResult.Win => "V",
        PlacementResult.Draw => "E",
        PlacementResult.Loss => "D",
        _ => "—"
    };
}
