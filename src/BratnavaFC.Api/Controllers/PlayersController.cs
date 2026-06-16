using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PlayersController : GroupAuthorizedController
{
    private readonly IPlayerService _playerService;
    private readonly AppDbContext _db;

    public PlayersController(IPlayerService playerService, AppDbContext db)
    {
        _playerService = playerService;
        _db = db;
    }

    [HttpPost]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> CreatePlayer([FromBody] CreatePlayerDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        if (!await IsAuthorizedForGroupAsync(request.GroupId, _db, cancellationToken))
            return Forbid();

        var result = await _playerService.CreateAsync(request, cancellationToken);
        return ToResponse(result);
    }

    [HttpDelete("{playerId:guid}")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> DeletePlayer(Guid playerId, CancellationToken cancellationToken)
    {
        var result = await _playerService.DeleteAsync(playerId, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("{playerId:guid}")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetPlayer(Guid playerId, CancellationToken cancellationToken)
    {
        var result = await _playerService.GetByIdAsync(playerId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPut("{playerId:guid}")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> UpdatePlayer(Guid playerId, [FromBody] UpdatePlayerDto request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest();

        var groupId = await _playerService.GetGroupIdAsync(playerId, cancellationToken);
        if (groupId == null) return NotFound();

        if (!await IsAuthorizedForGroupAsync(groupId.Value, _db, cancellationToken))
            return Forbid();

        var result = await _playerService.UpdateAsync(playerId, request, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("{playerId:guid}/inactivate")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> InactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var groupId = await _playerService.GetGroupIdAsync(playerId, cancellationToken);
        if (groupId == null) return NotFound();

        if (!await IsAuthorizedForGroupAsync(groupId.Value, _db, cancellationToken))
            return Forbid();

        var result = await _playerService.InactivateAsync(playerId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("{playerId:guid}/reactivate")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> ReactivateAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var groupId = await _playerService.GetGroupIdAsync(playerId, cancellationToken);
        if (groupId == null) return NotFound();

        if (!await IsAuthorizedForGroupAsync(groupId.Value, _db, cancellationToken))
            return Forbid();

        var result = await _playerService.ReactivateAsync(playerId, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>
    /// Remove um mensalista da patota: desvincula a conta e converte para convidado.
    /// Requer permissão de admin do grupo.
    /// </summary>
    [HttpPost("{playerId:guid}/remove-from-group")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> RemoveFromGroup(Guid playerId, CancellationToken cancellationToken)
    {
        var groupId = await _playerService.GetGroupIdAsync(playerId, cancellationToken);
        if (groupId == null) return NotFound();

        if (!await IsAuthorizedForGroupAsync(groupId.Value, _db, cancellationToken))
            return Forbid();

        var result = await _playerService.RemoveFromGroupAsync(playerId, cancellationToken);
        return ToResponse(result);
    }

    [HttpPost("{playerId:guid}/leave")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> LeaveGroup(Guid playerId, CancellationToken cancellationToken)
    {
        var userId = GetUserIdOrThrow();
        var result = await _playerService.LeaveGroupAsync(playerId, userId, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userId = GetUserIdOrThrow();
        var result = await _playerService.GetByUserIdAsync(userId, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("by-user/{userId:guid}")]
    [Authorize(Roles = "Admin,GodMode")]
    public async Task<IActionResult> GetByUser(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _playerService.GetByUserIdAsync(userId, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/birthday-status")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> GetBirthdayStatus(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, cancellationToken)) return Forbid();
        var result = await _playerService.GetBirthdayStatusAsync(groupId, cancellationToken);
        return ToResponse(result);
    }

    /// <summary>
    /// Alterna a posição de um jogador entre goleiro e jogador de linha.
    /// Requer permissão de admin do grupo.
    /// </summary>
    [HttpPatch("{playerId:guid}/toggle-goalkeeper")]
    [Authorize(Roles = "User,Admin,GodMode")]
    public async Task<IActionResult> ToggleGoalkeeper(Guid playerId, CancellationToken cancellationToken)
    {
        var groupId = await _playerService.GetGroupIdAsync(playerId, cancellationToken);
        if (groupId == null) return NotFound();

        if (!await IsAuthorizedForGroupAsync(groupId.Value, _db, cancellationToken))
            return Forbid();

        var result = await _playerService.ToggleGoalkeeperAsync(playerId, cancellationToken);
        return ToResponse(result);
    }

    private Guid GetUserIdOrThrow()
    {
        var raw =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            throw new UnauthorizedAccessException("Invalid user id in token.");

        return userId;
    }
}
