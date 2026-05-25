using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Transactions;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/groups/{groupId:guid}/transactions")]
[Authorize]
public sealed class FinancialTransactionsController : GroupAuthorizedController
{
    private readonly IFinancialTransactionService _service;
    private readonly AppDbContext                 _db;

    public FinancialTransactionsController(IFinancialTransactionService service, AppDbContext db)
    {
        _service = service;
        _db      = db;
    }

    // ── Consultas (apenas financeiro) ────────────────────────────────────────

    /// <summary>Retorna transações de um mês específico.</summary>
    [HttpGet]
    public async Task<IActionResult> GetByMonth(
        Guid groupId, [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _service.GetByMonthAsync(groupId, year, month, ct);
        return ToResponse(result);
    }

    /// <summary>Retorna o resumo mês a mês (para a aba Geral).</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetMonthlySummaries(Guid groupId, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _service.GetMonthlySummariesAsync(groupId, ct);
        return ToResponse(result);
    }

    /// <summary>Retorna os totais de pendências em aberto.</summary>
    [HttpGet("pending-totals")]
    public async Task<IActionResult> GetPendingTotals(Guid groupId, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _service.GetPendingTotalsAsync(groupId, ct);
        return ToResponse(result);
    }

    // ── Escrita (apenas financeiro) ──────────────────────────────────────────

    /// <summary>Cria um lançamento manual (entrada ou saída).</summary>
    [HttpPost]
    public async Task<IActionResult> CreateTransaction(
        Guid groupId, [FromBody] CreateTransactionDto dto, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _service.CreateManualAsync(groupId, dto, userId.Value, ct);
        return ToResponse(result);
    }

    /// <summary>Exclui um lançamento manual. Lançamentos automáticos não podem ser excluídos.</summary>
    [HttpDelete("{transactionId:guid}")]
    public async Task<IActionResult> DeleteTransaction(
        Guid groupId, Guid transactionId, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _service.DeleteManualAsync(groupId, transactionId, ct);
        return ToResponse(result);
    }

    /// <summary>
    /// Sincroniza retroativamente pagamentos marcados como Pago que ainda não têm
    /// lançamento no caixa. Idempotente — pode ser chamado múltiplas vezes.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncTransactions(Guid groupId, CancellationToken ct)
    {
        if (!await IsFinanceiroForGroupAsync(groupId, _db, ct))
            return Forbid();

        var result = await _service.SyncPaidPaymentsAsync(groupId, ct);
        return ToResponse(result);
    }

    /// <summary>
    /// Remove TODOS os lançamentos do caixa do grupo.
    /// Usar apenas para diagnóstico/teste — permite limpar e re-sincronizar do zero.
    /// </summary>
    [HttpDelete("all")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> ClearAllTransactions(Guid groupId, CancellationToken ct)
    {
        var result = await _service.ClearAllTransactionsAsync(groupId, ct);
        return ToResponse(result);
    }
}
