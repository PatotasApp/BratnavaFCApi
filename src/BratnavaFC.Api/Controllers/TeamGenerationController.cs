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
    [ProducesResponseType(typeof(TeamsOptionsResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TeamsOptionsResultDto>> Generate(
        [FromBody] TeamGenerationRequestDto request,
        [FromQuery] int count = 3,
        CancellationToken cancellationToken = default)
    {
        if (request == null) return BadRequest();

        var result = await _teamService.GenerateAsync(request, count, cancellationToken);
        return Ok(result);
    }

    [HttpGet("visual-stats/{groupId:guid}")]
    [ProducesResponseType(typeof(PlayerVisualStatsReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisualStats([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        var report = await _playerStats.GetVisualReportAsync(groupId, cancellationToken);
        return Ok(report);
    }

    [HttpGet("spotlight/{groupId:guid}")]
    [ProducesResponseType(typeof(PlayerSpotlightReport), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpotlight([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        var report = await _playerStats.GetSpotlightReportAsync(groupId, cancellationToken);
        return Ok(report);
    }
}