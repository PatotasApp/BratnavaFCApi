namespace BratnavaFC.Domain.Dtos.TeamBuilder;

public sealed class TeamBuilderRequestDto
{
    public List<Guid> PlayerIds { get; set; } = [];
}

public sealed class TeamBuilderStatsDto
{
    public bool                    NeverPlayedTogether  { get; set; }
    public int                     TotalMatches         { get; set; }
    public int                     Wins                 { get; set; }
    public int                     Draws                { get; set; }
    public int                     Losses               { get; set; }
    public int                     GoalsScored          { get; set; }
    public int                     GoalsConceded        { get; set; }
    public int                     GoalsScoredByPlayers { get; set; }
    public List<AssistPairDto>     AssistPairs          { get; set; } = [];
    public List<SelectedPlayerDto> Players              { get; set; } = [];
}

public sealed class AssistPairDto
{
    public Guid   AssisterId   { get; set; }
    public string AssisterName { get; set; } = "";
    public Guid   ScorerId     { get; set; }
    public string ScorerName   { get; set; } = "";
    public int    Count        { get; set; }
}

public sealed class SelectedPlayerDto
{
    public Guid   Id           { get; set; }
    public string Name         { get; set; } = "";
    public bool   IsGoalkeeper { get; set; }
}
