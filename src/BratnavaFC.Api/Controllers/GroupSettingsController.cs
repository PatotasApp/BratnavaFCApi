using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class GroupSettingsController : GroupAuthorizedController
{
    private readonly IGroupSettingsService _service;
    private readonly AppDbContext _db;

    public GroupSettingsController(IGroupSettingsService service, AppDbContext db)
    {
        _service = service;
        _db = db;
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

    [HttpPut("group/{groupId:guid}")]
    public async Task<IActionResult> Upsert(Guid groupId, [FromBody] UpsertGroupSettingsDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
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
