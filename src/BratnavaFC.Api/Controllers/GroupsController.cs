using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "User,Admin,GodMode")]
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

        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();

        var requestWithCreator = request with { CreatedByUserId = currentUserId.Value };

        var result = await _groupService.CreateAsync(requestWithCreator, cancellationToken);
        return ToResponse(result);
    }

    [HttpDelete("{groupId:guid}")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _groupService.DeleteAsync(groupId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{groupId:guid}")]
    public async Task<IActionResult> UpdateGroupAsync(Guid groupId, [FromBody] UpdateGroupDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.UpdateAsync(groupId, request, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{groupId:guid}/inactivate")]
    public async Task<IActionResult> InactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.InactivateAsync(groupId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{groupId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateAsync(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.ReactivateAsync(groupId, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> GetAllGroupsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _groupService.GetAllGroupsAsync(page, pageSize, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("{groupId:guid}")]
    public async Task<IActionResult> GetByIdAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _groupService.GetByIdAsync(groupId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("{groupId:guid}/logo")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogoAsync(
        Guid groupId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();
        if (file is null || file.Length == 0)
            return BadRequest("Selecione uma logo.");
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("A logo deve ter no máximo 5 MB.");

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);

        var result = await _groupService.SetLogoAsync(
            groupId, buffer.ToArray(), file.ContentType, cancellationToken);
        return ToResponse(result);
    }

    [AllowAnonymous]
    [HttpGet("{groupId:guid}/logo")]
    public async Task<IActionResult> GetLogoAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _groupService.GetLogoAsync(groupId, cancellationToken);
        if (!result.Success || result.Data == default)
            return ToResponse(result);

        var (data, contentType, updatedAt) = result.Data;
        Response.Headers.CacheControl = "public,max-age=3600,must-revalidate";
        Response.Headers.ETag = $"\"{updatedAt.ToUnixTimeMilliseconds()}\"";
        Response.Headers.LastModified = updatedAt.ToString("R");
        return File(data, contentType);
    }

    [HttpDelete("{groupId:guid}/logo")]
    public async Task<IActionResult> DeleteLogoAsync(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.RemoveLogoAsync(groupId, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("{groupId:guid}/my-roles")]
    [Authorize]
    public async Task<IActionResult> GetMyRolesAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var isAdmin       = await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken);
        var isFinanceiro  = await IsFinanceiroForGroupAsync(groupId, _db, cancellationToken);
        return Ok(new { success = true, data = new { isAdmin, isFinanceiro } });
    }

    [HttpGet("admin/{adminId:guid}")]
    public async Task<IActionResult> GetByAdminIdAsync(Guid adminId, CancellationToken cancellationToken)
    {
        var result = await _groupService.GetByAdminIdAsync(adminId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("{groupId:guid}/admins")]
    public async Task<IActionResult> AddAdminAsync(Guid groupId, [FromBody] AddAdminToGroupDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.AddAdminToGroupAsync(groupId, request, cancellationToken);
        return ToResponse(result);
    }

    [HttpDelete("{groupId:guid}/admins/{userId:guid}")]
    public async Task<IActionResult> RemoveAdminAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var requestingUserId = GetCurrentUserId();
        if (requestingUserId == null) return Unauthorized();

        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.RemoveAdminAsync(groupId, userId, requestingUserId.Value, cancellationToken);
        return ToResponse(result);
    }

    // ── Financeiros ───────────────────────────────────────────────────────────

    [HttpGet("financeiro/{financeiroId:guid}")]
    public async Task<IActionResult> GetByFinanceiroIdAsync(Guid financeiroId, CancellationToken cancellationToken)
    {
        var result = await _groupService.GetByFinanceiroIdAsync(financeiroId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("{groupId:guid}/financeiros")]
    public async Task<IActionResult> AddFinanceiroAsync(Guid groupId, [FromBody] AddAdminToGroupDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.AddFinanceiroToGroupAsync(groupId, request.UserId, cancellationToken);
        return ToResponse(result);
    }

    [HttpDelete("{groupId:guid}/financeiros/{userId:guid}")]
    public async Task<IActionResult> RemoveFinanceiroAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.RemoveFinanceiroAsync(groupId, userId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("{groupId:guid}/leave-creator")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> CreatorLeaveGroupAsync(Guid groupId, [FromBody] CreatorLeaveGroupDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        var requestingUserId = GetCurrentUserId();
        if (requestingUserId == null) return Unauthorized();

        var result = await _groupService.CreatorLeaveGroupAsync(groupId, requestingUserId.Value, request, cancellationToken);
        return ToResponse(result);
    }

    // ── Convites ──────────────────────────────────────────────────────────────

    /// <summary>Lista os convites pendentes da patota com dados do convidado (visão do admin).</summary>
    [HttpGet("{groupId:guid}/invites/pending")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetGroupPendingInvitesAsync(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.GetGroupPendingInvitesAsync(groupId, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>Admin cancela um convite pendente.</summary>
    [HttpDelete("{groupId:guid}/invites/{inviteId:guid}")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> CancelInviteAsync(Guid groupId, Guid inviteId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken))
            return Forbid();

        var result = await _groupService.CancelInviteAsync(groupId, inviteId, cancellationToken);
        return ToResponse(result);
    }

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

        var result = await _groupService.CreateInviteAsync(groupId, request, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>Lista convites pendentes do usuário logado.</summary>
    [HttpGet("invites/mine")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetMyInvitesAsync(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _groupService.GetMyInvitesAsync(userId.Value, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>Quantidade de convites pendentes do usuário logado.</summary>
    [HttpGet("invites/mine/count")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetMyPendingInviteCountAsync(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _groupService.GetMyPendingInviteCountAsync(userId.Value, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>Usuário aceita um convite.</summary>
    [HttpPatch("invites/{inviteId:guid}/accept")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> AcceptInviteAsync(Guid inviteId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _groupService.AcceptInviteAsync(inviteId, userId.Value, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>Usuário rejeita um convite.</summary>
    [HttpPatch("invites/{inviteId:guid}/reject")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> RejectInviteAsync(Guid inviteId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var result = await _groupService.RejectInviteAsync(inviteId, userId.Value, cancellationToken);
        return ToResponse(result);
    }
}
