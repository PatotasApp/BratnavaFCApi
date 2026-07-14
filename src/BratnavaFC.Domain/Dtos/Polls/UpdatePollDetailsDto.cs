namespace BratnavaFC.Domain.Dtos.Polls;

public sealed class UpdatePollDetailsDto
{
    /// <summary>null = sem alteracao; qualquer outro valor = substituir.</summary>
    public string? Title { get; set; }

    /// <summary>null = sem alteracao; "" = limpar; qualquer outro valor = substituir.</summary>
    public string? Description { get; set; }

    /// <summary>Apenas para eventos. null = sem alteracao; "" = limpar.</summary>
    public string? EventDate { get; set; }

    /// <summary>Apenas para eventos. null = sem alteracao; "" = limpar.</summary>
    public string? EventTime { get; set; }

    /// <summary>Apenas para eventos. null = sem alteracao; "" = limpar.</summary>
    public string? EventLocation { get; set; }

    /// <summary>Apenas para eventos. null = sem alteracao; "" = limpar.</summary>
    public string? EventIcon { get; set; }

    /// <summary>Apenas para eventos. null = sem alteracao.</summary>
    public decimal? CostAmount { get; set; }

    /// <summary>Apenas para eventos. null = sem alteracao; "" = limpar.</summary>
    public string? CostType { get; set; }
}
