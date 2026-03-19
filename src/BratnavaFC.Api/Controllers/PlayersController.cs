using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Players;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PlayersController : ControllerBase
{
    private readonly IPlayerService _playerService;

    public PlayersController(IPlayerService playerService)
    {
        _playerService = playerService;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePlayer([FromBody] CreatePlayerDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        var playerCreated = await _playerService.CreateAsync(request, cancellationToken);
        return Ok(playerCreated);
    }

    [HttpDelete("{playerId:guid}")]
    public async Task<IActionResult> DeletePlayer(Guid playerId, CancellationToken cancellationToken)
    {
        await _playerService.DeleteAsync(playerId, cancellationToken);
        return Ok();
    }

    [HttpGet("{playerId:guid}")]
    public async Task<IActionResult> GetPlayer(Guid playerId, CancellationToken cancellationToken)
    {
        var player = await _playerService.GetByIdAsync(playerId, cancellationToken);
        return Ok(player);
    }

    [HttpPut("{playerId:guid}")]
    public async Task<IActionResult> UpdatePlayer(Guid playerId, [FromBody] UpdatePlayerDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        var playerUpdated = await _playerService.UpdateAsync(playerId, request, cancellationToken);
        return Ok(playerUpdated);
    }

    [HttpPost("{playerId:guid}/inactivate")]
    public async Task<IActionResult> InactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        await _playerService.InactivateAsync(playerId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{playerId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        await _playerService.ReactivateAsync(playerId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{playerId:guid}/leave")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> LeaveGroup(Guid playerId, CancellationToken cancellationToken)
    {
        var userId = GetUserIdOrThrow();
        await _playerService.LeaveGroupAsync(playerId, userId, cancellationToken);
        return NoContent();
    }

    [HttpGet("mine")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userId = GetUserIdOrThrow();
        var items = await _playerService.GetByUserIdAsync(userId, cancellationToken);
        return Ok(items);
    }

    [HttpGet("by-user/{userId:guid}")]
    [Authorize(Roles = "Admin,GodMode")]
    public async Task<IActionResult> GetByUser(Guid userId, CancellationToken cancellationToken)
    {
        var items = await _playerService.GetByUserIdAsync(userId, cancellationToken);
        return Ok(items);
    }

    private Guid GetUserIdOrThrow()
    {
        // padrão
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            throw new UnauthorizedAccessException("Invalid user id in token.");

        return userId;
    }
}
