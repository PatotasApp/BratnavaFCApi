using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public class TeamColorController : GroupAuthorizedController
{
    private readonly ITeamColorService _service;
    private readonly AppDbContext _db;

    public TeamColorController(ITeamColorService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetAll(Guid groupId, [FromQuery] bool activeOnly, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var result = await _service.GetAllAsync(groupId, activeOnly, ct);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/{colorId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid colorId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var result = await _service.GetByIdAsync(groupId, colorId, ct);
        return ToResponse(result);
    }

    [HttpPost("group/{groupId:guid}")]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateTeamColorDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        dto.GroupId = groupId;
        var result = await _service.CreateAsync(dto, ct);
        return ToResponse(result);
    }

    [HttpPut("group/{groupId:guid}/{colorId:guid}")]
    public async Task<IActionResult> Update(Guid groupId, Guid colorId, [FromBody] UpdateTeamColorDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _service.UpdateAsync(groupId, colorId, dto, ct);
        return ToResponse(result);
    }

    [HttpPost("group/{groupId:guid}/{colorId:guid}/deactivate")]
    public async Task<IActionResult> Inactivate(Guid groupId, Guid colorId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _service.InactivateAsync(groupId, colorId, ct);
        return ToResponse(result);
    }

    [HttpPost("group/{groupId:guid}/{colorId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid groupId, Guid colorId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _service.ActivateAsync(groupId, colorId, ct);
        return ToResponse(result);
    }

    /// <summary>Exclui permanentemente uma cor do time. Apenas GodMode.</summary>
    [HttpDelete("group/{groupId:guid}/{colorId:guid}")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> Delete(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var result = await _service.DeleteAsync(groupId, colorId, ct);
        return ToResponse(result);
    }
}
