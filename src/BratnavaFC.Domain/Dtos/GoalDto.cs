namespace BratnavaFC.Domain.Dtos;

public sealed class GoalDto
{
    public Guid GoalId { get; set; }
    public Guid ScorerPlayerId { get; set; }
    public string ScorerName { get; set; } = string.Empty;

    public Guid? AssistPlayerId { get; set; }
    public string? AssistName { get; set; }

    public int? TimeSeconds { get; set; }
    public string? Time { get; set; }
}
