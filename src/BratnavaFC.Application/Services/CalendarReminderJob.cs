using BratnavaFC.Application.Abstractions;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class CalendarReminderJob : ICalendarReminderJob
{
    private readonly AppDbContext _db;
    private readonly IPushService _push;
    private readonly ILogger<CalendarReminderJob> _logger;

    public CalendarReminderJob(AppDbContext db, IPushService push, ILogger<CalendarReminderJob> logger)
    {
        _db     = db;
        _push   = push;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid eventId, Guid groupId, string triggerType, CancellationToken ct = default)
    {
        var ev = await _db.CalendarEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.GroupId == groupId, ct);

        if (ev is null)
        {
            _logger.LogInformation("CalendarReminderJob ({TriggerType}): evento {EventId} não encontrado — ignorando.", triggerType, eventId);
            return;
        }

        var timeStr = ev.EventTime.HasValue
            ? $" às {ev.EventTime.Value:HH\\:mm}"
            : string.Empty;

        var (title, body) = triggerType == "24h"
            ? ("Evento amanhã! 📅",  $"O evento \"{ev.Title}\" acontece amanhã{timeStr}.")
            : ("Evento em 2 horas! 📅", $"O evento \"{ev.Title}\" começa em 2h{timeStr}.");

        await _push.SendToGroupAsync(
            groupId, title, body,
            data: new Dictionary<string, string>
            {
                ["type"]    = "event_reminder",
                ["eventId"] = eventId.ToString(),
                ["groupId"] = groupId.ToString(),
            },
            cancellationToken: ct);

        _logger.LogInformation(
            "CalendarReminderJob ({TriggerType}): lembrete enviado para grupo {GroupId}, evento \"{Title}\".",
            triggerType, groupId, ev.Title);
    }
}
