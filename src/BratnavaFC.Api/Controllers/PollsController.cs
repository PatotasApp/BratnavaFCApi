using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Polls;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class PollsController : GroupAuthorizedController
{
    private readonly IPollService _polls;
    private readonly AppDbContext _db;

    public PollsController(IPollService polls, AppDbContext db)
    {
        _polls = polls;
        _db = db;
    }

    // GET /api/Polls/group/{groupId}
    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetPolls(Guid groupId, CancellationToken ct)
    {
        var playerId = await GetPlayerIdForGroup(groupId, ct);
        var result = await _polls.GetPollsAsync(groupId, playerId, ct);
        return ToResponse(result);
    }

    // GET /api/Polls/group/{groupId}/{pollId}
    [HttpGet("group/{groupId:guid}/{pollId:guid}")]
    public async Task<IActionResult> GetPoll(Guid groupId, Guid pollId, CancellationToken ct)
    {
        var playerId = await GetPlayerIdForGroup(groupId, ct);
        var isAdmin = await IsAuthorizedForGroupAsync(groupId, _db, ct);
        var result = await _polls.GetPollAsync(groupId, pollId, playerId, isAdmin, ct);
        return ToResponse(result);
    }

    // POST /api/Polls/group/{groupId}
    [HttpPost("group/{groupId:guid}")]
    public async Task<IActionResult> CreatePoll(Guid groupId, [FromBody] CreatePollDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _polls.CreatePollAsync(groupId, userId.Value, dto, ct);
        return ToResponse(result);
    }

    // POST /api/Polls/group/{groupId}/event
    [HttpPost("group/{groupId:guid}/event")]
    public async Task<IActionResult> CreateEventPoll(Guid groupId, [FromBody] CreateEventPollDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _polls.CreateEventPollAsync(groupId, userId.Value, dto, ct);
        return ToResponse(result);
    }

    // POST /api/Polls/group/{groupId}/{pollId}/close
    [HttpPost("group/{groupId:guid}/{pollId:guid}/close")]
    public async Task<IActionResult> ClosePoll(Guid groupId, Guid pollId, [FromBody] ClosePollDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _polls.ClosePollAsync(groupId, pollId, userId.Value, dto, ct);
        return ToResponse(result);
    }

    // PATCH /api/Polls/group/{groupId}/{pollId}/show-votes
    [HttpPatch("group/{groupId:guid}/{pollId:guid}/show-votes")]
    public async Task<IActionResult> SetShowVotes(Guid groupId, Guid pollId, [FromBody] SetShowVotesDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _polls.SetShowVotesAsync(groupId, pollId, dto.ShowVotes, ct);
        return ToResponse(result);
    }

    // PUT /api/Polls/group/{groupId}/{pollId}/reopen
    [HttpPut("group/{groupId:guid}/{pollId:guid}/reopen")]
    public async Task<IActionResult> ReopenPoll(Guid groupId, Guid pollId, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _polls.ReopenPollAsync(groupId, pollId, ct);
        return ToResponse(result);
    }

    // PATCH /api/Polls/group/{groupId}/{pollId}/deadline
    [HttpPatch("group/{groupId:guid}/{pollId:guid}/deadline")]
    public async Task<IActionResult> UpdateDeadline(Guid groupId, Guid pollId, [FromBody] UpdatePollDeadlineDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _polls.UpdateDeadlineAsync(groupId, pollId, dto, ct);
        return ToResponse(result);
    }

    // DELETE /api/Polls/group/{groupId}/{pollId}
    [HttpDelete("group/{groupId:guid}/{pollId:guid}")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> DeletePoll(Guid groupId, Guid pollId, CancellationToken ct)
    {
        var result = await _polls.DeletePollAsync(groupId, pollId, ct);
        return ToResponse(result);
    }

    // POST /api/Polls/group/{groupId}/{pollId}/options
    [HttpPost("group/{groupId:guid}/{pollId:guid}/options")]
    public async Task<IActionResult> AddOption(Guid groupId, Guid pollId, [FromBody] AddPollOptionDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _polls.AddOptionAsync(groupId, pollId, dto, ct);
        return ToResponse(result);
    }

    // PUT /api/Polls/group/{groupId}/{pollId}/options/{optionId}
    [HttpPut("group/{groupId:guid}/{pollId:guid}/options/{optionId:guid}")]
    public async Task<IActionResult> UpdateOption(Guid groupId, Guid pollId, Guid optionId, [FromBody] UpdatePollOptionDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _polls.UpdateOptionAsync(groupId, pollId, optionId, dto, ct);
        return ToResponse(result);
    }

    // DELETE /api/Polls/group/{groupId}/{pollId}/options/{optionId}
    [HttpDelete("group/{groupId:guid}/{pollId:guid}/options/{optionId:guid}")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> DeleteOption(Guid groupId, Guid pollId, Guid optionId, CancellationToken ct)
    {
        var result = await _polls.DeleteOptionAsync(groupId, pollId, optionId, ct);
        return ToResponse(result);
    }

    // POST /api/Polls/group/{groupId}/{pollId}/vote
    [HttpPost("group/{groupId:guid}/{pollId:guid}/vote")]
    public async Task<IActionResult> CastVote(Guid groupId, Guid pollId, [FromBody] CastVoteDto dto, CancellationToken ct)
    {
        var playerId = await GetPlayerIdForGroup(groupId, ct);
        if (playerId == Guid.Empty) return Forbid();
        var isAdmin = await IsAuthorizedForGroupAsync(groupId, _db, ct);
        var result = await _polls.CastVoteAsync(groupId, pollId, playerId, dto, isAdmin, ct);
        return ToResponse(result);
    }

    // POST /api/Polls/group/{groupId}/{pollId}/admin-vote
    [HttpPost("group/{groupId:guid}/{pollId:guid}/admin-vote")]
    public async Task<IActionResult> AdminCastVote(Guid groupId, Guid pollId, [FromBody] AdminCastVoteDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _polls.AdminCastVoteAsync(groupId, pollId, dto, ct);
        return ToResponse(result);
    }

    // DELETE /api/Polls/group/{groupId}/{pollId}/vote
    [HttpDelete("group/{groupId:guid}/{pollId:guid}/vote")]
    public async Task<IActionResult> RemoveVote(Guid groupId, Guid pollId, CancellationToken ct)
    {
        var playerId = await GetPlayerIdForGroup(groupId, ct);
        if (playerId == Guid.Empty) return Forbid();
        var isAdmin = await IsAuthorizedForGroupAsync(groupId, _db, ct);
        var result = await _polls.RemoveVoteAsync(groupId, pollId, playerId, isAdmin, ct);
        return ToResponse(result);
    }

    // PATCH /api/Polls/group/{groupId}/{pollId}/allow-guests
    [HttpPatch("group/{groupId:guid}/{pollId:guid}/allow-guests")]
    public async Task<IActionResult> SetAllowGuests(Guid groupId, Guid pollId, [FromBody] SetAllowGuestsDto dto, CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();
        var result = await _polls.SetAllowGuestsAsync(groupId, pollId, dto.AllowGuests, ct);
        return ToResponse(result);
    }

    // POST /api/Polls/group/{groupId}/{pollId}/guests
    [HttpPost("group/{groupId:guid}/{pollId:guid}/guests")]
    public async Task<IActionResult> AddGuest(Guid groupId, Guid pollId, [FromBody] AddPollGuestDto dto, CancellationToken ct)
    {
        var playerId = await GetPlayerIdForGroup(groupId, ct);
        if (playerId == Guid.Empty) return Forbid();
        var result = await _polls.AddGuestAsync(groupId, pollId, playerId, dto, ct);
        return ToResponse(result);
    }

    // DELETE /api/Polls/group/{groupId}/{pollId}/guests/{guestId}
    [HttpDelete("group/{groupId:guid}/{pollId:guid}/guests/{guestId:guid}")]
    public async Task<IActionResult> RemoveGuest(Guid groupId, Guid pollId, Guid guestId, CancellationToken ct)
    {
        var playerId = await GetPlayerIdForGroup(groupId, ct);
        if (playerId == Guid.Empty) return Forbid();
        var isAdmin = await IsAuthorizedForGroupAsync(groupId, _db, ct);
        var result = await _polls.RemoveGuestAsync(groupId, pollId, guestId, playerId, isAdmin, ct);
        return ToResponse(result);
    }

    private async Task<Guid> GetPlayerIdForGroup(Guid groupId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Guid.Empty;
        var player = await _db.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && p.UserId == userId.Value && !p.IsGuest && p.Status == Domain.Enums.Status.Active)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct);
        return player?.Id ?? Guid.Empty;
    }
}

public class SetShowVotesDto
{
    public bool ShowVotes { get; set; }
}

public class SetAllowGuestsDto
{
    public bool AllowGuests { get; set; }
}
