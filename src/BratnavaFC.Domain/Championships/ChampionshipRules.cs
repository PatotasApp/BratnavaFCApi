namespace BratnavaFC.Domain.Championships;

public enum ChampionshipFormat
{
    RoundRobin = 1,
    WinnerStays = 2,
    // 3 era o mata-mata, descartado pelo Luis (27/10/2026): não reutilizar o número.
    RoundRobinWithFinal = 4
}

public enum WinnerStaysTieRule
{
    BothLeave = 1,
    LongestOnFieldLeaves = 2,
    PenaltyShootout = 3
}

/// <summary>Quem sai quando o time que fica atinge o limite de vitórias seguidas.</summary>
public enum WinnerStaysLimitRule
{
    WinnerLeaves = 1, // sai só o vencedor; o perdedor fica
    BothLeave = 2     // saem os dois (com 3 times é impossível: vira WinnerLeaves)
}

public enum StandingMode
{
    TotalPoints = 1,
    PointsPerGame = 2
}

public enum TiebreakerCriteria
{
    Points = 1,
    GoalDifference = 2,
    GoalsFor = 3,
    HeadToHead = 4,
    ExternalDecision = 5
}

public sealed record WinnerStaysOptions
{
    public WinnerStaysOptions(
        int consecutiveWinsLimit,
        WinnerStaysTieRule tieRule,
        WinnerStaysLimitRule limitRule = WinnerStaysLimitRule.WinnerLeaves)
    {
        if (consecutiveWinsLimit < 0)
            throw new InvalidOperationException("O limite de vitorias seguidas nao pode ser negativo.");

        ConsecutiveWinsLimit = consecutiveWinsLimit;
        TieRule = tieRule;
        LimitRule = limitRule;
    }

    public int ConsecutiveWinsLimit { get; }
    public WinnerStaysTieRule TieRule { get; }
    public WinnerStaysLimitRule LimitRule { get; }
}

public sealed record GameEndOptions
{
    public GameEndOptions(TimeSpan? timeLimit, int? goalLimit)
    {
        if (!timeLimit.HasValue && !goalLimit.HasValue)
            throw new InvalidOperationException("Informe o tempo ou o limite de gols do jogo.");
        if (timeLimit <= TimeSpan.Zero)
            throw new InvalidOperationException("O tempo do jogo deve ser maior que zero.");
        if (goalLimit <= 0)
            throw new InvalidOperationException("O limite de gols deve ser maior que zero.");

        TimeLimit = timeLimit;
        GoalLimit = goalLimit;
    }

    public TimeSpan? TimeLimit { get; }
    public int? GoalLimit { get; }
}

public sealed record StandingsRules
{
    public StandingsRules(
        StandingMode mode,
        int winPoints,
        int drawPoints,
        IEnumerable<TiebreakerCriteria> tiebreakers)
    {
        if (winPoints < 0 || drawPoints < 0)
            throw new InvalidOperationException("Os pontos de vitoria e empate nao podem ser negativos.");

        ArgumentNullException.ThrowIfNull(tiebreakers);
        var criteria = tiebreakers.ToArray();
        if (criteria.Length == 0)
            throw new InvalidOperationException("Informe ao menos um criterio de classificacao.");
        if (criteria.Distinct().Count() != criteria.Length)
            throw new InvalidOperationException("Os criterios de classificacao nao podem se repetir.");
        if (!criteria.Contains(TiebreakerCriteria.Points))
            throw new InvalidOperationException("A classificacao deve incluir o criterio de pontos.");

        Mode = mode;
        WinPoints = winPoints;
        DrawPoints = drawPoints;
        Tiebreakers = Array.AsReadOnly(criteria);
    }

    public StandingMode Mode { get; }
    public int WinPoints { get; }
    public int DrawPoints { get; }
    public IReadOnlyList<TiebreakerCriteria> Tiebreakers { get; }
}
