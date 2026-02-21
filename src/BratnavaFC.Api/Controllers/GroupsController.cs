using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class GroupsController : ControllerBase
{
    private readonly IGroupService _groupService;

    public GroupsController(IGroupService groupService)
    {
        _groupService = groupService;
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
}
