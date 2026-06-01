namespace BratnavaFC.Domain.Dtos;

public sealed class GoalDto
{
    public Guid GoalId { get; set; }
    public Guid ScorerPlayerId { get; set; }
    public Guid ScorerMatchPlayerId { get; set; }
    public string ScorerName { get; set; } = string.Empty;

    public Guid? AssistPlayerId { get; set; }
    public Guid? AssistMatchPlayerId { get; set; }

    public string? AssistName { get; set; }

    public int? TimeSeconds { get; set; }
    public string? Time { get; set; }

    /// <summary>
    /// Gol contra: o ponto vai para o time adversário do marcador.
    /// </summary>
    public bool IsOwnGoal { get; set; }

    /// <summary>Placar do Time A imediatamente após este gol. Calculado pelo backend.</summary>
    public int ScoreAAfter { get; set; }
    /// <summary>Placar do Time B imediatamente após este gol. Calculado pelo backend.</summary>
    public int ScoreBAfter { get; set; }
}
