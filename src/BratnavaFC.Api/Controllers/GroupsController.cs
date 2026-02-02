using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers
{
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
        public async Task<IActionResult> CreateGroupAsync([FromBody] GroupContracts.CreateGroupRequest request, CancellationToken cancellationToken)
        {
            await _groupService.CreateAsync(request, cancellationToken);
            return Ok();
        }

        [HttpDelete("{groupId:guid}")]
        public async Task<IActionResult> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken)
        {
            await _groupService.DeleteAsync(new GroupContracts.DeleteGroupRequest(groupId), cancellationToken);
            return Ok();
        }

        [HttpPut("{groupId:guid}")]
        public async Task<IActionResult> UpdateGroupAsync(Guid groupId, [FromBody] GroupContracts.UpdateGroupRequest request, CancellationToken cancellationToken)
        {
            await _groupService.UpdateAsync(new GroupContracts.UpdateGroupRequest(groupId, request.Name, request.ScheduleMatchDate, request.Status), cancellationToken);
            return Ok();
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
    }
}
