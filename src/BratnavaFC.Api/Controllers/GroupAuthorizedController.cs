using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Base controller that adds group-level admin authorization on top of global role checks.
/// </summary>
public abstract class GroupAuthorizedController : ControllerBase
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
    /// Returns true if the caller has a global Admin/GodMode role
    /// OR is registered as an admin of the specific group.
    /// </summary>
    protected async Task<bool> IsAuthorizedForGroupAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        if (HasGlobalAdminRole()) return true;

        var userId = GetCurrentUserId();
        if (userId == null) return false;

        return await db.GroupAdmins
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }

    /// <summary>
    /// Returns true if the caller is GodMode OR is registered as a financeiro of the specific group.
    /// Admins are NOT automatically financeiros — they must be explicitly added.
    /// </summary>
    protected async Task<bool> IsFinanceiroForGroupAsync(Guid groupId, AppDbContext db, CancellationToken ct)
    {
        if (User.IsInRole("GodMode")) return true;

        var userId = GetCurrentUserId();
        if (userId == null) return false;

        return await db.GroupFinanceiros
            .AnyAsync(x => x.GroupId == groupId && x.UserId == userId.Value, ct);
    }
}
