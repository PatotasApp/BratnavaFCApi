using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public class TeamGenerationController : BaseApiController
{
    private readonly TeamGenerationService _teamService;
    private readonly IPlayerStatsService _playerStats;

    public TeamGenerationController(TeamGenerationService teamService, IPlayerStatsService playerStats)
    {
        _teamService = teamService ?? throw new ArgumentNullException(nameof(teamService));
        _playerStats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(ApiResponse<TeamsOptionsResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TeamsOptionsResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate(
        [FromBody] TeamGenerationRequestDto request,
        [FromQuery] int count = 3,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return ToResponse(Result<TeamsOptionsResultDto>.Fail("Request inválido.", ResultStatus.BadRequest));

        var result = await _teamService.GenerateAsync(request, count, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("visual-stats/{groupId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlayerVisualStatsReport>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisualStats([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        var report = await _playerStats.GetVisualReportAsync(groupId, cancellationToken);
        return ToResponse(Result<PlayerVisualStatsReport>.Ok(report));
    }

    [HttpGet("spotlight/{groupId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlayerSpotlightReport>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpotlight([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        var report = await _playerStats.GetSpotlightReportAsync(groupId, cancellationToken);
        return ToResponse(Result<PlayerSpotlightReport>.Ok(report));
    }
}