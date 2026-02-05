using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetAll(Guid groupId, CancellationToken cancellationToken)
    {
        var matches = await _service.GetAllAsync(groupId, cancellationToken);
        return Ok(matches.Select(ToDto));
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _service.GetByIdAsync(groupId, matchId, cancellationToken);
        if (match == null) return NotFound();
        return Ok(ToDto(match));
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}")]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateMatchDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var entity = new MatchEntity(groupId, dto.PlayedAt, dto.PlaceName);
            var created = await _service.Create(groupId, entity, cancellationToken);

            return CreatedAtAction(nameof(Get), new { groupId, matchId = created.Id }, ToDto(created));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}/{matchId:guid}/players/sync")]
    public async Task<IActionResult> SyncPlayers(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.SyncPlayersFromGroupAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPut("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Update(Guid groupId, Guid matchId, [FromBody] UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.UpdateAsync(groupId, matchId, dto, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpDelete("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/invite/accept")]
    public async Task<IActionResult> AcceptInviteAsync(Guid groupId, Guid matchId, [FromBody] InviteActionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.AcceptInviteAsync(groupId, matchId, dto.PlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/invite/reject")]
    public async Task<IActionResult> RejectInviteAsync(Guid groupId, Guid matchId, [FromBody] InviteActionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.RejectInviteAsync(groupId, matchId, dto.PlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}/{matchId:guid}/start")]
    public async Task<IActionResult> StartAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.StartMatchAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}/{matchId:guid}/end")]
    public async Task<IActionResult> EndAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.EndMatchAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/vote")]
    public async Task<IActionResult> VoteAsync(Guid groupId, Guid matchId, [FromBody] VoteRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.VoteAsync(groupId, matchId, dto.VoterPlayerId, dto.VotedPlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/mvp")]
    public async Task<IActionResult> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var mvp = await _service.GetMvpAsync(groupId, matchId, cancellationToken);
        if (mvp == null) return NotFound();

        return Ok(new MatchPlayerDto(mvp.Id, mvp.Player?.Name ?? string.Empty, mvp.IsMvp));
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPatch("group/{groupId:guid}/{matchId:guid}/score")]
    public async Task<IActionResult> SetScoreAsync(Guid groupId, Guid matchId, [FromBody] SetScoreRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.SetScoreAsync(groupId, matchId, dto.TeamAGoals, dto.TeamBGoals, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPatch("group/{groupId:guid}/{matchId:guid}/colors")]
    public async Task<IActionResult> SetMatchColorsAsync(Guid groupId, Guid matchId, [FromBody] SetMatchColorsRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.SetTeamColorsAsync(groupId, matchId, dto.TeamAColorId, dto.TeamBColorId, dto.Randomize, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}/{matchId:guid}/finalize")]
    public async Task<IActionResult> FinalizeAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.FinalizeMatchAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{matchId:guid}/details")]
    public async Task<ActionResult<MatchDetailsDto>> GetDetails(
    [FromRoute] Guid matchId,
    CancellationToken cancellationToken)
    {
        var details = await _service.GetDetailsAsync(matchId, cancellationToken);
        if (details is null) return NotFound();

        return Ok(details);
    }
    private static MatchDto ToDto(MatchEntity e) =>
        new(
            e.Id,
            e.GroupId,
            e.PlayedAt,
            e.TeamAGoals ?? 0,
            e.TeamBGoals ?? 0,
            e.PlaceName,
            e.TeamAColorId,
            e.TeamBColorId
        );
}
