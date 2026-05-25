using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Payments;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/payments")]
[Authorize]
public sealed class PaymentController : GroupAuthorizedController
{
    private readonly IPaymentService _payments;
    private readonly AppDbContext    _db;

    public PaymentController(IPaymentService payments, AppDbContext db)
    {
        _payments = payments;
        _db       = db;
    }

    // ── Grade mensal (apenas admin) ───────────────────────────────────────────

    /// <summary>
    /// Cria registros MonthlyPayment (Pending) para todos os mensalistas que
    /// ainda não têm registro para o mês informado (apenas admin).
    /// </summary>
    [HttpPost("monthly/{year:int}/{month:int}/initiate")]
    public async Task<IActionResult> InitiateMonthly(
        Guid groupId, int year, int month, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _payments.InitiateMonthlyAsync(groupId, year, month, ct);
        if (!result.Success) return ToResponse(result);

        var (created, skipped) = result.Data;
        return Ok(new { created, skipped });
    }

    /// <summary>Verifica se já existe pelo menos um registro para o mês informado (apenas admin).</summary>
    [HttpGet("monthly/{year:int}/{month:int}/is-initiated")]
    public async Task<IActionResult> IsMonthInitiated(
        Guid groupId, int year, int month, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _payments.IsMonthInitiatedAsync(groupId, year, month, ct);
        if (!result.Success) return ToResponse(result);

        return Ok(new { isInitiated = result.Data });
    }

    /// <summary>Retorna a grade mensal de pagamentos para todos os mensalistas da patota.</summary>
    [HttpGet("monthly/{year:int}")]
    public async Task<IActionResult> GetMonthlyGrid(
        Guid groupId, int year, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _payments.GetMonthlyGridAsync(groupId, year, ct);
        return ToResponse(result);
    }

    /// <summary>
    /// Admin ou o próprio jogador: cria ou atualiza o registro de pagamento de um mês.
    /// Jogador comum não pode enviar campo Discount.
    /// </summary>
    [HttpPut("monthly")]
    public async Task<IActionResult> UpsertMonthlyPayment(
        Guid groupId,
        [FromBody] UpsertMonthlyPaymentDto dto,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var isAdmin = await IsFinanceiroForGroupAsync(groupId, _db, ct);

        var result = await _payments.UpsertMonthlyPaymentAsync(groupId, dto, userId.Value, isAdmin, ct);
        return ToResponse(result);
    }

    // ── Cobranças extras ──────────────────────────────────────────────────────

    /// <summary>Lista todas as cobranças extras da patota (apenas admin).</summary>
    [HttpGet("extra-charges")]
    public async Task<IActionResult> GetExtraCharges(Guid groupId, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _payments.GetExtraChargesAsync(groupId, ct);
        return ToResponse(result);
    }

    /// <summary>Cria uma nova cobrança extra e atribui aos jogadores selecionados (apenas admin).</summary>
    [HttpPost("extra-charges")]
    public async Task<IActionResult> CreateExtraCharge(
        Guid groupId,
        [FromBody] CreateExtraChargeDto dto,
        CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _payments.CreateExtraChargeAsync(groupId, dto, userId.Value, ct);
        return ToResponse(result);
    }

    /// <summary>Cancela (soft-delete) uma cobrança extra (apenas admin).</summary>
    [HttpDelete("extra-charges/{chargeId:guid}")]
    public async Task<IActionResult> CancelExtraCharge(
        Guid groupId, Guid chargeId, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _payments.CancelExtraChargeAsync(groupId, chargeId, ct);
        return ToResponse(result);
    }

