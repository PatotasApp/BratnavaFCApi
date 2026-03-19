using BratnavaFC.Domain.Dtos.Payments;

namespace BratnavaFC.Application.Abstractions;

public interface IPaymentService
{
    // ── Grade mensal (admin) ──────────────────────────────────────────────────
    Task<MonthlyGridDto> GetMonthlyGridAsync(Guid groupId, int year, CancellationToken ct = default);

    Task UpsertMonthlyPaymentAsync(
        Guid groupId,
        UpsertMonthlyPaymentDto dto,
        Guid actingUserId,
        bool isAdmin,
        CancellationToken ct = default);

    // ── Cobranças extras ──────────────────────────────────────────────────────
    Task<IReadOnlyList<ExtraChargeDto>> GetExtraChargesAsync(Guid groupId, CancellationToken ct = default);

    Task<ExtraChargeDto> CreateExtraChargeAsync(
        Guid groupId,
        CreateExtraChargeDto dto,
        Guid adminId,
        CancellationToken ct = default);

    Task CancelExtraChargeAsync(Guid groupId, Guid chargeId, CancellationToken ct = default);

    /// <summary>Aplica desconto fixo a vários jogadores de uma cobrança extra de uma só vez.</summary>
    Task BulkDiscountExtraChargeAsync(
        Guid groupId,
        Guid chargeId,
        BulkExtraChargeDiscountDto dto,
        Guid adminId,
        CancellationToken ct = default);

    Task UpsertExtraChargePaymentAsync(
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
    Task<(int Created, int Skipped)> InitiateMonthlyAsync(Guid groupId, int year, int month, CancellationToken ct = default);

    /// <summary>Verifica se já existe pelo menos um registro para o mês informado.</summary>
    Task<bool> IsMonthInitiatedAsync(Guid groupId, int year, int month, CancellationToken ct = default);

    // ── Visão do próprio usuário (tela de pagamentos não-admin) ───────────────
    /// <summary>Retorna a linha mensal do jogador vinculado ao userId, ou null se não tiver player na patota.</summary>
    Task<PlayerMonthlyRowDto?> GetMyMonthlyRowAsync(Guid groupId, Guid userId, int year, CancellationToken ct = default);

    /// <summary>Retorna cobranças extras em que o jogador vinculado ao userId está incluído.</summary>
    Task<IReadOnlyList<ExtraChargeDto>> GetMyExtraChargesAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    // ── Resumo de pendências (usado no Dashboard e tela de usuários) ──────────
    Task<PaymentSummaryDto> GetPaymentSummaryAsync(Guid groupId, Guid playerId, CancellationToken ct = default);

    // ── Comprovantes ──────────────────────────────────────────────────────────
    Task<ProofResponseDto> GetMonthlyProofAsync(Guid groupId, Guid playerId, int year, int month, CancellationToken ct = default);
    Task<ProofResponseDto> GetExtraChargeProofAsync(Guid groupId, Guid chargeId, Guid playerId, CancellationToken ct = default);
}
