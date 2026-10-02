using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public sealed class ExtraChargePaymentEntity : BaseEntity
{
    private ExtraChargePaymentEntity() { } // EF

    public ExtraChargePaymentEntity(
        Guid extraChargeId,
        Guid playerId,
        Guid groupId,
        decimal amount)
    {
        if (extraChargeId == Guid.Empty) throw new InvalidOperationException("ExtraChargeId é obrigatório.");
        if (playerId == Guid.Empty)      throw new InvalidOperationException("PlayerId é obrigatório.");
        if (groupId == Guid.Empty)       throw new InvalidOperationException("GroupId é obrigatório.");
        if (amount < 0)                  throw new InvalidOperationException("Amount não pode ser negativo.");

        ExtraChargeId = extraChargeId;
        PlayerId      = playerId;
        GroupId       = groupId;
        Amount        = amount;
        Status        = PaymentStatus.Pending;
        Discount      = 0;
    }

    public Guid ExtraChargeId  { get; private set; }
    public Guid PlayerId       { get; private set; }
    public Guid GroupId        { get; private set; }

    public decimal Amount         { get; private set; }
    public decimal Discount       { get; private set; }
    public string? DiscountReason { get; private set; }

    /// <summary>Valor final após desconto. Se desconto ≥ valor, retorna 0.</summary>
    public decimal FinalAmount => Math.Max(0, Amount - Discount);

    public PaymentStatus Status  { get; private set; }
    public DateTime?     PaidAt  { get; private set; }

    public string? ProofBase64   { get; private set; }
    public string? ProofFileName { get; private set; }
    public string? ProofMimeType { get; private set; }

    public Guid? MarkedByAdminId { get; private set; }

    // Navegação
    public ExtraChargeEntity? ExtraCharge { get; private set; }
    public PlayerEntity?      Player      { get; private set; }
    public GroupEntity?       Group       { get; private set; }

    // ── Métodos de domínio ────────────────────────────────────────────────────

    public void MarkAsPaid(Guid? adminId, string? proofBase64, string? proofFileName, string? proofMimeType)
    {
        Status          = PaymentStatus.Paid;
        PaidAt          = DateTime.UtcNow;
        MarkedByAdminId = adminId;
        SetProof(proofBase64, proofFileName, proofMimeType);
    }

    public void MarkAsPending()
    {
        Status = PaymentStatus.Pending;
        PaidAt = null;
    }

    public void ApplyDiscount(decimal discount, string? reason, Guid adminId)
    {
        if (discount < 0) throw new InvalidOperationException("Desconto não pode ser negativo.");

        Discount        += discount;
        if (reason is not null) DiscountReason = reason;
        MarkedByAdminId = adminId;

        // Desconto cobre o total → marca como pago automaticamente
        if (Discount >= Amount)
        {
            Status = PaymentStatus.Paid;
            PaidAt = DateTime.UtcNow;
        }
    }

    public void SetProof(string? base64, string? fileName, string? mimeType)
    {
        ProofBase64   = base64;
        ProofFileName = fileName;
        ProofMimeType = mimeType;
    }
}
