using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BratnavaFC.Api.Billing;
using BratnavaFC.Application.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Avisos das lojas. Anônimos de propósito (a loja não tem login), mas nada aqui é
/// confiado sem conferência:
/// <list type="bullet">
/// <item>Apple: o corpo é um JWS assinado pela Apple, conferido até a raiz da Apple;</item>
/// <item>Google: a URL do push do Pub/Sub leva <c>?token=</c> com o segredo
/// <c>Billing:Google:PubSubToken</c>, e o estado da assinatura é sempre relido na API do Google.</item>
/// </list>
/// URLs para cadastrar nos consoles:
/// <c>https://&lt;api&gt;/api/billing/webhooks/apple</c> e
/// <c>https://&lt;api&gt;/api/billing/webhooks/google?token=&lt;segredo&gt;</c>.
/// </summary>
[ApiController]
[Route("api/billing/webhooks")]
[AllowAnonymous]
[AllowWhenGroupExpired]
public sealed class BillingWebhooksController : ControllerBase
{
    private readonly IBillingNotificationService _notifications;
    private readonly BillingOptions _options;

    public BillingWebhooksController(IBillingNotificationService notifications, IOptions<BillingOptions> options)
    {
        _notifications = notifications;
        _options = options.Value;
    }

    public sealed record AppleNotificationBody(string? SignedPayload);

    // POST /api/billing/webhooks/apple
    [HttpPost("apple")]
    public async Task<IActionResult> Apple([FromBody] AppleNotificationBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.SignedPayload)) return BadRequest();
        return ToStatus(await _notifications.HandleAppleAsync(body.SignedPayload, ct));
    }

    // POST /api/billing/webhooks/google?token=...
    [HttpPost("google")]
    public async Task<IActionResult> Google([FromQuery] string? token, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (!TokenMatches(token)) return Unauthorized();
        return ToStatus(await _notifications.HandleGoogleAsync(body.GetRawText(), ct));
    }

    private bool TokenMatches(string? token)
    {
        var expected = _options.Google.PubSubToken;
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(token)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token));
    }

    private IActionResult ToStatus(StoreNotificationOutcome outcome) => outcome switch
    {
        StoreNotificationOutcome.Ok => Ok(),
        StoreNotificationOutcome.Rejected => BadRequest(),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };
}
