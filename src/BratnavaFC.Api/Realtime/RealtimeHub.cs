using System.Security.Claims;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Realtime;

[Authorize(Roles = "User,Admin,GodMode")]
public sealed class RealtimeHub : Hub
{
    private readonly AppDbContext _db;

    public RealtimeHub(AppDbContext db)
    {
        _db = db;
    }

    public async Task JoinGroup(Guid groupId)
    {
        if (!await CanAccessGroupAsync(groupId))
            throw new HubException("Acesso negado ao grupo.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupChannel(groupId));
    }

    public Task LeaveGroup(Guid groupId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupChannel(groupId));

    internal static string GroupChannel(Guid groupId) => $"group:{groupId}";

    private async Task<bool> CanAccessGroupAsync(Guid groupId)
    {
        // Identidade INTERNA, escrita pelo FirebaseIdentityMiddleware. Ler "sub" aqui traria o
        // UID do Firebase, que não é GUID para usuários criados via social login — o
        // TryParse falharia e todo não-admin perderia acesso ao grupo.
        var userIdText = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
            return false;

        var roles = Context.User?.FindAll("role").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (roles.Contains("Admin") || roles.Contains("GodMode"))
            return true;

        return await _db.Players
            .AsNoTracking()
            .AnyAsync(p => p.GroupId == groupId && p.UserId == userId && !p.IsGuest);
    }
}
