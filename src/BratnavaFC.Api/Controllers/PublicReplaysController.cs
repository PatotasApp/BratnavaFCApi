using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Route("api/public")]
[AllowAnonymous]
public sealed class PublicReplaysController : ControllerBase
{
    private static readonly MatchEventType[] GoalTypes =
    [
        MatchEventType.Gol,
        MatchEventType.GolTimeA,
        MatchEventType.GolTimeB,
    ];

    private readonly AppDbContext _db;
    private readonly IReplayUrlService _replayUrls;

    public PublicReplaysController(AppDbContext db, IReplayUrlService replayUrls)
    {
        _db = db;
        _replayUrls = replayUrls;
    }

    // GET /api/public/clips/{clipId}
    [HttpGet("clips/{clipId:guid}")]
    public async Task<IActionResult> GetClip(Guid clipId, CancellationToken ct)
    {
        var clip = await _db.ReplayClips
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clipId, ct);

        if (clip is null) return NotFound();

        var matchColors = await _db.Matches
            .AsNoTracking()
            .Where(m => m.Id == clip.MatchId)
            .Select(m => new
            {
                TeamAColorName = m.TeamAColor != null ? m.TeamAColor.Name : null,
                TeamAColorHex = m.TeamAColor != null ? m.TeamAColor.HexValue : null,
                TeamBColorName = m.TeamBColor != null ? m.TeamBColor.Name : null,
                TeamBColorHex = m.TeamBColor != null ? m.TeamBColor.HexValue : null,
            })
            .FirstOrDefaultAsync(ct);

        int? goalNumber = null;
        int? totalGoals = null;

        if (IsGoal(clip.EventType))
        {
            var goalIds = await _db.ReplayClips
                .AsNoTracking()
                .Where(c => c.MatchId == clip.MatchId && GoalTypes.Contains(c.EventType))
                .OrderBy(c => c.RecordedAt)
                .Select(c => c.Id)
                .ToListAsync(ct);

            goalNumber = goalIds.IndexOf(clip.Id) + 1;
            totalGoals = goalIds.Count;
        }

        return Ok(new PublicClipDto(
            clip.Id,
            _replayUrls.GeneratePresignedUrl(clip.ObjectKey),
            clip.EventType.ToString(),
            clip.RecordedAt,
            goalNumber,
            totalGoals,
            matchColors?.TeamAColorName,
            matchColors?.TeamAColorHex,
            matchColors?.TeamBColorName,
            matchColors?.TeamBColorHex));
    }

    // GET /api/public/matches/{matchId}/replays
    [HttpGet("matches/{matchId:guid}/replays")]
    public async Task<IActionResult> GetMatchReplays(Guid matchId, CancellationToken ct)
    {
        var match = await _db.Matches
            .AsNoTracking()
            .Include(m => m.TeamAColor)
            .Include(m => m.TeamBColor)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null) return NotFound();

        var clips = await _db.ReplayClips
            .AsNoTracking()
            .Where(c => c.MatchId == matchId)
            .OrderBy(c => c.RecordedAt)
            .ToListAsync(ct);

        var goalIds = clips
            .Where(c => IsGoal(c.EventType))
            .Select(c => c.Id)
            .ToList();

        var publicClips = clips.Select(c => new PublicClipDto(
            c.Id,
            _replayUrls.GeneratePresignedUrl(c.ObjectKey),
            c.EventType.ToString(),
            c.RecordedAt,
            IsGoal(c.EventType) ? goalIds.IndexOf(c.Id) + 1 : null,
            IsGoal(c.EventType) ? goalIds.Count : null,
            match.TeamAColor?.Name,
            match.TeamAColor?.HexValue,
            match.TeamBColor?.Name,
            match.TeamBColor?.HexValue
        )).ToList();

        return Ok(new PublicMatchReplaysDto(
            match.Id,
            match.PlayedAt,
            match.PlaceName,
            match.TeamAGoals,
            match.TeamBGoals,
            match.TeamAColor?.Name,
            match.TeamAColor?.HexValue,
            match.TeamBColor?.Name,
            match.TeamBColor?.HexValue,
            publicClips));
    }

    private static bool IsGoal(MatchEventType type) =>
        type is MatchEventType.Gol or MatchEventType.GolTimeA or MatchEventType.GolTimeB;
}
