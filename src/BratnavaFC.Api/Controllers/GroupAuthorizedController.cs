using BratnavaFC.Domain.Common;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Base controller that adds group-level admin authorization on top of global role checks.
/// </summary>
public abstract class GroupAuthorizedController : BaseApiController
{
    protected Guid? GetCurrentUserId()
    {
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    protected bool HasGlobalAdminRole()
        => User.IsInRole("Admin") || User.IsInRole("GodMode");

    /// <summary>
    /// Returns true if the caller is registered as an admin of the specific group.
    /// Global roles (Admin, GodMode) are intentionally excluded — all users must be
    /// explicitly added as group admins to act as one.
    /// </summary>
    protected async Task<bool> IsAuthorizedForGroupAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return false;

        return await db.GroupAdmins
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    /// <summary>
    /// Returns true if the caller has a global Admin/GodMode role,
    /// is a group admin, OR is a regular member (has a player in the group).
    /// Use for endpoints accessible to all group participants.
    /// </summary>
    protected async Task<bool> IsGroupMemberAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        if (HasGlobalAdminRole()) return true;

        var userId = GetCurrentUserId();
        if (userId == null) return false;

        // group admin check
        if (await db.GroupAdmins.AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct))
            return true;

        // regular member (player linked to a user account in this group)
        return await db.Players
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    /// <summary>
    /// Returns true if the caller is GodMode OR is registered as a financeiro of the specific group.
    /// Admins are NOT automatically financeiros — they must be explicitly added.
    /// </summary>
    protected async Task<bool> IsFinanceiroForGroupAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return false;

        return await db.GroupFinanceiros
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    // ── Result pattern helpers ──────────────────────────────────────────────

    /// <summary>
    /// Converte Result&lt;T&gt; em IActionResult:
    /// sucesso → 200 OK com envelope { success, data };
    /// falha   → 400 Bad Request com { error }.
    /// </summary>
    protected IActionResult ToResponse<T>(Result<T> result) =>
        result.Success
            ? Ok(result)
            : BadRequest(new { error = result.Error });

    /// <summary>
    /// Converte Result (void) em IActionResult:
    /// sucesso → 204 No Content;
    /// falha   → 400 Bad Request com { error }.
    /// </summary>
    protected IActionResult ToResponse(Result result) =>
        result.Success
            ? NoContent()
            : BadRequest(new { error = result.Error });
}
