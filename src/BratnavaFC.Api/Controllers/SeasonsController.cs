using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Seasons;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>Temporadas da patota. Docs em docs/temporadas/TEMPORADAS.md.</summary>
[ApiController]
[Route("api/seasons/group/{groupId:guid}")]
[Authorize(Roles = "User,Admin,GodMode")]
public class SeasonsController : GroupAuthorizedController
{
    private readonly ISeasonService _service;
    private readonly AppDbContext _db;

    public SeasonsController(ISeasonService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    /// <summary>Temporadas da patota, com a atual e o número de jogos de cada uma.</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.ListAsync(groupId, ct));
    }

    /// <summary>Resumo do fechamento com esse corte (padrão: agora), sem gravar nada.</summary>
    [HttpGet("close-preview")]
    public async Task<IActionResult> ClosePreview(Guid groupId, [FromQuery] DateTimeOffset? cutAt, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.PreviewCloseAsync(groupId, cutAt, ct));
    }

    /// <summary>Retrospectiva da temporada encerrada: títulos e classificação real.</summary>
    [HttpGet("{seasonId:guid}/summary")]
    public async Task<IActionResult> Summary(Guid groupId, Guid seasonId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.GetSummaryAsync(groupId, seasonId, ct));
    }

    [HttpPost("close")]
    public Task<IActionResult> Close(Guid groupId, [FromBody] CloseSeasonDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.CloseAsync(groupId, GetCurrentUserId(), dto, ct));

    [HttpPut("{seasonId:guid}")]
    public Task<IActionResult> Update(Guid groupId, Guid seasonId, [FromBody] UpdateSeasonDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.UpdateAsync(groupId, seasonId, dto, ct));

    [HttpPut("{seasonId:guid}/start")]
    public Task<IActionResult> MoveStart(Guid groupId, Guid seasonId, [FromBody] MoveSeasonStartDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.MoveStartAsync(groupId, seasonId, dto, ct));

    [HttpPost("undo-close")]
    public Task<IActionResult> UndoClose(Guid groupId, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.UndoCloseAsync(groupId, ct));

    private async Task<IActionResult> AsAdmin(Guid groupId, CancellationToken ct, Func<Task<Result>> action)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await action());
    }
}
