using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Application.Abstractions;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public class MatchesController : ControllerBase
{
    private readonly IMatchService _service;

    public MatchesController(IMatchService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var matches = await _service.GetAllAsync(cancellationToken);
        return Ok(matches.Select(ToDto));
    }

    [HttpGet("{matchId:guid}")]
    public async Task<IActionResult> Get(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _service.GetByIdAsync(matchId, cancellationToken);
        if (match == null) return NotFound();
        return Ok(ToDto(match));
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMatchDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var entity = FromDto(dto);
            var created = await _service.Create(entity, cancellationToken);
            return CreatedAtAction(nameof(Get), new { matchId = created.Id }, ToDto(created));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPut("{matchId:guid}")]
    public async Task<IActionResult> Update(Guid matchId, [FromBody] UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.UpdateAsync(matchId, dto, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpDelete("{matchId:guid}")]
    public async Task<IActionResult> Delete(Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{matchId:guid}/invite/accept")]
    public async Task<IActionResult> AcceptInviteAsync(Guid matchId, [FromBody] InviteActionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.AcceptInviteAsync(matchId, dto.PlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("{matchId:guid}/invite/reject")]
    public async Task<IActionResult> RejectInviteAsync(Guid matchId, [FromBody] InviteActionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.RejectInviteAsync(matchId, dto.PlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("{matchId:guid}/start")]
    public async Task<IActionResult> StartAsync(Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.StartMatchAsync(matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("{matchId:guid}/end")]
    public async Task<IActionResult> EndAsync(Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.EndMatchAsync(matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{matchId:guid}/vote")]
    public async Task<IActionResult> VoteAsync(Guid matchId, [FromBody] VoteRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.VoteAsync(matchId, dto.VoterPlayerId, dto.VotedPlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{matchId:guid}/mvp")]
    public async Task<IActionResult> GetMvpAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var mvp = await _service.GetMvpAsync(matchId, cancellationToken);
        if (mvp == null) return NotFound();

        return Ok(new MatchPlayerDto(mvp.Id, mvp.Player?.Name ?? string.Empty, mvp.IsMvp));
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPut("{matchId:guid}/score")]
    public async Task<IActionResult> SetScoreAsync(Guid matchId, [FromBody] SetScoreRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.SetScoreAsync(matchId, dto.TeamAGoals, dto.TeamBGoals, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPut("{matchId:guid}/colors")]
    public async Task<IActionResult> SetMatchColorsAsync(Guid matchId, [FromBody] SetMatchColorsRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.SetTeamColorsAsync(
                matchId,
                dto.TeamAColorId,
                dto.TeamBColorId,
                dto.Randomize,
                cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("{matchId:guid}/finalize")]
    public async Task<IActionResult> FinalizeAsync(Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.FinalizeMatchAsync(matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static MatchDto ToDto(MatchEntity e) =>
        new(e.PlayedAt, e.TeamAGoals ?? 0, e.TeamBGoals ?? 0, e.PlaceName, e.TeamAColorId, e.TeamBColorId);

    private static MatchEntity FromDto(CreateMatchDto dto) =>
        new(dto.PlayedAt, dto.PlaceName);
}
