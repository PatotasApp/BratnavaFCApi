using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Dtos.Players;
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
        public async Task<IActionResult> CreateGroupAsync([FromBody] CreateGroupDto request, CancellationToken cancellationToken)
        {
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
            await _groupService.UpdateAsync(groupId, request, cancellationToken);
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

        [HttpPut("{groupId:guid}/activate")]
        public async Task<IActionResult> ActivateGroupAsync(Guid groupId, CancellationToken cancellationToken)
        {
            await _groupService.ActivateAsync(groupId, cancellationToken);
            return Ok();
        }

        [HttpPut("{groupId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateGroupAsync(Guid groupId, CancellationToken cancellationToken)
        {
            await _groupService.DeactivateAsync(groupId, cancellationToken);
            return Ok();
        }

        [HttpPost("{groupId:guid}/players")]
        public async Task<IActionResult> AddPlayerAsync(Guid groupId, [FromBody] CreatePlayerDto request, CancellationToken cancellationToken)
        {
            var playerId = await _groupService.AddPlayerAsync(groupId, request, cancellationToken);
            return Ok(playerId);
        }

        [HttpDelete("{groupId:guid}/players/{playerId:guid}")]
        public async Task<IActionResult> RemovePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
        {
            await _groupService.RemovePlayerAsync(groupId, playerId, cancellationToken);
            return Ok();
        }

        [HttpPut("{groupId:guid}/players/{playerId:guid}/deactivate")]
        public async Task<IActionResult> DeactivatePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
        {
            await _groupService.DeactivatePlayerAsync(groupId, playerId, cancellationToken);
            return Ok();
        }

        [HttpPut("{groupId:guid}/players/{playerId:guid}/activate")]
        public async Task<IActionResult> ActivatePlayerAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
        {
            await _groupService.ActivatePlayerAsync(groupId, playerId, cancellationToken);
            return Ok();
        }

        [HttpPut("{groupId:guid}/players/{playerId:guid}")]
        public async Task<IActionResult> UpdatePlayerAsync(Guid groupId, Guid playerId, [FromBody] UpdatePlayerDto request, CancellationToken cancellationToken)
        {
            await _groupService.UpdatePlayerAsync(groupId, playerId, request, cancellationToken);
            return Ok();    
        }

        [HttpGet("{groupId:guid}/players/{playerId:guid}")]
        public async Task<IActionResult> GetPlayerByIdAsync(Guid groupId, Guid playerId, CancellationToken cancellationToken)
        {
            var player = await _groupService.GetPlayerByIdAsync(groupId, playerId, cancellationToken);
            return Ok(player);
        }

        [HttpGet("{groupId:guid}/players")]
        public async Task<IActionResult> GetPlayersByGroupIdAsync(Guid groupId, CancellationToken cancellationToken)
        {
            var players = await _groupService.GetPlayersByGroupIdAsync(groupId, cancellationToken);
            return Ok(players);
        }
    }
}
