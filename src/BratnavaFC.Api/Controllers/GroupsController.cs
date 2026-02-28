using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class GroupsController : GroupAuthorizedController
{
    private readonly IGroupService _groupService;
    private readonly AppDbContext _db;

    public GroupsController(IGroupService groupService, AppDbContext db)
    {
        _groupService = groupService;
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> CreateGroupAsync([FromBody] CreateGroupDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        var newGroupId = await _groupService.CreateAsync(request, cancellationToken);
        return Ok(newGroupId);
    }

    [HttpDelete("{groupId:guid}")]
    public async Task<IActionResult> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await _groupService.DeleteAsync(groupId, cancellationToken);
        return Ok();
    }

    [HttpPut("{groupId:guid}")]
    public async Task<IActionResult> UpdateGroupAsync(Guid groupId, [FromBody] UpdateGroupDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        await _groupService.UpdateAsync(groupId, request, cancellationToken);
        return Ok();
    }

    [HttpPut("{groupId:guid}/inactivate")]
    public async Task<IActionResult> InactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await _groupService.InactivateAsync(groupId, cancellationToken);
        return NoContent();
    }

    [HttpPut("{groupId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        await _groupService.ReactivateAsync(groupId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{groupId:guid}")]
    public async Task<IActionResult> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var response = await _groupService.GetByIdAsync(groupId, cancellationToken);
        return Ok(response);
    }

    [HttpGet("admin/{adminId:guid}")]
    public async Task<IActionResult> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken)
    {
        var response = await _groupService.GetByAdminIdAsync(adminId, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{groupId:guid}/admins")]
    public async Task<IActionResult> AddAdminAsync(Guid groupId, [FromBody] AddAdminToGroupDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        await _groupService.AddAdminToGroupAsync(groupId, request, cancellationToken);
        return NoContent();
    }

    // ── Convites ──────────────────────────────────────────────────────────────

    /// <summary>Admin da patota envia convite para um usuário.</summary>
    [HttpPost("{groupId:guid}/invites")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> CreateInviteAsync(
        Guid groupId,
        [FromBody] CreateGroupInviteDto request,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        try
        {
            var result = await _groupService.CreateInviteAsync(groupId, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Lista convites pendentes do usuário logado.</summary>
    [HttpGet("invites/mine")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetMyInvitesAsync(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var list = await _groupService.GetMyInvitesAsync(userId.Value, cancellationToken);
        return Ok(list);
    }

    /// <summary>Quantidade de convites pendentes do usuário logado.</summary>
    [HttpGet("invites/mine/count")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetMyPendingInviteCountAsync(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var count = await _groupService.GetMyPendingInviteCountAsync(userId.Value, cancellationToken);
        return Ok(new { count });
    }

    /// <summary>Usuário aceita um convite.</summary>
    [HttpPatch("invites/{inviteId:guid}/accept")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> AcceptInviteAsync(Guid inviteId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        try
        {
            await _groupService.AcceptInviteAsync(inviteId, userId.Value, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Usuário rejeita um convite.</summary>
    [HttpPatch("invites/{inviteId:guid}/reject")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> RejectInviteAsync(Guid inviteId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        try
        {
            await _groupService.RejectInviteAsync(inviteId, userId.Value, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
