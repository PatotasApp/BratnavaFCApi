namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class GroupPendingPaymentsDto
{
    public Guid GroupId { get; init; }
    public string GroupName { get; init; } = string.Empty;
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public PendingPaymentItemDto[] Items { get; init; } = [];
    public decimal Total { get; init; }
}

public sealed class ExitPendingPaymentsDto
{
    public GroupPendingPaymentsDto[] Groups { get; init; } = [];
    public decimal Total { get; init; }
    public int Count { get; init; }
    public bool HasPending => Count > 0;
}

public sealed class ExitDebtAlertDto
{
    public Guid NotificationId { get; init; }
    public Guid GroupId { get; init; }
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal Total { get; init; }
    public DateTime CreatedAt { get; init; }
}
