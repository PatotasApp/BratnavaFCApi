using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public class MatchesController : GroupAuthorizedController
{
    private readonly IMatchService _service;
    private readonly AppDbContext _db;

    public MatchesController(IMatchService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetAll(Guid groupId, CancellationToken cancellationToken)
    {
        var matches = await _service.GetAllAsync(groupId, cancellationToken);
        return Ok(matches);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _service.GetByIdAsync(groupId, matchId, cancellationToken);
        if (match == null) return NotFound();
        return Ok(ToDto(match));
    }

    [HttpGet("group/{groupId:guid}/current")]
    public async Task<IActionResult> GetCurrent(Guid groupId, CancellationToken cancellationToken)
    {
        var match = await _service.GetCurrentAsync(groupId, cancellationToken);
        if (match is null) return NotFound();
        return Ok(ToDto(match));
    }

    // header leve (pra stepper / status)
    [HttpGet("group/{groupId:guid}/{matchId:guid}/header")]
    public async Task<IActionResult> GetHeader(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var dto = await _service.GetHeaderAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/acceptation")]
    public async Task<IActionResult> GetAcceptation(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var dto = await _service.GetAcceptationAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/matchmaking")]
    public async Task<IActionResult> GetMatchMaking(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var dto = await _service.GetMatchMakingAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/postgame")]
    public async Task<IActionResult> GetPostGame(Guid groupId, Guid matchId, CancellationToken ct)
    {
        var dto = await _service.GetPostGameAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}")]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateMatchDto dto, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpPost("group/{groupId:guid}/{matchId:guid}/players/sync")]
    public async Task<IActionResult> SyncPlayers(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpPut("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Update(Guid groupId, Guid matchId, [FromBody] UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpDelete("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpPost("group/{groupId:guid}/{matchId:guid}/start")]
    public async Task<IActionResult> StartAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpPost("group/{groupId:guid}/{matchId:guid}/end")]
    public async Task<IActionResult> EndAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/score")]
    public async Task<IActionResult> SetScoreAsync(Guid groupId, Guid matchId, [FromBody] SetScoreRequestDto dto, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/colors")]
    public async Task<IActionResult> SetMatchColorsAsync(Guid groupId, Guid matchId, [FromBody] SetMatchColorsRequestDto dto, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpPost("group/{groupId:guid}/{matchId:guid}/finalize")]
    public async Task<IActionResult> FinalizeAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
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

    [HttpGet("group/{groupId:guid}/{matchId:guid}/details")]
    public async Task<ActionResult<MatchDetailsDto>> GetDetails(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        CancellationToken cancellationToken)
    {
        var details = await _service.GetDetailsAsync(matchId, cancellationToken);
        if (details is null) return NotFound();
        if (details.GroupId != groupId) return NotFound();
        return Ok(details);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/goals")]
    public async Task<IActionResult> GetGoals(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var goals = await _service.GetGoalsAsync(groupId, matchId, cancellationToken);
        return Ok(goals);
    }

    [HttpPut("group/{groupId:guid}/{matchId:guid}/teams")]
    public async Task<IActionResult> AssignTeams(
        Guid groupId,
        Guid matchId,
        [FromBody] AssignTeamsDto dto,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
        await _service.AssignTeamsAsync(groupId, matchId, dto, cancellationToken);
        return NoContent();
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/swap")]
    public async Task<IActionResult> SwapPlayers(
        Guid groupId,
        Guid matchId,
        [FromBody] SwapPlayersDto dto,
        CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.SwapPlayersByPlayerIdAsync(groupId, matchId, dto.PlayerAId, dto.PlayerBId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/goals")]
    public async Task<IActionResult> AddGoal(
        Guid groupId,
        Guid matchId,
        [FromBody] AddGoalRequestDto dto,
        CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.AddGoalAsync(groupId, matchId, dto, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("group/{groupId:guid}/{matchId:guid}/goals/{goalId:guid}")]
    public async Task<IActionResult> RemoveGoal(Guid groupId, Guid matchId, Guid goalId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.RemoveGoalAsync(groupId, matchId, goalId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/matchmaking")]
    public async Task<IActionResult> GoToMatchMaking(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.GoToMatchMakingAsync(groupId, matchId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/postgame")]
    public async Task<IActionResult> GoToPostGame(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.GoToPostGameAsync(groupId, matchId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/goals/bulk")]
    public async Task<IActionResult> AddGoalsBulk(
        Guid groupId,
        Guid matchId,
        [FromBody] AddGoalsBulkRequestDto dto,
        CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.AddGoalsBulkAsync(groupId, matchId, dto, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}/{matchId:guid}/rewind")]
    public async Task<IActionResult> Rewind(Guid groupId, Guid matchId, CancellationToken ct)
    {
        try
        {
            await _service.RewindOneStepAsync(groupId, matchId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("group/{groupId:guid}/history")]
    public async Task<IActionResult> GetHistory(
    Guid groupId,
    [FromQuery] int take = 200,
    CancellationToken cancellationToken = default)
    {
        var items = await _service.GetHistoryAsync(groupId, take, cancellationToken);
        return Ok(items);
    }

    [Authorize(Roles = "Admin,GodMode")]
    [HttpPost("group/{groupId:guid}/{matchId:guid}/guests")]
    public async Task<IActionResult> AddGuestToMatch(
        Guid groupId,
        Guid matchId,
        [FromBody] AddGuestToMatchDto dto,
        CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.AddGuestToMatchAsync(groupId, matchId, dto, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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