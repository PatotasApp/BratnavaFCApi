using System.Security.Claims;
using BratnavaFC.Application.Billing;
using BratnavaFC.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Administração da cobrança, só GodMode (área /app/admin/godmode do site). O app não
/// conhece nada disto.
/// </summary>
[ApiController]
[Route("api/admin/billing")]
[Authorize(Roles = "GodMode")]
public sealed class GodModeBillingController : BaseApiController
{
    private readonly IBillingAdminService _admin;
    private readonly IBillingNotificationService _notifications;

    public GodModeBillingController(IBillingAdminService admin, IBillingNotificationService notifications)
    {
        _admin = admin;
        _notifications = notifications;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct) =>
        ToResponse(Result<BillingDashboardDto>.Ok(await _admin.GetDashboardAsync(ct)));

    // GET /api/admin/billing/groups?status=trial|active|grace|expired|lifetime|expiring&search=
    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] string? status, [FromQuery] string? search, CancellationToken ct) =>
        ToResponse(Result<IReadOnlyList<BillingGroupListItemDto>>.Ok(await _admin.ListGroupsAsync(status, search, ct)));

    [HttpGet("groups/{groupId:guid}")]
    public async Task<IActionResult> Group(Guid groupId, CancellationToken ct) =>
        await _admin.GetGroupAsync(groupId, ct) is { } detail
            ? ToResponse(Result<BillingGroupDetailDto>.Ok(detail))
            : ToResponse(Result<BillingGroupDetailDto>.Fail("Patota não encontrada.", ResultStatus.NotFound));

    [HttpPost("groups/{groupId:guid}/extend")]
    public async Task<IActionResult> Extend(Guid groupId, [FromBody] ExtendBillingDto dto, CancellationToken ct) =>
        ToResponse(await _admin.ExtendAsync(groupId, ActorId(), dto, ct));

    [HttpPost("groups/{groupId:guid}/lifetime")]
    public async Task<IActionResult> Lifetime(Guid groupId, [FromBody] SetLifetimeDto dto, CancellationToken ct) =>
        ToResponse(await _admin.SetLifetimeAsync(groupId, ActorId(), dto, ct));

    [HttpPost("purchases/reprocess")]
    public async Task<IActionResult> Reprocess([FromBody] ReprocessPurchaseDto dto, CancellationToken ct) =>
        ToResponse(await _admin.ReprocessAsync(ActorId(), dto, ct));

    [HttpPost("subscriptions/{subscriptionId:guid}/resync")]
    public async Task<IActionResult> Resync(Guid subscriptionId, CancellationToken ct) =>
        ToResponse(await _admin.ResyncSubscriptionAsync(subscriptionId, ActorId(), ct));

    /// <summary>Só Development com a compra simulada ligada.</summary>
    [HttpPost("subscriptions/{subscriptionId:guid}/simulate")]
    public async Task<IActionResult> Simulate(Guid subscriptionId, [FromBody] SimulateSubscriptionEventDto dto, CancellationToken ct) =>
        ToResponse(await _admin.SimulateAsync(subscriptionId, ActorId(), dto, ct));

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) =>
        ToResponse(Result<BillingSettingsDto>.Ok(await _admin.GetSettingsAsync(ct)));

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateBillingSettingsDto dto, CancellationToken ct) =>
        ToResponse(await _admin.UpdateSettingsAsync(ActorId(), dto, ct));

    [HttpPut("products")]
    public async Task<IActionResult> UpsertProduct([FromBody] UpsertBillingProductDto dto, CancellationToken ct) =>
        ToResponse(await _admin.UpsertProductAsync(ActorId(), dto, ct));

    [HttpGet("problems")]
    public async Task<IActionResult> Problems(CancellationToken ct) =>
        ToResponse(Result<BillingProblemsDto>.Ok(await _admin.GetProblemsAsync(ct)));

    [HttpPost("events/{eventId:guid}/retry")]
    public async Task<IActionResult> RetryEvent(Guid eventId, CancellationToken ct)
    {
        var outcome = await _notifications.RetryAsync(eventId, ct);
        return outcome == StoreNotificationOutcome.Ok
            ? ToResponse(Result.Ok("Aviso reprocessado."))
            : ToResponse(Result.Fail("O aviso continua com erro. Veja o detalhe no painel."));
    }

    private Guid ActorId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException();
}
