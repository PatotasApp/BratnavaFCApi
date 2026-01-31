using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Models;
using BratnavaFC.Domain.Dtos;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TeamGenerationController : ControllerBase
{
    private readonly TeamGenerationService _teamService;

    public TeamGenerationController(TeamGenerationService teamService)
    {
        _teamService = teamService ?? throw new ArgumentNullException(nameof(teamService));
    }

    [HttpPost("generate")]
    public async Task<ActionResult<TeamsResultDto>> Generate([FromBody] TeamGenerationRequestDto request)
    {
        if (request == null) return BadRequest();

        var players = request.Players.Select(p => new Player
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


}