    /// <summary>Aplica desconto em massa a vários jogadores de uma cobrança extra (apenas admin).</summary>
    [HttpPost("extra-charges/{chargeId:guid}/bulk-discount")]
    public async Task<IActionResult> BulkDiscountExtraCharge(
        Guid groupId, Guid chargeId, [FromBody] BulkExtraChargeDiscountDto dto, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct)) return Forbid();

        var adminId = GetCurrentUserId();
        if (adminId is null) return Unauthorized();

        var result = await _payments.BulkDiscountExtraChargeAsync(groupId, chargeId, dto, adminId.Value, ct);
        return ToResponse(result);
    }

    /// <summary>Admin ou o próprio jogador: atualiza o status de pagamento de uma cobrança extra.</summary>
    [HttpPut("extra-charges/{chargeId:guid}/players/{playerId:guid}")]
    public async Task<IActionResult> UpsertExtraChargePayment(
        Guid groupId,
        Guid chargeId,
        Guid playerId,
        [FromBody] UpsertExtraChargePaymentDto dto,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var isAdmin = await IsFinanceiroForGroupAsync(groupId, _db, ct);

        var result = await _payments.UpsertExtraChargePaymentAsync(
            groupId, chargeId, playerId, dto, userId.Value, isAdmin, ct);
        return ToResponse(result);
    }

    // ── Endpoints do próprio usuário (view não-admin) ─────────────────────────

    /// <summary>Retorna a linha mensal do player vinculado ao usuário logado na patota.</summary>
    [HttpGet("monthly/{year:int}/me")]
    public async Task<IActionResult> GetMyMonthlyRow(Guid groupId, int year, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _payments.GetMyMonthlyRowAsync(groupId, userId.Value, year, ct);
        return ToResponse(result);
    }

    /// <summary>Retorna cobranças extras em que o jogador do usuário logado está incluído.</summary>
    [HttpGet("extra-charges/me")]
    public async Task<IActionResult> GetMyExtraCharges(Guid groupId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _payments.GetMyExtraChargesAsync(groupId, userId.Value, ct);
        return ToResponse(result);
    }

    // ── Resumo de pendências ──────────────────────────────────────────────────

    /// <summary>Resumo de pagamentos de um jogador específico (admin vê qualquer um; jogador vê o próprio).</summary>
    [HttpGet("summary/{playerId:guid}")]
    public async Task<IActionResult> GetSummary(
        Guid groupId, Guid playerId, CancellationToken ct)
    {
        var userId  = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var isAdmin = await IsFinanceiroForGroupAsync(groupId, _db, ct);
        var result  = await _payments.GetPaymentSummaryAsync(groupId, playerId, userId, isAdmin, ct);
        return ToResponse(result);
    }

    /// <summary>Resumo do próprio usuário autenticado na patota (busca o player vinculado ao user).</summary>
    [HttpGet("my")]
    public async Task<IActionResult> GetMySummary(Guid groupId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _payments.GetMySummaryAsync(groupId, userId.Value, ct);
        return ToResponse(result);
    }

    // ── Pagar pendências em lote ──────────────────────────────────────────────

    /// <summary>Retorna todos os itens pendentes do usuário logado na patota (mensalidades + cobranças extras).</summary>
    [HttpGet("my-pending-items")]
    public async Task<IActionResult> GetMyPendingItems(Guid groupId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _payments.GetMyPendingItemsAsync(groupId, userId.Value, ct);
        return ToResponse(result);
    }

    /// <summary>Marca os itens selecionados como pagos (jogador confirma seus próprios débitos).</summary>
    [HttpPost("pay-selected")]
    public async Task<IActionResult> PaySelected(
        Guid groupId, [FromBody] PaySelectedDto dto, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _payments.PaySelectedAsync(groupId, userId.Value, dto, ct);
        return ToResponse(result);
    }

    // ── Comprovantes ──────────────────────────────────────────────────────────

    [HttpGet("monthly/{year:int}/{month:int}/{playerId:guid}/proof")]
    public async Task<IActionResult> GetMonthlyProof(
        Guid groupId, int year, int month, Guid playerId, CancellationToken ct)
    {
        var userId  = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var isAdmin = await IsFinanceiroForGroupAsync(groupId, _db, ct);
        var result  = await _payments.GetMonthlyProofAsync(groupId, playerId, year, month, userId, isAdmin, ct);
        return ToResponse(result);
    }

    [HttpGet("extra-charges/{chargeId:guid}/{playerId:guid}/proof")]
    public async Task<IActionResult> GetExtraChargeProof(
        Guid groupId, Guid chargeId, Guid playerId, CancellationToken ct)
    {
        var userId  = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var isAdmin = await IsFinanceiroForGroupAsync(groupId, _db, ct);
        var result  = await _payments.GetExtraChargeProofAsync(groupId, chargeId, playerId, userId, isAdmin, ct);
        return ToResponse(result);
    }

    /// <summary>
    /// Remove todas as mensalidades e reseta todos os pagamentos de cobranças extras
    /// para Pendente. Também limpa o caixa. Usar apenas para diagnóstico/teste.
    /// </summary>
    [HttpDelete("all")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> ClearAllPayments(Guid groupId, CancellationToken ct)
    {
        var result = await _payments.ClearAllPaymentsAsync(groupId, ct);
        return ToResponse(result);
    }
}
