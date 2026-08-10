using BratnavaFC.Application.Abstractions;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class ConquistasController : GroupAuthorizedController
{
    private readonly IConquistaService _conquistas;
    private readonly IConquistaProjectionService _projection;
    private readonly AppDbContext _db;

    public ConquistasController(IConquistaService conquistas, IConquistaProjectionService projection, AppDbContext db)
    {
        _conquistas = conquistas;
        _projection = projection;
        _db = db;
    }

    /// <summary>Conquistas de todos os jogadores do grupo (qualquer membro pode ver).</summary>
    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetGroup(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var data = await _conquistas.GetGroupConquistasAsync(groupId, ct);
        return Ok(new { data });
    }

    [HttpPost("group/{groupId:guid}/rebuild")]
    public async Task<IActionResult> Rebuild(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        await _projection.RebuildGroupAsync(groupId, ct);
        return Ok(new { message = "Projeções reconstruídas com sucesso." });
    }
}
