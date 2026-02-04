using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        _playerStats = playerStats;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<TeamsResultDto>> Generate([FromBody] TeamGenerationRequestDto request)
    {
        if (request == null) return BadRequest();

        var players = request.Players.Select(p => new PlayerEntity
        {
            Id = p.Id,
            Name = p.Name,
            IsGoalkeeper = p.IsGoalkeeper
        }).ToList();

        var settings = new TeamGenerationSettings
        {
            PlayersPerTeam = request.PlayersPerTeam,
            IncludeGoalkeepers = request.IncludeGoalkeepers
        };

        var result = await _teamService.GenerateAsync(players, settings, request.StrategyType);
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