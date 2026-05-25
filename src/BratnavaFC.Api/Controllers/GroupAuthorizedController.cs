using BratnavaFC.Domain.Common;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Base controller that adds group-level authorization on top of the JWT role checks.
///
/// GodMode design principle
/// ────────────────────────
/// A user with the GodMode role is treated as a virtual admin and member of every
/// group. No database records are created for this — the short-circuit happens
/// exclusively inside <see cref="IsGodMode"/>. Every authorization helper calls
/// IsGodMode() first, so controllers never need to mention the GodMode role
/// explicitly.
/// </summary>
public abstract class GroupAuthorizedController : BaseApiController
{
    // ── Identity helpers ─────────────────────────────────────────────────────

    protected Guid? GetCurrentUserId()
    {
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Single source of truth for the GodMode check.
    /// GodMode is a virtual admin/member/financeiro of every group — no DB record needed.
    /// All authorization helpers below call this first; controllers must not.
    /// </summary>
    protected bool IsGodMode() => User.IsInRole("GodMode");

    /// <summary>
    /// Returns true when the caller has the platform-level Admin or GodMode role.
    /// Use only when you want BOTH roles to bypass a check (e.g. IsGroupAdminAsync).
    /// </summary>
    private bool HasPlatformAdminRole() => User.IsInRole("Admin") || IsGodMode();

    // ── Group authorization helpers ──────────────────────────────────────────

    /// <summary>
    /// Returns true if the caller is a GodMode user
    /// OR is explicitly registered as an admin of the specific group.
    ///
    /// Use for operations that only the group's own admins should perform
    /// (platform-level Admin role does NOT grant group ownership here).
    /// </summary>
    protected async Task<bool> IsAuthorizedForGroupAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        if (IsGodMode()) return true;

        var userId = GetCurrentUserId();
        if (userId == null) return false;

        return await db.GroupAdmins
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    /// <summary>
    /// Returns true if the caller is GodMode, has the platform Admin role,
    /// OR is registered as an admin of the specific group.
    ///
    /// Use for endpoints that both group admins and platform admins should reach.
    /// </summary>
    protected async Task<bool> IsGroupAdminAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        if (HasPlatformAdminRole()) return true;

        var userId = GetCurrentUserId();
        if (userId == null) return false;

        return await db.GroupAdmins
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    /// <summary>
    /// Returns true if the caller is GodMode, has the platform Admin role,
    /// is a group admin, OR is a regular member (has a player record in the group).
    ///
    /// Use for endpoints accessible to all group participants.
    /// </summary>
    protected async Task<bool> IsGroupMemberAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        if (HasPlatformAdminRole()) return true;

        var userId = GetCurrentUserId();
        if (userId == null) return false;

        if (await db.GroupAdmins.AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct))
            return true;

        return await db.Players
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    /// <summary>
    /// Returns true if the caller is GodMode
    /// OR is explicitly registered as a financeiro of the specific group.
    ///
    /// Platform Admin does NOT automatically become financeiro — must be explicitly added.
    /// </summary>
    protected async Task<bool> IsFinanceiroForGroupAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        if (IsGodMode()) return true;

        var userId = GetCurrentUserId();
        if (userId == null) return false;

        return await db.GroupFinanceiros
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    // ── Result pattern helpers ───────────────────────────────────────────────

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
