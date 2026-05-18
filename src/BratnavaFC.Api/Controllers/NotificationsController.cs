using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public sealed class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    // ── GET /api/Notifications/mine ──────────────────────────────────────────

    [HttpGet("mine")]
    public async Task<IActionResult> GetMineAsync(
        [FromQuery] Guid?   groupId  = null,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 30,
        CancellationToken   ct       = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(page, 1);

        var query = _db.UserNotifications
            .Where(n => n.UserId == userId.Value);

        if (groupId.HasValue)
            query = query.Where(n => n.GroupId == groupId.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(n => n.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                id        = n.Id,
                title     = n.Title,
                body      = n.Body,
                type      = n.Type,
                dataJson  = n.DataJson,
                isRead    = n.IsRead,
                createdAt = n.CreateDate,
                groupId   = n.GroupId,
            })
            .ToListAsync(ct);

        return Ok(new { data = items, total, page, pageSize });
    }

    // ── GET /api/Notifications/mine/unread-count ─────────────────────────────

    [HttpGet("mine/unread-count")]
    public async Task<IActionResult> GetUnreadCountAsync(
        [FromQuery] Guid? groupId = null,
        CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var query = _db.UserNotifications
            .Where(n => n.UserId == userId.Value && !n.IsRead);

        if (groupId.HasValue)
            query = query.Where(n => n.GroupId == groupId.Value);

        var count = await query.CountAsync(ct);
        return Ok(new { data = count });
    }

    // ── PUT /api/Notifications/{id}/read ─────────────────────────────────────

    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var n = await _db.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, ct);

        if (n is null) return NotFound();

        n.MarkAsRead();
        await _db.SaveChangesAsync(ct);
        return Ok(new { data = true });
    }

    // ── PUT /api/Notifications/mine/read-all ─────────────────────────────────

    [HttpPatch("mine/read-all")]
    public async Task<IActionResult> MarkAllReadAsync(
        [FromQuery] Guid? groupId = null,
        CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var query = _db.UserNotifications
            .Where(n => n.UserId == userId.Value && !n.IsRead);

        if (groupId.HasValue)
            query = query.Where(n => n.GroupId == groupId.Value);

        var unread = await query.ToListAsync(ct);
        foreach (var n in unread) n.MarkAsRead();
        await _db.SaveChangesAsync(ct);

        return Ok(new { data = true });
    }

    // ── DELETE /api/Notifications/{id} ───────────────────────────────────────

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var n = await _db.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, ct);

        if (n is null) return Ok(new { data = true }); // idempotente

        _db.UserNotifications.Remove(n);
        await _db.SaveChangesAsync(ct);
        return Ok(new { data = true });
    }
}
