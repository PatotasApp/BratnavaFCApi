using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Players;
using Microsoft.AspNetCore.Mvc;

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

    [HttpPut("{playerId:guid}/inactivate")]
    public async Task<IActionResult> InactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        await _playerService.InactivateAsync(playerId, cancellationToken);
        return NoContent();
    }

    [HttpPut("{playerId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        await _playerService.ReactivateAsync(playerId, cancellationToken);
        return NoContent();
    }
}
