using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.TeamGeneration;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BratnavaFC.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public class TeamGenerationController : BaseApiController
{
    private readonly TeamGenerationService _teamService;
    private readonly IPlayerStatsService _playerStats;
    private readonly AppDbContext _db;
    private readonly ISeasonService _seasons;

    public TeamGenerationController(TeamGenerationService teamService, IPlayerStatsService playerStats, AppDbContext db, ISeasonService seasons)
    {
        _seasons     = seasons ?? throw new ArgumentNullException(nameof(seasons));
        _teamService = teamService ?? throw new ArgumentNullException(nameof(teamService));
        _playerStats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
        _db          = db ?? throw new ArgumentNullException(nameof(db));
    }

    // Espelha GroupAuthorizedController.IsGroupAdminAsync (este controller herda de BaseApiController).
    private async Task<bool> IsGroupAdminAsync(Guid groupId, CancellationToken ct)
    {
        if (User.IsInRole("Admin") || User.IsInRole("GodMode")) return true;

        // NameIdentifier carrega a identidade INTERNA (FirebaseIdentityMiddleware); "sub" tem o
        // UID do Firebase e não serve para comparar com GroupAdmins.UserId.
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty) return false;

        return await _db.GroupAdmins.AnyAsync(x => x.GroupId == groupId && x.UserId == userId, ct);
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(ApiResponse<TeamsOptionsResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TeamsOptionsResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generate(
        [FromBody] TeamGenerationRequestDto request,
        [FromQuery] int count = 3,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return ToResponse(Result<TeamsOptionsResultDto>.Fail("Request inválido.", ResultStatus.BadRequest));

        var result = await _teamService.GenerateAsync(request, count, cancellationToken);
        return ToResponse(result);
    }

    [HttpGet("visual-stats/{groupId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlayerVisualStatsReport>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisualStats(
        [FromRoute] Guid groupId,
        [FromQuery] bool includeGuests = true,
        [FromQuery] Guid? seasonId = null,
        [FromQuery] int? year = null,
        [FromQuery] bool includePreSeason = false,
        CancellationToken cancellationToken = default)
    {
        // Sem temporada nem ano: tudo (como sempre foi; o app antigo não manda nada).
        var period = await _seasons.ResolvePeriodAsync(groupId, seasonId, year, includePreSeason, cancellationToken);
        if (!period.Success) return ToResponse(Result<PlayerVisualStatsReport>.Fail(period.Error ?? "Periodo invalido.", period.Status));

        var report = await _playerStats.GetVisualReportAsync(groupId, includeGuests, period.Data, cancellationToken);
        return ToResponse(Result<PlayerVisualStatsReport>.Ok(report));
    }

    [HttpGet("spotlight/{groupId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<PlayerSpotlightReport>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpotlight([FromRoute] Guid groupId, CancellationToken cancellationToken)
    {
        // Spotlight é exclusivo de admins do grupo.
        if (!await IsGroupAdminAsync(groupId, cancellationToken))
            return Forbid();

        var report = await _playerStats.GetSpotlightReportAsync(groupId, cancellationToken);
        return ToResponse(Result<PlayerSpotlightReport>.Ok(report));
    }
}