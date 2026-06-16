using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.TeamBuilder;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class TeamBuilderController : GroupAuthorizedController
{
    private readonly ITeamBuilderService _teamBuilder;
    private readonly AppDbContext        _db;

    public TeamBuilderController(ITeamBuilderService teamBuilder, AppDbContext db)
    {
        _teamBuilder = teamBuilder;
        _db          = db;
    }

    [HttpPost("group/{groupId:guid}/stats")]
    public async Task<IActionResult> GetStats(
        Guid groupId,
        [FromBody] TeamBuilderRequestDto dto,
        CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();

        var result = await _teamBuilder.GetStatsAsync(groupId, dto.PlayerIds, ct);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new { data = result.Data });
    }
}
