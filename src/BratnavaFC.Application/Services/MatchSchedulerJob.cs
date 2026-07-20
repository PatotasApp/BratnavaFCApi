using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Time;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class MatchSchedulerJob : IMatchSchedulerJob
{
    private static readonly TimeSpan RecurringWindow = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly IMatchService _matches;
    private readonly ILogger<MatchSchedulerJob> _logger;

    public MatchSchedulerJob(AppDbContext db, IMatchService matches, ILogger<MatchSchedulerJob> logger)
    {
        _db = db;
        _matches = matches;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = BratnavaDateTime.UtcToSaoPauloLocal(DateTime.UtcNow);
        var settings = await _db.GroupSettings
            .Where(x => x.MatchSchedulingEnabled)
            .ToListAsync(ct);

        foreach (var setting in settings)
        {
            if (setting.MatchSchedulingMode == 0)
            {
                await ProcessManualAsync(setting, now, ct);
                continue;
            }

            await ProcessRecurringAsync(setting, now, ct);
        }
    }

    private async Task ProcessManualAsync(GroupSettingsEntity setting, DateTime now, CancellationToken ct)
    {
        var entries = setting.GetManualMatchSchedules().ToList();
        var changed = false;

        foreach (var entry in entries.Where(x => !x.Created && AsBrazilLocal(x.PlayedAt) <= now))
        {
            var matchId = await CreateMatchIfMissingAsync(setting, BrazilLocalToUtc(entry.PlayedAt), ct);
            if (!matchId.HasValue) continue;

            entry.Created = true;
            entry.MatchId = matchId.Value;
            changed = true;
        }

        if (!changed) return;

        setting.SetMatchScheduling(
            setting.MatchSchedulingEnabled,
            setting.MatchSchedulingMode,
            setting.MatchScheduleDayOfWeek,
            setting.MatchScheduleTime,
            entries);

        await _db.SaveChangesAsync(ct);
    }

    private async Task ProcessRecurringAsync(GroupSettingsEntity setting, DateTime now, CancellationToken ct)
    {
        if (!setting.MatchScheduleDayOfWeek.HasValue || !setting.MatchScheduleTime.HasValue)
            return;

        if (now.DayOfWeek != setting.MatchScheduleDayOfWeek.Value)
            return;

        var scheduledTrigger = now.Date.Add(setting.MatchScheduleTime.Value);
        if (now < scheduledTrigger || now >= scheduledTrigger.Add(RecurringWindow))
            return;

        var playedAt = ResolveRecurringMatchTime(setting, now);
        await CreateMatchIfMissingAsync(setting, playedAt, ct);
    }

    private static DateTime ResolveRecurringMatchTime(GroupSettingsEntity setting, DateTime now)
    {
        if (!setting.DefaultDayOfWeek.HasValue || !setting.DefaultKickoffTime.HasValue)
            return BrazilLocalToUtc(now.Date.Add(setting.MatchScheduleTime!.Value));

        var daysUntilMatch = ((int)setting.DefaultDayOfWeek.Value - (int)now.DayOfWeek + 7) % 7;
        var date = now.Date.AddDays(daysUntilMatch);
        var playedAt = date.Add(setting.DefaultKickoffTime.Value);

        if (playedAt < now)
            playedAt = playedAt.AddDays(7);

        return BrazilLocalToUtc(playedAt);
    }

    private async Task<Guid?> CreateMatchIfMissingAsync(GroupSettingsEntity setting, DateTime playedAt, CancellationToken ct)
    {
        playedAt = EnsureUtc(playedAt);

        var exists = await _db.Matches
            .AsNoTracking()
            .AnyAsync(m =>
                m.GroupId == setting.GroupId &&
                m.PlayedAt == playedAt,
                ct);

        if (exists)
            return null;

        var placeName = string.IsNullOrWhiteSpace(setting.DefaultPlaceName)
            ? "Partida"
            : setting.DefaultPlaceName;

        var result = await _matches.Create(setting.GroupId, new MatchEntity(setting.GroupId, playedAt, placeName), ct);
        if (result.Success)
            return result.Data!.Id;

        _logger.LogWarning(
            "Nao foi possivel criar partida agendada para o grupo {GroupId}: {Error}",
            setting.GroupId,
            result.Error);

        return null;
    }

    private static DateTime AsBrazilLocal(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        return BratnavaDateTime.UtcToSaoPauloLocal(value);
    }

    internal static DateTime BrazilLocalToUtc(DateTime local)
        => BratnavaDateTime.SaoPauloLocalToUtc(local);

    private static DateTime EnsureUtc(DateTime value)
        => BratnavaDateTime.EnsureUtc(value);
}
