namespace BratnavaFC.Domain.Dtos.Polls;

public sealed class UpdatePollDeadlineDto
{
    /// <summary>Nova data de vencimento no formato "yyyy-MM-dd". Ignorado quando ClearDeadline = true.</summary>
    public string? DeadlineDate { get; set; }

    /// <summary>Novo horário de vencimento no formato "HH:mm". Opcional; null mantém sem horário fixo.</summary>
    public string? DeadlineTime { get; set; }

    /// <summary>Quando true, remove completamente o prazo (DeadlineDate e DeadlineTime ficam null).</summary>
    public bool ClearDeadline { get; set; } = false;
}
