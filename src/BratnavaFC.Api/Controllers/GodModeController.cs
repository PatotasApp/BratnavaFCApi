using BratnavaFC.Application.Abstractions;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Endpoints exclusivos do papel GodMode — controle total do sistema.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "GodMode")]
public sealed class GodModeController : BaseApiController
{
    private readonly IPushService _push;
    private readonly AppDbContext _db;

    public GodModeController(IPushService push, AppDbContext db)
    {
        _push = push;
        _db   = db;
    }

    public sealed record NotifyRequest(string Title, string Body);

    /// <summary>Envia notificação para todos os jogadores de um grupo.</summary>
    [HttpPost("groups/{groupId:guid}/notify")]
    public async Task<IActionResult> NotifyGroupAsync(
        Guid groupId, [FromBody] NotifyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { error = "Title e body são obrigatórios." });

        await _push.SendToGroupAsync(
            groupId, req.Title, req.Body,
            data: new Dictionary<string, string> { ["type"] = "admin_notification" },
            cancellationToken: ct);

        return Ok(new { success = true, message = "Notificação enviada." });
    }

    /// <summary>Envia notificação para um usuário específico.</summary>
    [HttpPost("users/{userId:guid}/notify")]
    public async Task<IActionResult> NotifyUserAsync(
        Guid userId, [FromBody] NotifyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { error = "Title e body são obrigatórios." });

        await _push.SendToUserAsync(
            userId, req.Title, req.Body,
            data: new Dictionary<string, string> { ["type"] = "admin_notification" },
            cancellationToken: ct);

        return Ok(new { success = true, message = "Notificação enviada." });
    }

    /// <summary>Envia notificação para todos os usuários ativos do sistema.</summary>
    [HttpPost("users/notify-all")]
    public async Task<IActionResult> NotifyAllUsersAsync(
        [FromBody] NotifyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { error = "Title e body são obrigatórios." });

        var userIds = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == Domain.Enums.Status.Active)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (userIds.Count == 0)
            return Ok(new { success = true, message = "Nenhum usuário ativo encontrado.", sent = 0 });

        await _push.SendToUsersAsync(
            userIds, req.Title, req.Body,
            data: new Dictionary<string, string> { ["type"] = "admin_broadcast" },
            cancellationToken: ct);

        return Ok(new { success = true, message = $"Notificação enviada para {userIds.Count} usuário(s).", sent = userIds.Count });
    }
}
