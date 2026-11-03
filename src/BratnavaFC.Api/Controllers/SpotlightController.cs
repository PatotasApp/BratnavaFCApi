using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Spotlight do telão (site). Tudo aqui é só dos admins da patota (e de Admin/GodMode da
/// plataforma): quem não é admin recebe 403, inclusive no relatório.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class SpotlightController : GroupAuthorizedController
{
    private readonly ISpotlightService _service;
    private readonly AppDbContext _db;

    public SpotlightController(ISpotlightService service, AppDbContext db)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    [HttpGet("group/{groupId:guid}")]
    [ProducesResponseType(typeof(SpotlightEventReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReport(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.GetReportAsync(groupId, ct));
    }

    [HttpGet("group/{groupId:guid}/settings")]
    [ProducesResponseType(typeof(SpotlightSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.GetSettingsAsync(groupId, ct));
    }

    [HttpPut("group/{groupId:guid}/settings")]
    [ProducesResponseType(typeof(SpotlightSettings), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSettings(Guid groupId, [FromBody] SpotlightSettings settings, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        if (settings is null) return BadRequest(new { error = "Configuração inválida." });
        return ToResponse(await _service.SaveSettingsAsync(groupId, settings, ct));
    }
}
