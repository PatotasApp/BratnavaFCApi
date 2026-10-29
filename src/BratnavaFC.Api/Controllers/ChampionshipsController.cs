using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>Modo Campeonato. Contrato em docs/campeonato/CONTRATO-API.md.</summary>
[ApiController]
[Route("api/championships/group/{groupId:guid}")]
[Authorize(Roles = "User,Admin,GodMode")]
public class ChampionshipsController : GroupAuthorizedController
{
    private readonly IChampionshipService _service;
    private readonly AppDbContext _db;

    public ChampionshipsController(IChampionshipService service, AppDbContext db)
    {
        _service = service;
        _db = db;
    }

    // ── Consulta ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.ListAsync(groupId, ct));
    }

    [HttpGet("{championshipId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid championshipId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.GetAsync(groupId, championshipId, GetCurrentUserId(), ct));
    }

    [HttpGet("{championshipId:guid}/next-game")]
    public async Task<IActionResult> NextGame(Guid groupId, Guid championshipId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.GetNextGameAsync(groupId, championshipId, ct));
    }

    // ── Criação e regras ─────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateChampionshipDto dto, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.CreateAsync(groupId, dto, ct));
    }

    [HttpPut("{championshipId:guid}")]
    public Task<IActionResult> Update(Guid groupId, Guid championshipId, [FromBody] UpdateChampionshipDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.UpdateAsync(groupId, championshipId, dto, ct));

    [HttpPost("{championshipId:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid groupId, Guid championshipId, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.CancelAsync(groupId, championshipId, ct));

    // ── Presença ─────────────────────────────────────────────────────────────

    [HttpPost("{championshipId:guid}/acceptation/open")]
    public Task<IActionResult> OpenAcceptation(Guid groupId, Guid championshipId, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.OpenAcceptationAsync(groupId, championshipId, ct));

    [HttpPatch("{championshipId:guid}/my-invite/accept")]
    public Task<IActionResult> AcceptMyInvite(Guid groupId, Guid championshipId, CancellationToken ct) =>
        AsMember(groupId, ct, userId => _service.RespondMyInviteAsync(groupId, championshipId, userId, accept: true, ct));

    [HttpPatch("{championshipId:guid}/my-invite/reject")]
    public Task<IActionResult> RejectMyInvite(Guid groupId, Guid championshipId, CancellationToken ct) =>
        AsMember(groupId, ct, userId => _service.RespondMyInviteAsync(groupId, championshipId, userId, accept: false, ct));

    [HttpPatch("{championshipId:guid}/invite/accept")]
    public Task<IActionResult> AcceptInvite(Guid groupId, Guid championshipId, [FromBody] ChampionshipPlayerActionDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.RespondInviteAsync(groupId, championshipId, dto.PlayerId, accept: true, ct));

    [HttpPatch("{championshipId:guid}/invite/reject")]
    public Task<IActionResult> RejectInvite(Guid groupId, Guid championshipId, [FromBody] ChampionshipPlayerActionDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.RespondInviteAsync(groupId, championshipId, dto.PlayerId, accept: false, ct));

    [HttpPost("{championshipId:guid}/players/guest")]
    public Task<IActionResult> AddGuest(Guid groupId, Guid championshipId, [FromBody] AddChampionshipGuestDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.AddGuestAsync(groupId, championshipId, dto, ct));

    // ── Times ────────────────────────────────────────────────────────────────

    [HttpPost("{championshipId:guid}/teams/draw")]
    public async Task<IActionResult> DrawTeams(Guid groupId, Guid championshipId, [FromBody] DrawTeamsDto dto, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.DrawTeamsAsync(groupId, championshipId, dto.TeamCount, ct));
    }

    [HttpPut("{championshipId:guid}/teams")]
    public Task<IActionResult> DefineTeams(Guid groupId, Guid championshipId, [FromBody] DefineTeamsDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.DefineTeamsAsync(groupId, championshipId, dto, ct));

    [HttpPatch("{championshipId:guid}/players/{playerId:guid}/team")]
    public Task<IActionResult> MovePlayer(Guid groupId, Guid championshipId, Guid playerId, [FromBody] MovePlayerDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.MovePlayerAsync(groupId, championshipId, playerId, dto.TeamId, ct));

    [HttpPost("{championshipId:guid}/teams/colors/draw")]
    public async Task<IActionResult> DrawTeamColors(Guid groupId, Guid championshipId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();

        var result = await _service.DrawTeamColorsAsync(groupId, championshipId, ct);
        var response = result.Success
            ? Result<object?>.Ok(null, result.Message)
            : Result<object?>.Fail(result.Error ?? "Falha ao sortear as cores.", result.Status, result.Errors);
        return ToResponse(response);
    }

    [HttpPatch("{championshipId:guid}/teams/{teamId:guid}")]
    public Task<IActionResult> UpdateTeam(Guid groupId, Guid championshipId, Guid teamId, [FromBody] UpdateChampionshipTeamDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.UpdateTeamAsync(groupId, championshipId, teamId, dto, ct));

    [HttpPatch("{championshipId:guid}/goalkeeper")]
    public Task<IActionResult> SetGoalkeeper(Guid groupId, Guid championshipId, [FromBody] SetGameGoalkeeperDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.SetTeamGoalkeeperAsync(groupId, championshipId, dto, ct));

    // ── Jogos ────────────────────────────────────────────────────────────────

    [HttpPost("{championshipId:guid}/start")]
    public Task<IActionResult> Start(Guid groupId, Guid championshipId, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.StartAsync(groupId, championshipId, ct));

    [HttpPost("{championshipId:guid}/games")]
    public async Task<IActionResult> CreateGame(Guid groupId, Guid championshipId, [FromBody] CreateChampionshipGameDto dto, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.CreateGameAsync(groupId, championshipId, dto, ct));
    }

    [HttpPost("{championshipId:guid}/games/{gameId:guid}/goals")]
    public Task<IActionResult> AddGoal(Guid groupId, Guid championshipId, Guid gameId, [FromBody] ChampionshipGoalDto dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.AddGoalAsync(groupId, championshipId, gameId, dto, ct));

    [HttpDelete("{championshipId:guid}/games/{gameId:guid}/goals/{goalId:guid}")]
    public Task<IActionResult> RemoveGoal(Guid groupId, Guid championshipId, Guid gameId, Guid goalId, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.RemoveGoalAsync(groupId, championshipId, gameId, goalId, ct));

    [HttpPost("{championshipId:guid}/games/{gameId:guid}/end")]
    public Task<IActionResult> EndGame(Guid groupId, Guid championshipId, Guid gameId, [FromBody] EndChampionshipGameDto? dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.EndGameAsync(groupId, championshipId, gameId, dto ?? new EndChampionshipGameDto(null), ct));

    [HttpPost("{championshipId:guid}/step-back")]
    public Task<IActionResult> StepBack(Guid groupId, Guid championshipId, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.StepBackAsync(groupId, championshipId, ct));

    [HttpPost("{championshipId:guid}/games/{gameId:guid}/events")]
    public async Task<IActionResult> PublishGameEvent(
        Guid groupId, Guid championshipId, Guid gameId, [FromBody] ChampionshipGameEventRequestDto dto, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await _service.PublishGameEventAsync(groupId, championshipId, gameId, GetCurrentUserId(), dto, ct));
    }

    [HttpPost("{championshipId:guid}/finish")]
    public Task<IActionResult> Finish(Guid groupId, Guid championshipId, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.FinishAsync(groupId, championshipId, ct));

    // ── Encerramento ─────────────────────────────────────────────────────────

    [HttpPost("{championshipId:guid}/vote")]
    public Task<IActionResult> Vote(Guid groupId, Guid championshipId, [FromBody] ChampionshipVoteDto dto, CancellationToken ct) =>
        AsMember(groupId, ct, userId => _service.VoteAsync(groupId, championshipId, userId, dto, ct));

    [HttpPost("{championshipId:guid}/finalize")]
    public Task<IActionResult> Finalize(Guid groupId, Guid championshipId, [FromBody] FinalizeChampionshipDto? dto, CancellationToken ct) =>
        AsAdmin(groupId, ct, () => _service.FinalizeAsync(groupId, championshipId, dto ?? new FinalizeChampionshipDto(null), ct));

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private async Task<IActionResult> AsAdmin(Guid groupId, CancellationToken ct, Func<Task<Result>> action)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await action());
    }

    private async Task<IActionResult> AsMember(Guid groupId, CancellationToken ct, Func<Guid, Task<Result>> action)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        return ToResponse(await action(userId.Value));
    }
}
