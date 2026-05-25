namespace BratnavaFC.Domain.Dtos.Payments;

public enum PendingPaymentType { Monthly = 0, Extra = 1 }

public sealed class PendingPaymentItemDto
{
    /// <summary>Chave única para o frontend (ex: "m-2026-4" ou "e-{chargeId}").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Descrição legível (ex: "Abril 2026" ou nome da cobrança extra).</summary>
    public string Description { get; init; } = string.Empty;

    public decimal Amount     { get; init; }
    public decimal Discount   { get; init; }
    public decimal FinalAmount { get; init; }

    public PendingPaymentType Type { get; init; }

    /// <summary>Preenchido apenas para mensalidades.</summary>
    public int? Year  { get; init; }

    /// <summary>Preenchido apenas para mensalidades.</summary>
    public int? Month { get; init; }

    /// <summary>Preenchido apenas para cobranças extras.</summary>
    public Guid? ChargeId { get; init; }

    /// <summary>
    /// Indica se o item já está pago.
    /// Usado pelo modal para pré-marcar itens e permitir desfazer pagamentos.
    /// </summary>
    public bool IsPaid { get; init; }
}
