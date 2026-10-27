namespace BratnavaFC.Domain.Championships;

public enum ChampionshipFormat
{
    RoundRobin = 1,
    WinnerStays = 2,
    Knockout = 3,
    RoundRobinWithFinal = 4
}

public enum WinnerStaysTieRule
{
    BothLeave = 1,
    LongestOnFieldLeaves = 2,
    PenaltyShootout = 3
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

public enum ChampionshipEndMode
{
    TotalTime = 1,
    NumberOfGames = 2,
    EqualGamesForAllTeams = 3
}

public sealed record WinnerStaysOptions
{
    public WinnerStaysOptions(int consecutiveWinsLimit, WinnerStaysTieRule tieRule)
    {
        if (consecutiveWinsLimit < 0)
            throw new InvalidOperationException("O limite de vitorias seguidas nao pode ser negativo.");

        ConsecutiveWinsLimit = consecutiveWinsLimit;
        TieRule = tieRule;
    }

    public int ConsecutiveWinsLimit { get; }
    public WinnerStaysTieRule TieRule { get; }
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

public sealed record ChampionshipEndOptions
{
    private ChampionshipEndOptions(ChampionshipEndMode mode, TimeSpan? totalTime, int? numberOfGames)
    {
        Mode = mode;
        TotalTime = totalTime;
        NumberOfGames = numberOfGames;
    }

    public ChampionshipEndMode Mode { get; }
    public TimeSpan? TotalTime { get; }
    public int? NumberOfGames { get; }

    public static ChampionshipEndOptions ByTotalTime(TimeSpan totalTime)
    {
        if (totalTime <= TimeSpan.Zero)
            throw new InvalidOperationException("O tempo total do campeonato deve ser maior que zero.");

        return new ChampionshipEndOptions(ChampionshipEndMode.TotalTime, totalTime, null);
    }

    public static ChampionshipEndOptions ByNumberOfGames(int numberOfGames)
    {
        if (numberOfGames <= 0)
            throw new InvalidOperationException("O numero de jogos deve ser maior que zero.");

        return new ChampionshipEndOptions(ChampionshipEndMode.NumberOfGames, null, numberOfGames);
    }

    public static ChampionshipEndOptions WhenAllTeamsHaveEqualGames() =>
        new(ChampionshipEndMode.EqualGamesForAllTeams, null, null);
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
