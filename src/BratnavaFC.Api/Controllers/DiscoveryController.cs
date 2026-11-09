using System.Security.Claims;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Discovery;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/discovery")]
[Authorize(Roles = "User,Admin,GodMode")]
public sealed class DiscoveryController : BaseApiController
{
    private readonly IDiscoveryService _service;
    private readonly AppDbContext _db;
    public DiscoveryController(IDiscoveryService service, AppDbContext db) { _service = service; _db = db; }

    [HttpGet("groups")]
    public Task<IActionResult> SearchGroups([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Respond(_service.SearchGroupsAsync(q, page, pageSize, ct));

    [HttpGet("players")]
    public Task<IActionResult> SearchPlayers([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Respond(_service.SearchPlayersAsync(UserId(), q, page, pageSize, ct));

    [HttpGet("groups/{groupId:guid}")]
    public Task<IActionResult> GroupProfile(Guid groupId, CancellationToken ct) =>
        Respond(_service.GetGroupProfileAsync(groupId, UserId(), ct));

    [HttpPut("groups/{groupId:guid}/profile")]
    public async Task<IActionResult> UpdateGroupProfile(Guid groupId, UpdateGroupProfileDto dto, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.UpdateGroupProfileAsync(groupId, dto, ct));
    }

    [HttpPost("groups/{groupId:guid}/code/regenerate")]
    public async Task<IActionResult> RegenerateCode(Guid groupId, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.RegenerateGroupCodeAsync(groupId, ct));
    }

    [HttpPost("groups/{groupId:guid}/matches/{matchId:guid}/openings")]
    public async Task<IActionResult> CreateOpening(Guid groupId, Guid matchId, UpsertMatchOpeningDto dto, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.CreateOpeningAsync(groupId, matchId, dto, ct));
    }

    [HttpPut("groups/{groupId:guid}/openings/{openingId:guid}")]
    public async Task<IActionResult> UpdateOpening(Guid groupId, Guid openingId, UpsertMatchOpeningDto dto, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.UpdateOpeningAsync(groupId, openingId, dto, ct));
    }

    [HttpDelete("groups/{groupId:guid}/openings/{openingId:guid}")]
    public async Task<IActionResult> DeleteOpening(Guid groupId, Guid openingId, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.DeleteOpeningAsync(groupId, openingId, ct));
    }

    [HttpPost("groups/{groupId:guid}/applications")]
    public Task<IActionResult> Apply(Guid groupId, CreateMatchApplicationDto dto, CancellationToken ct) =>
        Respond(_service.ApplyAsync(groupId, UserId(), dto, ct));

    [HttpGet("groups/{groupId:guid}/applications")]
    public async Task<IActionResult> GroupApplications(Guid groupId, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.GetGroupApplicationsAsync(groupId, ct));
    }

    [HttpGet("applications/mine")]
    public Task<IActionResult> MyApplications(CancellationToken ct) => Respond(_service.GetMyApplicationsAsync(UserId(), ct));

    [HttpGet("groups/{groupId:guid}/guest-candidates/{userId:guid}")]
    public async Task<IActionResult> GuestCandidates(Guid groupId, Guid userId, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.GetGuestCandidatesAsync(groupId, userId, ct));
    }

    [HttpPatch("groups/{groupId:guid}/applications/{applicationId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid groupId, Guid applicationId, ApproveMatchApplicationDto dto, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.ApproveAsync(groupId, applicationId, dto, ct));
    }

    [HttpPatch("groups/{groupId:guid}/applications/{applicationId:guid}/reject")]
    public async Task<IActionResult> RejectByAdmin(Guid groupId, Guid applicationId, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.RejectByAdminAsync(groupId, applicationId, ct));
    }

    [HttpPost("groups/{groupId:guid}/invitations")]
    public async Task<IActionResult> Invite(Guid groupId, CreateAdminMatchInviteDto dto, CancellationToken ct)
    {
        if (!await IsAdmin(groupId, ct)) return Forbid();
        return ToResponse(await _service.InviteAsync(groupId, UserId(), dto, ct));
    }

    [HttpPatch("applications/{applicationId:guid}/accept")]
    public Task<IActionResult> Accept(Guid applicationId, CancellationToken ct) =>
        Respond(_service.AcceptByPlayerAsync(applicationId, UserId(), ct));

    [HttpPatch("applications/{applicationId:guid}/reject")]
    public Task<IActionResult> Reject(Guid applicationId, CancellationToken ct) =>
        Respond(_service.RejectByPlayerAsync(applicationId, UserId(), ct));

    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new UnauthorizedAccessException();

    private Task<bool> IsAdmin(Guid groupId, CancellationToken ct) => User.IsInRole("GodMode")
        ? Task.FromResult(true)
        : _db.GroupAdmins.AnyAsync(x => x.GroupId == groupId && x.UserId == UserId(), ct);

    private async Task<IActionResult> Respond<T>(Task<BratnavaFC.Domain.Common.Result<T>> task) => ToResponse(await task);
    private async Task<IActionResult> Respond(Task<BratnavaFC.Domain.Common.Result> task) => ToResponse(await task);
}
