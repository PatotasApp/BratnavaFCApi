using System.Security.Claims;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/push")]
[Authorize]
public sealed class PushController : BaseApiController
{
    private readonly IPushService _pushService;

    public PushController(IPushService pushService)
    {
        _pushService = pushService;
    }

    /// <summary>
    /// Registra ou atualiza o token FCM do dispositivo do usuário autenticado.
    /// Idempotente: se o token já existir, apenas reassocia ao usuário e ativa.
    /// </summary>
    [HttpPost("register-token")]
    public async Task<IActionResult> RegisterTokenAsync(
        [FromBody] RegisterPushTokenDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await _pushService.RegisterTokenAsync(
            userId, request.Token, request.Platform, cancellationToken);

        return ToResponse(result);
    }

    /// <summary>Desativa as notificações push deste dispositivo no logout.</summary>
    [HttpDelete("register-token")]
    public async Task<IActionResult> UnregisterTokenAsync(
        [FromBody] UnregisterPushTokenDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await _pushService.UnregisterTokenAsync(
            userId, request.Token, cancellationToken);

        return ToResponse(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
