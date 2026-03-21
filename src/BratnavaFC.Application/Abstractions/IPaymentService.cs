using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Payments;

namespace BratnavaFC.Application.Abstractions;

public interface IPaymentService
{
    // ── Grade mensal (admin) ──────────────────────────────────────────────────
    Task<Result<MonthlyGridDto>> GetMonthlyGridAsync(Guid groupId, int year, CancellationToken ct = default);

    Task<Result> UpsertMonthlyPaymentAsync(
        Guid groupId,
        UpsertMonthlyPaymentDto dto,
        Guid actingUserId,
        bool isAdmin,
        CancellationToken ct = default);

    // ── Cobranças extras ──────────────────────────────────────────────────────
    Task<Result<IReadOnlyList<ExtraChargeDto>>> GetExtraChargesAsync(Guid groupId, CancellationToken ct = default);

    Task<Result<ExtraChargeDto>> CreateExtraChargeAsync(
        Guid groupId,
        CreateExtraChargeDto dto,
        Guid adminId,
        CancellationToken ct = default);

    Task<Result> CancelExtraChargeAsync(Guid groupId, Guid chargeId, CancellationToken ct = default);

    /// <summary>Aplica desconto fixo a vários jogadores de uma cobrança extra de uma só vez.</summary>
    Task<Result> BulkDiscountExtraChargeAsync(
        Guid groupId,
        Guid chargeId,
        BulkExtraChargeDiscountDto dto,
        Guid adminId,
        CancellationToken ct = default);

    Task<Result> UpsertExtraChargePaymentAsync(
        Guid groupId,
        Guid chargeId,
        Guid playerId,
        UpsertExtraChargePaymentDto dto,
        Guid actingUserId,
        bool isAdmin,
        CancellationToken ct = default);

    // ── Lançamento de mensalidade em lote (modo Monthly) ─────────────────────
    /// <summary>
    /// Cria registros MonthlyPayment (Pending) para todos os mensalistas do grupo
    /// que ainda não têm registro para o ano/mês informado.
    /// Retorna quantos foram criados e quantos já existiam.
    /// </summary>
    Task<Result<(int Created, int Skipped)>> InitiateMonthlyAsync(Guid groupId, int year, int month, CancellationToken ct = default);

    /// <summary>Verifica se já existe pelo menos um registro para o mês informado.</summary>
    Task<Result<bool>> IsMonthInitiatedAsync(Guid groupId, int year, int month, CancellationToken ct = default);

    // ── Visão do próprio usuário (tela de pagamentos não-admin) ───────────────
    /// <summary>Retorna a linha mensal do jogador vinculado ao userId, ou null se não tiver player na patota.</summary>
    Task<Result<PlayerMonthlyRowDto?>> GetMyMonthlyRowAsync(Guid groupId, Guid userId, int year, CancellationToken ct = default);

    /// <summary>Retorna cobranças extras em que o jogador vinculado ao userId está incluído.</summary>
    Task<Result<IReadOnlyList<ExtraChargeDto>>> GetMyExtraChargesAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    // ── Resumo de pendências (usado no Dashboard e tela de usuários) ──────────
    Task<Result<PaymentSummaryDto>> GetPaymentSummaryAsync(Guid groupId, Guid playerId, CancellationToken ct = default);

    // ── Comprovantes ──────────────────────────────────────────────────────────
    Task<Result<ProofResponseDto>> GetMonthlyProofAsync(Guid groupId, Guid playerId, int year, int month, CancellationToken ct = default);
    Task<Result<ProofResponseDto>> GetExtraChargeProofAsync(Guid groupId, Guid chargeId, Guid playerId, CancellationToken ct = default);
}
