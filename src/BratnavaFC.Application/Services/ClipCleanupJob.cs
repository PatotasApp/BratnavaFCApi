using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Cloudflare;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class ClipCleanupJob : IClipCleanupJob
{
    private readonly AppDbContext _db;
    private readonly IReplayUrlService _r2;
    private readonly ILogger<ClipCleanupJob> _logger;

    public ClipCleanupJob(
        AppDbContext db,
        IReplayUrlService r2,
        ILogger<ClipCleanupJob> logger)
    {
        _db     = db;
        _r2     = r2;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ClipCleanupJob: starting");

        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);

        var candidates = await _db.ReplayClips
            .Where(c =>
                c.RecordedAt < cutoff &&
                !_db.ReplayLikes.Any(l => l.ClipId == c.Id) &&
                !_db.ReplayFavorites.Any(f => f.ClipId == c.Id))
            .ToListAsync(ct);

        _logger.LogInformation(
            "ClipCleanupJob: {Count} clip(s) eligible for deletion", candidates.Count);

        var deleted = new List<ReplayClipEntity>(candidates.Count);

        foreach (var clip in candidates)
        {
            try
            {
                await _r2.DeleteObjectAsync(clip.ObjectKey, ct);
                deleted.Add(clip);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ClipCleanupJob: failed to delete R2 object for clip {ClipId} ({ObjectKey})",
                    clip.Id, clip.ObjectKey);
            }
        }

        if (deleted.Count > 0)
        {
            _db.ReplayClips.RemoveRange(deleted);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "ClipCleanupJob: completed — deleted {Deleted}/{Total} clip(s)",
            deleted.Count, candidates.Count);
    }
}
