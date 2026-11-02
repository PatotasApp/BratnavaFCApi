using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Retrospective;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

/// <summary>Retrospectiva individual da temporada (docs/retrospectiva/RETROSPECTIVA.md).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class RetrospectiveController : GroupAuthorizedController
{
    private readonly IPlayerRetrospectiveService _retrospective;
    private readonly AppDbContext _db;

    public RetrospectiveController(IPlayerRetrospectiveService retrospective, AppDbContext db)
    {
        _retrospective = retrospective;
        _db = db;
    }

    /// <summary>
    /// Só o admin da patota (e Admin/GodMode) vê a retrospectiva, de qualquer jogador (playerId;
    /// sem ele, a do próprio admin, ou o primeiro jogador). A resposta traz a lista para escolher.
    /// Período: year (padrão: o atual, ou o último com jogos) ou from/to (datas inclusivas, Brasília).
    /// </summary>
    [HttpGet("group/{groupId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlayerRetrospectiveDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid groupId,
        [FromQuery] Guid? playerId,
        [FromQuery] int? year,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();

        var targetId = playerId;
        if (targetId is null)
        {
            var userId = GetCurrentUserId();
            targetId = userId is null ? null : await _db.Players.AsNoTracking()
                .Where(p => p.GroupId == groupId && p.UserId == userId)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(ct);
        }

        // Admin que não joga na patota: começa pelo primeiro jogador (escolhe na lista).
        targetId ??= await _db.Players.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .OrderBy(p => p.Name)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (targetId is null)
            return ToResponse(Result<PlayerRetrospectiveDto>.Fail("A patota ainda não tem jogadores.", ResultStatus.NotFound));

        var result = await _retrospective.GetAsync(groupId, targetId.Value, year, from, to, withPlayerList: true, ct);
        return ToResponse(result);
    }
}
