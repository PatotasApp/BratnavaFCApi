using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "User,Admin,GodMode")]
public class MatchesController : GroupAuthorizedController
{
    private readonly IMatchService _service;
    private readonly AppDbContext _db;
    private readonly IMatchEventPublisher _eventPublisher;
    private readonly IReplayUrlService _replayUrl;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBetService _bets;

    public MatchesController(IMatchService service, AppDbContext db, IMatchEventPublisher eventPublisher, IReplayUrlService replayUrl, IHttpClientFactory httpClientFactory, IBetService bets)
    {
        _service            = service;
        _db                 = db;
        _eventPublisher     = eventPublisher;
        _replayUrl          = replayUrl;
        _httpClientFactory  = httpClientFactory;
        _bets               = bets;
    }

    [HttpGet("group/{groupId:guid}")]
    public async Task<IActionResult> GetAll(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsGroupMemberAsync(groupId, _db, cancellationToken)) return Forbid();
        var matches = await _service.GetAllAsync(groupId, cancellationToken);
        return Ok(matches);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Get(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsGroupMemberAsync(groupId, _db, cancellationToken)) return Forbid();
        var match = await _service.GetByIdAsync(groupId, matchId, cancellationToken);
        if (match is null || !match.Success || match.Data is null) return NotFound();
        return ToResponse(Result<MatchDto>.Ok(ToDto(match.Data)));
    }

    [HttpGet("group/{groupId:guid}/current")]
    public async Task<IActionResult> GetCurrent(Guid groupId, CancellationToken cancellationToken)
    {
        if (!await IsGroupMemberAsync(groupId, _db, cancellationToken)) return Forbid();
        var match = await _service.GetCurrentAsync(groupId, cancellationToken);
        if (match.Data is null) return NotFound();
        return ToResponse(Result<MatchDto>.Ok(ToDto(match.Data)));
    }

    /// <summary>
    /// Returns all non-finalized matches ordered by date ascending (nearest first).
    /// Capped at <see cref="BratnavaFC.Domain.Constants.MatchConstants.MaxSimultaneousActiveMatches"/>.
    /// </summary>
    [HttpGet("group/{groupId:guid}/upcoming")]
    public async Task<IActionResult> GetUpcoming(Guid groupId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var result = await _service.GetUpcomingAsync(groupId, ct);
        return ToResponse(result);
    }

    // header leve (pra stepper / status)
    [HttpGet("group/{groupId:guid}/{matchId:guid}/header")]
    public async Task<IActionResult> GetHeader(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var dto = await _service.GetHeaderAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/acceptation")]
    public async Task<IActionResult> GetAcceptation(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var dto = await _service.GetAcceptationAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    /// <summary>Resumo de aceitação sem convidados — para uso no dashboard.</summary>
    [HttpGet("group/{groupId:guid}/{matchId:guid}/acceptation/summary")]
    public async Task<IActionResult> GetAcceptationSummary(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var dto = await _service.GetAcceptationSummaryAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/matchmaking")]
    public async Task<IActionResult> GetMatchMaking(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var dto = await _service.GetMatchMakingAsync(groupId, matchId, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/postgame")]
    public async Task<IActionResult> GetPostGame(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        var dto = await _service.GetPostGameAsync(groupId, matchId, ct, userId);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    [HttpPost("group/{groupId:guid}")]
    public async Task<IActionResult> Create(Guid groupId, [FromBody] CreateMatchDto dto, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            var entity = new MatchEntity(groupId, dto.PlayedAt, dto.PlaceName);
            var created = await _service.Create(groupId, entity, cancellationToken);

            if (!created.Success || created.Data is null)
                return BadRequest(new { error = created.Error ?? "Falha ao criar partida." });

            return CreatedAtAction(nameof(Get), new { groupId, matchId = created.Data.Id }, ToDto(created.Data));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/players/sync")]
    public async Task<IActionResult> SyncPlayers(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            await _service.SyncPlayersFromGroupAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Update(Guid groupId, Guid matchId, [FromBody] UpdateMatchDto dto, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            var result = await _service.UpdateAsync(groupId, matchId, dto, cancellationToken);
            return ToResponse(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("group/{groupId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Delete(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            var result = await _service.DeleteAsync(groupId, matchId, cancellationToken);
            return ToResponse(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/my-invite/accept")]
    public async Task<IActionResult> AcceptMyInviteAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        try
        {
            var result = await _service.AcceptMyInviteAsync(groupId, matchId, userId.Value, cancellationToken);
            return ToResponse(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/my-invite/reject")]
    public async Task<IActionResult> RejectMyInviteAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        try
        {
            var result = await _service.RejectMyInviteAsync(groupId, matchId, userId.Value, cancellationToken);
            return ToResponse(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/invite/accept")]
    public async Task<IActionResult> AcceptInviteAsync(Guid groupId, Guid matchId, [FromBody] InviteActionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.AcceptInviteAsync(groupId, matchId, dto.PlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/invite/reject")]
    public async Task<IActionResult> RejectInviteAsync(Guid groupId, Guid matchId, [FromBody] InviteActionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.RejectInviteAsync(groupId, matchId, dto.PlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/start")]
    public async Task<IActionResult> StartAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            await _service.StartMatchAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/end")]
    public async Task<IActionResult> EndAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            await _service.EndMatchAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/vote")]
    public async Task<IActionResult> VoteAsync(Guid groupId, Guid matchId, [FromBody] VoteRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _service.VoteAsync(groupId, matchId, dto.VoterPlayerId, dto.VotedPlayerId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("group/{groupId:guid}/{matchId:guid}/mvp")]
    public async Task<IActionResult> GetMvpAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var mvp = await _service.GetMvpAsync(groupId, matchId, cancellationToken);
        if (mvp == null) return NotFound();

        return ToResponse(Result<MatchPlayerDto>.Ok(new MatchPlayerDto(mvp.Data.Id, mvp.Data.Player?.Name ?? string.Empty, mvp.Data.IsMvp)));
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/score")]
    public async Task<IActionResult> SetScoreAsync(Guid groupId, Guid matchId, [FromBody] SetScoreRequestDto dto, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            await _service.SetScoreAsync(groupId, matchId, dto.TeamAGoals, dto.TeamBGoals, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/colors")]
    public async Task<IActionResult> SetMatchColorsAsync(Guid groupId, Guid matchId, [FromBody] SetMatchColorsRequestDto dto, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            await _service.SetTeamColorsAsync(groupId, matchId, dto.TeamAColorId, dto.TeamBColorId, dto.Randomize, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/finalize")]
    public async Task<IActionResult> FinalizeAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            await _service.FinalizeMatchAsync(groupId, matchId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/reapply-mvp")]
    public async Task<IActionResult> ReapplyMvpAsync(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        var result = await _service.ReapplyMvpTieRuleAsync(groupId, matchId, cancellationToken);
        return ToResponse(result);
    }

    [EnableRateLimiting("PerUser")]
    [HttpGet("group/{groupId:guid}/{matchId:guid}/details")]
    public async Task<ActionResult<MatchDetailsDto>> GetDetails(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        CancellationToken cancellationToken)
    {
        var details = await _service.GetDetailsAsync(matchId, cancellationToken);
        if (details is null) return NotFound();
        if (details.Data?.GroupId != groupId) return NotFound();
        return Ok(details);
    }

    [EnableRateLimiting("PerUser")]
    [HttpGet("group/{groupId:guid}/{matchId:guid}/replays")]
    public async Task<IActionResult> GetReplays(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        var result = await _service.GetReplaysAsync(groupId, matchId, userId, ct);
        return ToResponse(result);
    }

    [HttpPost("group/{groupId:guid}/replays/{clipId:guid}/like")]
    public async Task<IActionResult> ToggleLike(
        [FromRoute] Guid groupId,
        [FromRoute] Guid clipId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var (isLiked, likeCount) = await _service.ToggleLikeAsync(clipId, userId.Value, ct);
        return Ok(new { isLiked, likeCount });
    }

    [HttpPost("group/{groupId:guid}/replays/{clipId:guid}/favorite")]
    public async Task<IActionResult> ToggleFavorite(
        [FromRoute] Guid groupId,
        [FromRoute] Guid clipId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var isFavorited = await _service.ToggleFavoriteAsync(clipId, userId.Value, ct);
        return Ok(new { isFavorited });
    }

    [HttpGet("group/{groupId:guid}/replays/all")]
    public async Task<IActionResult> GetAllGroupReplays(
        [FromRoute] Guid groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _service.GetAllGroupReplaysAsync(groupId, userId.Value, page, pageSize, ct);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/replays/liked")]
    public async Task<IActionResult> GetLikedReplays(
        [FromRoute] Guid groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        var result = await _service.GetLikedReplaysAsync(groupId, userId, page, pageSize, ct);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/replays/my-likes")]
    public async Task<IActionResult> GetMyLikes(
        [FromRoute] Guid groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _service.GetMyLikesAsync(groupId, userId.Value, page, pageSize, ct);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/replays/{clipId:guid}/likers")]
    public async Task<IActionResult> GetClipLikers(
        [FromRoute] Guid groupId,
        [FromRoute] Guid clipId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var result = await _service.GetClipLikersAsync(clipId, ct);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/replays/my-favorites")]
    public async Task<IActionResult> GetMyFavorites(
        [FromRoute] Guid groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _service.GetMyFavoritesAsync(groupId, userId.Value, page, pageSize, ct);
        return ToResponse(result);
    }

    [HttpGet("group/{groupId:guid}/replays/{clipId:guid}/download")]
    public async Task<IActionResult> DownloadReplay(
        [FromRoute] Guid groupId,
        [FromRoute] Guid clipId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct)) return Forbid();

        var clip = await _service.GetReplayClipAsync(groupId, clipId, ct);

        if (clip is null) return NotFound();

        var (stream, contentType) = await _replayUrl.GetObjectStreamAsync(clip.ObjectKey, ct);

        var filename = $"{clip.EventType}_{clip.RecordedAt:HH-mm-ss}.mp4";

        return File(stream, contentType, filename);
    }

    /// <summary>
    /// Proxy de streaming que encaminha Range requests ao R2 — necessário para iOS Safari.
    /// Aceita o JWT via query string "t" para uso em elementos &lt;video src&gt;.
    /// </summary>
    [HttpGet("group/{groupId:guid}/replays/{clipId:guid}/stream")]
    public async Task StreamReplay(
        [FromRoute] Guid groupId,
        [FromRoute] Guid clipId,
        CancellationToken ct)
    {
        if (!await IsGroupMemberAsync(groupId, _db, ct))
        {
            Response.StatusCode = 403;
            return;
        }

        var clip = await _service.GetReplayClipAsync(groupId, clipId, ct);

        if (clip is null)
        {
            Response.StatusCode = 404;
            return;
        }

        var presignedUrl = _replayUrl.GeneratePresignedUrl(clip.ObjectKey);

        using var httpClient = _httpClientFactory.CreateClient();
        var r2Request = new HttpRequestMessage(HttpMethod.Get, presignedUrl);

        // Encaminha o Range header do cliente para o R2 (suporte a streaming no iOS)
        if (Request.Headers.TryGetValue("Range", out var rangeValues))
            r2Request.Headers.TryAddWithoutValidation("Range", rangeValues.ToArray());

        var r2Response = await httpClient.SendAsync(
            r2Request, HttpCompletionOption.ResponseHeadersRead, ct);

        Response.StatusCode    = (int)r2Response.StatusCode;
        Response.ContentType   = r2Response.Content.Headers.ContentType?.ToString() ?? "video/mp4";
        Response.Headers["Accept-Ranges"] = "bytes";

        if (r2Response.Content.Headers.TryGetValues("Content-Length", out var cl))
            Response.Headers["Content-Length"] = cl.First();
        if (r2Response.Content.Headers.TryGetValues("Content-Range", out var cr))
            Response.Headers["Content-Range"] = cr.First();

        await r2Response.Content.CopyToAsync(Response.Body, ct);
    }

    [HttpDelete("group/{groupId:guid}/replays/{clipId:guid}")]
    [Authorize(Roles = "GodMode")]
    public async Task<IActionResult> DeleteReplay(
        [FromRoute] Guid groupId,
        [FromRoute] Guid clipId,
        CancellationToken ct)
    {
        await _service.DeleteReplayAsync(groupId, clipId, ct);
        return NoContent();
    }

    /// <summary>Upload manual de vídeo para uma partida (admin only).</summary>
    [HttpPost("group/{groupId:guid}/{matchId:guid}/replays/upload")]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public async Task<IActionResult> UploadReplay(
        [FromRoute] Guid groupId,
        [FromRoute] Guid matchId,
        [FromForm] IFormFile file,
        [FromForm] string eventType,
        CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Nenhum arquivo enviado." });

        var allowedTypes = new[] { "video/mp4", "video/quicktime", "video/webm", "video/x-msvideo" };
        if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
            return BadRequest(new { error = "Formato de vídeo não suportado. Use MP4, MOV, WebM ou AVI." });

        await using var stream = file.OpenReadStream();
        var result = await _service.UploadReplayAsync(
            groupId, matchId, userId.Value,
            stream, file.ContentType, file.FileName,
            eventType, ct);

        return ToResponse(result);
    }

    [EnableRateLimiting("PerUser")]
    [HttpGet("group/{groupId:guid}/{matchId:guid}/goals")]
    public async Task<IActionResult> GetGoals(Guid groupId, Guid matchId, CancellationToken cancellationToken)
    {
        var goals = await _service.GetGoalsAsync(groupId, matchId, cancellationToken);
        return Ok(goals);
    }

    [HttpPut("group/{groupId:guid}/{matchId:guid}/teams")]
    public async Task<IActionResult> AssignTeams(
        Guid groupId,
        Guid matchId,
        [FromBody] AssignTeamsDto dto,
        CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        await _service.AssignTeamsAsync(groupId, matchId, dto, cancellationToken);

        // Whenever teams are (re)assigned, pending bets are stale:
        // players may have changed sides, making old selections invalid.
        await _bets.ResetBetsForMatchAsync(matchId, cancellationToken);

        return NoContent();
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/swap")]
    public async Task<IActionResult> SwapPlayers(
        Guid groupId,
        Guid matchId,
        [FromBody] SwapPlayersDto dto,
        CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.SwapPlayersByPlayerIdAsync(groupId, matchId, dto.PlayerAId, dto.PlayerBId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/players/{matchPlayerId:guid}/no-show")]
    public async Task<IActionResult> SetNoShowAsync(
        Guid groupId, Guid matchId, Guid matchPlayerId,
        [FromBody] SetNoShowDto dto, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        var result = await _service.SetNoShowAsync(groupId, matchId, matchPlayerId, dto.DidNotPlay, cancellationToken);
        if (!result.Success) return result.Status == ResultStatus.NotFound ? NotFound(new { error = result.Error }) : BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpPatch("group/{groupId:guid}/{matchId:guid}/players/{matchPlayerId:guid}/role")]
    public async Task<IActionResult> SetPlayerRoleAsync(
        Guid groupId, Guid matchId, Guid matchPlayerId,
        [FromBody] SetPlayerRoleDto dto, CancellationToken cancellationToken)
    {
        if (!await IsGroupAdminAsync(groupId, _db, cancellationToken)) return Forbid();
        try
        {
            await _service.SetPlayerRoleAsync(groupId, matchId, matchPlayerId, dto, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/goals")]
    public async Task<IActionResult> AddGoal(
        Guid groupId,
        Guid matchId,
        [FromBody] AddGoalRequestDto dto,
        CancellationToken ct)
    {
        var result = await _service.AddGoalAsync(groupId, matchId, dto, ct);
        return ToResponse(result);
    }

    [HttpPut("group/{groupId:guid}/{matchId:guid}/goals/{goalId:guid}")]
    public async Task<IActionResult> UpdateGoal(
        Guid groupId,
        Guid matchId,
        Guid goalId,
        [FromBody] UpdateGoalRequestDto dto,
        CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.UpdateGoalAsync(groupId, matchId, goalId, dto, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("group/{groupId:guid}/{matchId:guid}/goals/{goalId:guid}")]
    public async Task<IActionResult> RemoveGoal(Guid groupId, Guid matchId, Guid goalId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.RemoveGoalAsync(groupId, matchId, goalId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/matchmaking")]
    public async Task<IActionResult> GoToMatchMaking(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.GoToMatchMakingAsync(groupId, matchId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/postgame")]
    public async Task<IActionResult> GoToPostGame(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.GoToPostGameAsync(groupId, matchId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/goals/bulk")]
    public async Task<IActionResult> AddGoalsBulk(
        Guid groupId,
        Guid matchId,
        [FromBody] AddGoalsBulkRequestDto dto,
        CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.AddGoalsBulkAsync(groupId, matchId, dto, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/rewind")]
    public async Task<IActionResult> Rewind(Guid groupId, Guid matchId, CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.RewindOneStepAsync(groupId, matchId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("group/{groupId:guid}/history")]
    public async Task<IActionResult> GetHistory(
        Guid groupId,
        [FromQuery] int take = 20,
        [FromQuery] int skip = 0,
        [FromQuery] Guid? playerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await IsGroupMemberAsync(groupId, _db, cancellationToken)) return Forbid();
        var result = await _service.GetHistoryAsync(groupId, take, skip, cancellationToken, playerId);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(result);
    }

    /// <summary>
    /// Retorna as últimas N partidas finalizadas de um jogador com dados enriquecidos
    /// (time, gols, assistências, MVP) numa única query — usado pelo dashboard.
    /// </summary>
    [HttpGet("group/{groupId:guid}/player-recent")]
    public async Task<IActionResult> GetPlayerRecentMatches(
        Guid groupId,
        [FromQuery] Guid playerId,
        [FromQuery] int take = 3,
        CancellationToken ct = default)
    {
        if (playerId == Guid.Empty) return BadRequest(new { error = "playerId é obrigatório." });

        var items = await _service.GetPlayerRecentMatchesAsync(groupId, playerId, take, ct);
        return Ok(items);
    }

    [HttpGet("group/{groupId:guid}/player-history")]
    public async Task<IActionResult> GetPlayerHistory(
        Guid groupId,
        [FromQuery] Guid playerId,
        [FromQuery] int? year = null,
        CancellationToken ct = default)
    {
        if (playerId == Guid.Empty) return BadRequest(new { error = "playerId é obrigatório." });

        // Admins and group admins can query any player in the group.
        // Regular members can only view their own player's history.
        if (!await IsGroupAdminAsync(groupId, _db, ct))
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Forbid();

            var ownsPlayer = await _db.Players
                .AnyAsync(p => p.GroupId == groupId && p.Id == playerId && p.UserId == userId.Value, ct);

            if (!ownsPlayer) return Forbid();
        }

        var items = await _service.GetPlayerHistoryAsync(groupId, playerId, year, ct);
        return Ok(items);
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/guests")]
    public async Task<IActionResult> AddGuestToMatch(
        Guid groupId,
        Guid matchId,
        [FromBody] AddGuestToMatchDto dto,
        CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        try
        {
            await _service.AddGuestToMatchAsync(groupId, matchId, dto, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("group/{groupId:guid}/{matchId:guid}/events")]
    public async Task<IActionResult> PublishMatchEvent(
        Guid groupId,
        Guid matchId,
        [FromBody] PublishMatchEventRequest dto,
        CancellationToken ct)
    {
        if (!await IsGroupAdminAsync(groupId, _db, ct)) return Forbid();
        await _eventPublisher.PublishAsync(groupId, matchId, dto.Type, dto.SecondsBeforeStart, dto.DurationSeconds, ct);
        return NoContent();
    }

    // ── Linked poll ───────────────────────────────────────────────────────────

    /// <summary>
    /// Vincula ou desvincula uma votação desta partida.
    /// Envie { "pollId": "guid" } para vincular ou { "pollId": null } para desvincular.
    /// </summary>
    [HttpPatch("group/{groupId:guid}/{matchId:guid}/linked-poll")]
    public async Task<IActionResult> SetLinkedPoll(
        Guid groupId,
        Guid matchId,
        [FromBody] SetLinkedPollRequestDto dto,
        CancellationToken ct)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct)) return Forbid();

        var result = await _service.SetLinkedPollAsync(groupId, matchId, dto.PollId, ct);
        if (!result.Success)
            return result.Status == Domain.Common.ResultStatus.NotFound
                ? NotFound(new { error = result.Error })
                : BadRequest(new { error = result.Error });

        return Ok(new { message = result.Message, linkedPollId = result.Data });
    }

    private static MatchDto ToDto(MatchEntity e) =>
        new(
            e.Id,
            e.GroupId,
            e.PlayedAt,
            e.TeamAGoals ?? 0,
            e.TeamBGoals ?? 0,
            e.PlaceName,
            e.TeamAColorId,
            e.TeamBColorId
        );
}
