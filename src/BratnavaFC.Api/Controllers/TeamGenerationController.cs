using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TeamGenerationController : ControllerBase
{
    private readonly TeamGenerationService _teamService;
    private readonly IPlayerStatsService _playerStats;

    public TeamGenerationController(TeamGenerationService teamService, IPlayerStatsService playerStats)
    {
        _teamService = teamService ?? throw new ArgumentNullException(nameof(teamService));
        _playerStats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(TeamsResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TeamsResultDto>> Generate([FromBody] TeamGenerationRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        var result = await _teamService.GenerateAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("visual-stats/{groupId:guid}")]
    [ProducesResponseType(typeof(PlayerVisualStatsReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisualStats([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        var report = await _playerStats.GetVisualReportAsync(groupId, cancellationToken);
        return Ok(report);
    }
}
