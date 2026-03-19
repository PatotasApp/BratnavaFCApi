using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Payments;

public sealed class UpsertExtraChargePaymentDto
{
    public PaymentStatus Status         { get; set; }

    // Apenas admin pode enviar desconto
    public decimal?      Discount       { get; set; }
    public string?       DiscountReason { get; set; }

    // Comprovante (Base64) — opcional
    public string?       ProofBase64    { get; set; }
    public string?       ProofFileName  { get; set; }
    public string?       ProofMimeType  { get; set; }
}
