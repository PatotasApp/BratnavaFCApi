using BratnavaFC.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    [HttpGet("mine")]
    public async Task<IActionResult> GetMineAsync(
        [FromQuery] Guid?   groupId  = null,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 30,
        CancellationToken   ct       = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _notifications.GetMineAsync(userId.Value, groupId, page, pageSize, ct);
        return Ok(new
        {
            data     = result.Data!.Data,
            total    = result.Data.Total,
            page     = result.Data.Page,
            pageSize = result.Data.PageSize,
        });
    }

    [HttpGet("mine/unread-count")]
    public async Task<IActionResult> GetUnreadCountAsync(
        [FromQuery] Guid? groupId = null,
        CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _notifications.GetUnreadCountAsync(userId.Value, groupId, ct);
        return Ok(new { data = result.Data });
    }

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _notifications.MarkReadAsync(id, userId.Value, ct);
        if (!result.Success && result.Status == Domain.Common.ResultStatus.NotFound) return NotFound();
        return Ok(new { data = true });
    }

    [HttpPatch("mine/read-all")]
    public async Task<IActionResult> MarkAllReadAsync(
        [FromQuery] Guid? groupId = null,
        CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await _notifications.MarkAllReadAsync(userId.Value, groupId, ct);
        return Ok(new { data = true });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await _notifications.DeleteAsync(id, userId.Value, ct);
        return Ok(new { data = true });
    }
}
