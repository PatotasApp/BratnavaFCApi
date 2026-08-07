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
    public List<AssistPairDto>      AssistPairs          { get; set; } = [];
    public List<SelectedPlayerDto>  Players              { get; set; } = [];
    public List<PlayerBreakdownDto> PlayerBreakdown      { get; set; } = [];
    public List<FormationMatchDto>  Matches              { get; set; } = [];
}

/// <summary>Contribuição individual de cada selecionado nas partidas em que jogaram juntos.</summary>
public sealed class PlayerBreakdownDto
{
    public Guid   Id           { get; set; }
    public string Name         { get; set; } = "";
    public bool   IsGoalkeeper { get; set; }
    public int    Games        { get; set; }
    public int    Wins         { get; set; }
    public int    Draws        { get; set; }
    public int    Losses       { get; set; }
    public double WinRate      { get; set; }
    public int    Goals        { get; set; }
    public int    Assists      { get; set; }
    public int    Mvps         { get; set; }
}

/// <summary>Uma partida em que a formação selecionada jogou junta (perspectiva do time dominante).</summary>
public sealed class FormationMatchDto
{
    public Guid           MatchId         { get; set; }
    public DateTimeOffset PlayedAt        { get; set; }
    public int            GoalsFor        { get; set; }
    public int            GoalsAgainst    { get; set; }
    public string?        PlaceName       { get; set; }
    public string?        ColorForHex     { get; set; }
    public string?        ColorAgainstHex { get; set; }
    public int            Result          { get; set; } // 1 vitória, 0 empate, -1 derrota
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
