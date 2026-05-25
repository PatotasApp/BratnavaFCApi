using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public class BetController : GroupAuthorizedController
{
    private readonly IBetService _bets;
    private readonly AppDbContext _db;

    public BetController(IBetService bets, AppDbContext db)
    {
        _bets = bets;
        _db   = db;
    }

    /// <summary>
    /// Lista de partidas elegíveis para apostas no grupo:
    /// status MatchMaking + pelo menos um jogador em cada time.
    /// Usada para popular o carrossel de seleção de partida.
    /// </summary>
    [HttpGet("group/{groupId:guid}/bettable-matches")]
    public async Task<IActionResult> GetBettableMatches(
        [FromRoute] Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var matches = await _bets.GetBettableMatchesAsync(groupId, ct);
        return Ok(matches);
    }

    /// <summary>
    /// Retorna o contexto de aposta de uma partida específica (carrossel multi-partida).
    /// Inclui a lista de jogadores e a aposta já feita pelo usuário (se houver).
    /// </summary>
    [HttpGet("group/{groupId:guid}/match/{matchId:guid}/context")]
    public async Task<IActionResult> GetContextForMatch(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var ctx = await _bets.GetContextForMatchAsync(groupId, matchId, userId.Value, ct);
        if (ctx is null) return NotFound();

        return Ok(ctx);
    }

    /// <summary>
    /// Retorna o contexto da partida atual para montar o formulário de aposta.
    /// Inclui a lista de jogadores e a aposta já feita pelo usuário (se houver).
    /// </summary>
    [HttpGet("group/{groupId:guid}/current")]
    public async Task<IActionResult> GetCurrentContext(
        [FromRoute] Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var ctx = await _bets.GetCurrentContextAsync(groupId, userId.Value, ct);
        if (ctx is null) return NotFound();

        return Ok(ctx);
    }

    /// <summary>
    /// Cria ou substitui a aposta do usuário na partida atual (somente durante matchmaking).
    /// </summary>
    [HttpPost("group/{groupId:guid}/match/{matchId:guid}")]
    public async Task<IActionResult> PlaceOrUpdateBet(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        [FromBody]  PlaceMatchBetDto dto,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _bets.PlaceOrUpdateBetAsync(groupId, matchId, userId.Value, dto, ct);

        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Retorna os resultados de todas as apostas de uma partida.
    /// Se a partida estiver finalizada e ainda não resolvida, resolve agora.
    /// </summary>
    [HttpGet("group/{groupId:guid}/match/{matchId:guid}/results")]
    public async Task<IActionResult> GetMatchResults(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var results = await _bets.GetMatchResultsAsync(groupId, matchId, ct);
        if (results is null) return NotFound();

        return Ok(results);
    }

    /// <summary>Ranking de fichas acumuladas do grupo.</summary>
    [HttpGet("group/{groupId:guid}/leaderboard")]
    public async Task<IActionResult> GetLeaderboard(
        [FromRoute] Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var leaderboard = await _bets.GetLeaderboardAsync(groupId, ct);
        return Ok(leaderboard);
    }

    /// <summary>Retorna o saldo de fichas do usuário logado no grupo.</summary>
    [HttpGet("group/{groupId:guid}/balance")]
    public async Task<IActionResult> GetMyBalance(
        [FromRoute] Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var balance = await _bets.GetMyBalanceAsync(groupId, userId.Value, ct);
        return Ok(new { balance });
    }

    /// <summary>Remove a aposta do usuário na partida (somente durante matchmaking).</summary>
    [HttpDelete("group/{groupId:guid}/match/{matchId:guid}")]
    public async Task<IActionResult> DeleteBet(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var result = await _bets.DeleteBetAsync(groupId, matchId, userId.Value, ct);
        return result.Success ? Ok() : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Parcial das apostas: simula o resultado como se a partida encerrasse agora.
    /// Usa placar e gols atuais sem persistir nada. Exclusivo para admins do grupo.
    /// </summary>
    [HttpGet("group/{groupId:guid}/match/{matchId:guid}/preview")]
    public async Task<IActionResult> GetBetPreview(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var preview = await _bets.GetBetPreviewAsync(groupId, matchId, ct);
        if (preview is null) return NotFound();

        return Ok(preview);
    }

    /// <summary>Histórico de apostas de todas as partidas resolvidas do grupo.</summary>
    [HttpGet("group/{groupId:guid}/history")]
    public async Task<IActionResult> GetHistory(
        [FromRoute] Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var history = await _bets.GetHistoryAsync(groupId, ct);
        return Ok(history);
    }
}
