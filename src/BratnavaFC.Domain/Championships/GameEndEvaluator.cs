namespace BratnavaFC.Domain.Championships;

public static class GameEndEvaluator
{
    public static bool ShouldEnd(
        GameEndOptions options,
        TimeSpan elapsed,
        int teamAGoals,
        int teamBGoals)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (elapsed < TimeSpan.Zero)
            throw new InvalidOperationException("O tempo decorrido nao pode ser negativo.");
        if (teamAGoals < 0 || teamBGoals < 0)
            throw new InvalidOperationException("Placar nao pode ser negativo.");

        var reachedTimeLimit = options.TimeLimit.HasValue && elapsed >= options.TimeLimit.Value;
        var reachedGoalLimit = options.GoalLimit.HasValue &&
                               Math.Max(teamAGoals, teamBGoals) >= options.GoalLimit.Value;
        return reachedTimeLimit || reachedGoalLimit;
    }
}
