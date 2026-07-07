namespace BratnavaFC.Domain.Dtos.Polls;

public sealed class UpdatePollDetailsDto
{
    /// <summary>null = sem alteração; "" = limpar; qualquer outro valor = substituir.</summary>
    public string? Description { get; set; }

    /// <summary>Apenas para eventos. null = sem alteração.</summary>
    public decimal? CostAmount { get; set; }

    /// <summary>Apenas para eventos. null = sem alteração; "" = limpar.</summary>
    public string? CostType { get; set; }
}
