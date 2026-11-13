using System.Security.Claims;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Moderation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Denunciar conteúdo e bloquear usuários (App Store, regra 1.2). Qualquer usuário
/// logado denuncia e bloqueia; só os administradores da plataforma listam e
/// resolvem as denúncias.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Roles = "User,Admin,GodMode")]
[BratnavaFC.Api.Billing.AllowWhenGroupExpired]
public sealed class ModerationController : BaseApiController
{
    private readonly IModerationService _moderation;

    public ModerationController(IModerationService moderation) => _moderation = moderation;

    // POST /api/reports
    [HttpPost("reports")]
    public async Task<IActionResult> Report([FromBody] CreateContentReportDto dto, CancellationToken ct) =>
        ToResponse(await _moderation.ReportAsync(CurrentUserId(), dto, ct));

    // GET /api/reports?pendingOnly=true
    [HttpGet("reports")]
    [Authorize(Roles = "Admin,GodMode")]
    public async Task<IActionResult> GetReports([FromQuery] bool pendingOnly = true, CancellationToken ct = default) =>
        ToResponse(await _moderation.GetReportsAsync(pendingOnly, ct));

    // PUT /api/reports/{id}/resolve
    [HttpPut("reports/{reportId:guid}/resolve")]
    [Authorize(Roles = "Admin,GodMode")]
    public async Task<IActionResult> Resolve(Guid reportId, [FromBody] ResolveContentReportDto? dto, CancellationToken ct) =>
        ToResponse(await _moderation.ResolveReportAsync(reportId, CurrentUserId(), dto?.Note, ct));

    // GET /api/users/me/blocks
    [HttpGet("users/me/blocks")]
    public async Task<IActionResult> GetBlocked(CancellationToken ct) =>
        ToResponse(await _moderation.GetBlockedAsync(CurrentUserId(), ct));

    // POST /api/users/{id}/block
    [HttpPost("users/{userId:guid}/block")]
    public async Task<IActionResult> Block(Guid userId, CancellationToken ct) =>
        ToResponse(await _moderation.BlockAsync(CurrentUserId(), userId, ct));

    // DELETE /api/users/{id}/block
    [HttpDelete("users/{userId:guid}/block")]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken ct) =>
        ToResponse(await _moderation.UnblockAsync(CurrentUserId(), userId, ct));

    /// <summary>Identidade interna, injetada no NameIdentifier pelo FirebaseIdentityMiddleware.</summary>
    private Guid CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedAccessException();
    }
}
