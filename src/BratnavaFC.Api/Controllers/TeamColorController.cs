using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TeamColorController : BaseApiController
{
    private readonly ITeamColorService _service;

    public TeamColorController(ITeamColorService service)
    {
        _service = service;
    }

    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetAll(Guid groupId, [FromQuery] bool activeOnly, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(groupId, activeOnly, ct);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/{colorId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(groupId, colorId, ct);
        return ToResponse(result);
    }

    [HttpPost("group/{groupId:guid}")]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateTeamColorDto dto, CancellationToken ct)
    {
        dto.GroupId = groupId;
        var result = await _service.CreateAsync(dto, ct);
        return ToResponse(result);
    }

    [HttpPut("group/{groupId:guid}/{colorId:guid}")]
    public async Task<IActionResult> Update(Guid groupId, Guid colorId, [FromBody] UpdateTeamColorDto dto, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(groupId, colorId, dto, ct);
        return ToResponse(result);
    }

    [HttpPost("group/{groupId:guid}/{colorId:guid}/deactivate")]
    public async Task<IActionResult> Inactivate(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var result = await _service.InactivateAsync(groupId, colorId, ct);
        return ToResponse(result);
    }

    [HttpPost("group/{groupId:guid}/{colorId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid groupId, Guid colorId, CancellationToken ct)
    {
        var result = await _service.ActivateAsync(groupId, colorId, ct);
        return ToResponse(result);
    }
}
