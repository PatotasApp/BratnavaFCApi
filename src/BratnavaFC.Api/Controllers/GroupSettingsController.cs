using System;
using System.Threading;
using System.Threading.Tasks;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class GroupSettingsController : ControllerBase
{
    private readonly IGroupSettingsService _service;

    public GroupSettingsController(IGroupSettingsService service)
    {
        _service = service;
    }

    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetAsync(groupId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPut("group/{groupId:guid}")]
    public async Task<IActionResult> Upsert(Guid groupId, [FromBody] UpsertGroupSettingsDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _service.UpsertAsync(groupId, dto, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
