using BratnavaFC.Api.Billing;
using BratnavaFC.Application.Billing;
using BratnavaFC.Domain.Common;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Plano da patota no app. Só admin e financeiro da patota enxergam e pagam; para o
/// jogador comum estes endpoints respondem 403 e o app não mostra nada de cobrança.
///
/// Liberado com a patota vencida, claro: é por aqui que ela volta a funcionar.
/// </summary>
[ApiController]
[Route("api/billing")]
[Authorize(Roles = "User,Admin,GodMode")]
[AllowWhenGroupExpired]
public sealed class BillingController : GroupAuthorizedController
{
    private readonly IBillingService _billing;
    private readonly AppDbContext _db;

    public BillingController(IBillingService billing, AppDbContext db)
    {
        _billing = billing;
        _db = db;
    }

    // GET /api/billing/groups/{groupId}?platform=android|ios
    [HttpGet("groups/{groupId:guid}")]
    public async Task<IActionResult> GetStatus(Guid groupId, [FromQuery] string? platform, CancellationToken ct)
    {
        if (!await CanManageBillingAsync(groupId, ct)) return Forbid();

        var status = await _billing.GetStatusAsync(groupId, GetCurrentUserId(), platform, ct);
        return status == null
            ? base.ToResponse(Result<GroupBillingStatusDto>.Fail("Patota não encontrada.", ResultStatus.NotFound))
            : base.ToResponse(Result<GroupBillingStatusDto>.Ok(status));
    }

    // POST /api/billing/groups/{groupId}/purchases?platform=android|ios
    [HttpPost("groups/{groupId:guid}/purchases")]
    public async Task<IActionResult> SubmitPurchase(Guid groupId, [FromBody] SubmitPurchaseDto dto, [FromQuery] string? platform, CancellationToken ct)
    {
        if (dto == null) return BadRequest();
        if (!await CanManageBillingAsync(groupId, ct)) return Forbid();

        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        return base.ToResponse(await _billing.SubmitPurchaseAsync(groupId, userId.Value, dto, platform, ct));
    }

    // POST /api/billing/restore
    [HttpPost("restore")]
    public async Task<IActionResult> Restore([FromBody] RestorePurchasesDto dto, CancellationToken ct)
    {
        if (dto == null) return BadRequest();
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        return base.ToResponse(Result<RestoreResultDto>.Ok(await _billing.RestoreAsync(userId.Value, dto, ct)));
    }

    // GET /api/billing/me/subscriptions
    [HttpGet("me/subscriptions")]
    public async Task<IActionResult> MySubscriptions(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();
        return base.ToResponse(Result<IReadOnlyList<MySubscriptionDto>>.Ok(await _billing.GetMySubscriptionsAsync(userId.Value, ct)));
    }

    private async Task<bool> CanManageBillingAsync(Guid groupId, CancellationToken ct) =>
        await IsAuthorizedForGroupAsync(groupId, _db, ct) || await IsFinanceiroForGroupAsync(groupId, _db, ct);
}
