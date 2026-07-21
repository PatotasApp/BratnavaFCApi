using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Api.Controllers;

[ApiController]
[Authorize(Roles = "User,Admin,GodMode")]
[Route("api/groups/{groupId:guid}/scheduled-actions")]
public sealed class ScheduledActionsController : GroupAuthorizedController
{
    private static readonly TimeZoneInfo BrazilTimeZone = ResolveBrazilTimeZone();

    private readonly AppDbContext _db;
    private readonly IServiceProvider _services;

    public ScheduledActionsController(AppDbContext db, IServiceProvider services)
    {
        _db = db;
        _services = services;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid groupId, [FromQuery] int take = 80, CancellationToken ct = default)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct))
            return Forbid();

        take = Math.Clamp(take, 1, 200);
        var nowUtc = DateTime.UtcNow;
        var nowLocal = ToBrazilLocal(nowUtc);

        var items = new List<ScheduledActionDto>();
        items.AddRange(await BuildNotificationActionsAsync(groupId, nowUtc, ct));
        items.AddRange(await BuildConfiguredActionsAsync(groupId, nowLocal, ct));

        var ordered = items
            .OrderBy(x => x.ScheduledForUtc)
            .ThenBy(x => x.Title)
            .Take(take)
            .ToList();

        return Ok(new ApiResponse<IReadOnlyList<ScheduledActionDto>>(true, ordered, null, null, []));
    }

    [HttpDelete("{scheduledJobId:guid}")]
    public async Task<IActionResult> Cancel(Guid groupId, Guid scheduledJobId, CancellationToken ct = default)
    {
        if (!await IsAuthorizedForGroupAsync(groupId, _db, ct))
            return Forbid();

        var job = await _db.ScheduledNotificationJobs.FirstOrDefaultAsync(x => x.Id == scheduledJobId, ct);
        if (job is null)
            return NotFound(new { error = "Agendamento nao encontrado." });

        if (!await BelongsToGroupAsync(job, groupId, ct))
            return Forbid();

        var backgroundJobs = _services.GetService<IBackgroundJobClient>();
        backgroundJobs?.Delete(job.HangfireJobId);

        _db.ScheduledNotificationJobs.Remove(job);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private async Task<IReadOnlyList<ScheduledActionDto>> BuildNotificationActionsAsync(Guid groupId, DateTime nowUtc, CancellationToken ct)
    {
        var jobs = await _db.ScheduledNotificationJobs
            .AsNoTracking()
            .Where(x => x.ScheduledForUtc >= nowUtc)
            .OrderBy(x => x.ScheduledForUtc)
            .Take(300)
            .ToListAsync(ct);

        if (jobs.Count == 0)
            return [];

        var matchIds = jobs
            .Where(x => x.EntityType is "match" or "match_noquorum" or "mvp")
            .Select(x => x.EntityId)
            .Distinct()
            .ToList();

        var pollIds = jobs
            .Where(x => x.EntityType == "poll")
            .Select(x => x.EntityId)
            .Distinct()
            .ToList();

        var calendarIds = jobs
            .Where(x => x.EntityType == "calendar")
            .Select(x => x.EntityId)
            .Distinct()
            .ToList();

        var matches = await _db.Matches
            .AsNoTracking()
            .Where(x => matchIds.Contains(x.Id) && x.GroupId == groupId)
            .Select(x => new { x.Id, x.PlayedAt, x.PlaceName, x.Status })
            .ToDictionaryAsync(x => x.Id, ct);

        var polls = await _db.Polls
            .AsNoTracking()
            .Where(x => pollIds.Contains(x.Id) && x.GroupId == groupId)
            .Select(x => new { x.Id, x.Title, x.Type, x.Status })
            .ToDictionaryAsync(x => x.Id, ct);

        var events = await _db.CalendarEvents
            .AsNoTracking()
            .Where(x => calendarIds.Contains(x.Id) && x.GroupId == groupId)
            .Select(x => new { x.Id, x.Title, x.EventDate, x.EventTime })
            .ToDictionaryAsync(x => x.Id, ct);

        var result = new List<ScheduledActionDto>();
        foreach (var job in jobs)
        {
            ScheduledActionDto? dto = job.EntityType switch
            {
                "match" when matches.TryGetValue(job.EntityId, out var match) =>
                    await BuildMatchReminderAsync(job, match.PlayedAt, match.PlaceName, groupId, ct),
                "match_noquorum" when matches.TryGetValue(job.EntityId, out var match) =>
                    await BuildMatchNoQuorumAsync(job, match.PlayedAt, match.PlaceName, groupId, ct),
                "mvp" when matches.TryGetValue(job.EntityId, out var match) =>
                    await BuildMvpAsync(job, match.PlayedAt, match.PlaceName, groupId, ct),
                "poll" when polls.TryGetValue(job.EntityId, out var poll) =>
                    await BuildPollAsync(job, poll.Title, poll.Type, groupId, ct),
                "calendar" when events.TryGetValue(job.EntityId, out var ev) =>
                    await BuildCalendarAsync(job, ev.Title, groupId, ct),
                _ => null
            };

            if (dto is not null)
                result.Add(dto);
        }

        return result;
    }

    private async Task<IReadOnlyList<ScheduledActionDto>> BuildConfiguredActionsAsync(Guid groupId, DateTime nowLocal, CancellationToken ct)
    {
        var setting = await _db.GroupSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.GroupId == groupId, ct);

        if (setting is null)
            return [];

        var items = new List<ScheduledActionDto>();
        foreach (var entry in setting.GetManualMatchSchedules().Where(x => !x.Created && x.PlayedAt >= nowLocal))
        {
            var local = DateTime.SpecifyKind(entry.PlayedAt, DateTimeKind.Unspecified);
            items.Add(CreateConfiguredAction(
                id: StableGuid(groupId, $"manual-match:{local:O}"),
                entityType: "scheduled_match",
                triggerType: "manual",
                title: "Criar partida manual",
                description: $"Partida configurada para {FormatDateTime(local)} em {setting.DefaultPlaceName ?? "local padrao"}.",
                local,
                targetAudience: "Sistema",
                recipients: [],
                "Configurado"));
        }

        if (setting.MatchSchedulingEnabled && setting.MatchSchedulingMode == 1
            && setting.MatchScheduleDayOfWeek.HasValue && setting.MatchScheduleTime.HasValue)
        {
            var nextTrigger = NextLocalOccurrence(nowLocal, setting.MatchScheduleDayOfWeek.Value, setting.MatchScheduleTime.Value);
            var matchTime = ResolveRecurringMatchTime(setting, nextTrigger);
            items.Add(CreateConfiguredAction(
                id: StableGuid(groupId, $"recurring-match:{nextTrigger:O}"),
                entityType: "scheduled_match",
                triggerType: "recurring",
                title: "Criar partida recorrente",
                description: $"O sistema vai criar a proxima partida para {FormatDateTime(matchTime)} em {setting.DefaultPlaceName ?? "local padrao"}.",
                nextTrigger,
                targetAudience: "Sistema",
                recipients: [],
                "Recorrente"));
        }

        if (setting.PaymentDueDay.HasValue)
        {
            var nextPaymentReminder = NextPaymentReminderDate(nowLocal, setting.PaymentDueDay.Value);
            var recipients = await GetPendingPaymentRecipientsAsync(groupId, nextPaymentReminder.Year, nextPaymentReminder.Month, ct);
            items.Add(CreateConfiguredAction(
                id: StableGuid(groupId, $"monthly-payment:{nextPaymentReminder:yyyy-MM-dd}"),
                entityType: "monthly_payment",
                triggerType: nextPaymentReminder.Day == setting.PaymentDueDay.Value ? "due_day" : "first_of_month",
                title: "Lembrete de mensalidade",
                description: nextPaymentReminder.Day == setting.PaymentDueDay.Value
                    ? $"Aviso do dia do vencimento ({setting.PaymentDueDay.Value})."
                    : $"Aviso inicial do mes para mensalidades com vencimento no dia {setting.PaymentDueDay.Value}.",
                nextPaymentReminder.Date.AddHours(8),
                targetAudience: recipients.Count > 0 ? "Mensalistas pendentes" : "Mensalistas com pendencia no mes",
                recipients,
                "Recorrente"));
        }

        return items;
    }

    private async Task<ScheduledActionDto> BuildMatchReminderAsync(ScheduledNotificationJobEntity job, DateTime playedAt, string placeName, Guid groupId, CancellationToken ct)
    {
        var recipients = await _db.MatchPlayers
            .AsNoTracking()
            .Where(mp => mp.MatchId == job.EntityId
                         && mp.InviteResponse == InviteResponse.None
                         && mp.Player != null
                         && !mp.Player.IsGuest
                         && mp.Player.UserId != null)
            .Select(mp => new ScheduledActionRecipientDto(mp.Player!.UserId, mp.PlayerId, mp.Player!.Name, "Jogador pendente"))
            .Distinct()
            .ToListAsync(ct);

        return CreateNotificationAction(job, "Lembrete de partida", $"Partida de {FormatDateTime(playedAt)} em {placeName}.", "Jogadores pendentes", recipients);
    }

    private async Task<ScheduledActionDto> BuildMatchNoQuorumAsync(ScheduledNotificationJobEntity job, DateTime playedAt, string placeName, Guid groupId, CancellationToken ct)
    {
        var recipients = await GetGroupAdminRecipientsAsync(groupId, ct);
        return CreateNotificationAction(job, "Alerta de quorum", $"Aviso para admins se a partida de {FormatDateTime(playedAt)} em {placeName} ainda estiver sem quorum.", "Admins da patota", recipients);
    }

    private async Task<ScheduledActionDto> BuildMvpAsync(ScheduledNotificationJobEntity job, DateTime playedAt, string placeName, Guid groupId, CancellationToken ct)
    {
        if (job.TriggerType == "finalize")
            return CreateNotificationAction(job, "Finalizar MVP automaticamente", $"Fecha a votacao MVP da partida de {FormatDateTime(playedAt)}.", "Sistema", []);

        var voterIds = await _db.Votes
            .AsNoTracking()
            .Where(v => v.MatchId == job.EntityId)
            .Select(v => v.VoterId)
            .Distinct()
            .ToListAsync(ct);

        var recipients = await _db.MatchPlayers
            .AsNoTracking()
            .Where(mp => mp.MatchId == job.EntityId
                         && mp.InviteResponse == InviteResponse.Accepted
                         && !voterIds.Contains(mp.Id)
                         && mp.Player != null
                         && !mp.Player.IsGuest
                         && mp.Player.UserId != null)
            .Select(mp => new ScheduledActionRecipientDto(mp.Player!.UserId, mp.PlayerId, mp.Player!.Name, "Jogador sem voto MVP"))
            .Distinct()
            .ToListAsync(ct);

        return CreateNotificationAction(job, "Lembrete de MVP", $"Lembrete para votar no MVP da partida de {FormatDateTime(playedAt)} em {placeName}.", "Jogadores que ainda nao votaram", recipients);
    }

    private async Task<ScheduledActionDto> BuildPollAsync(ScheduledNotificationJobEntity job, string title, string type, Guid groupId, CancellationToken ct)
    {
        if (job.TriggerType == "close")
        {
            var groupRecipients = await GetGroupMemberRecipientsAsync(groupId, ct);
            return CreateNotificationAction(job, type == "event" ? "Encerrar evento" : "Encerrar votacao", $"Fecha automaticamente \"{title}\" e avisa o grupo.", "Grupo inteiro", groupRecipients);
        }

        var decidedPlayerIds = await (
            from vote in _db.PollVotes.AsNoTracking()
            join option in _db.PollOptions.AsNoTracking() on vote.OptionId equals option.Id
            where vote.PollId == job.EntityId && (type != "event" || option.Text != "Talvez")
            select vote.PlayerId
        ).Distinct().ToListAsync(ct);

        var recipients = await _db.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && p.UserId != null && !p.IsGuest && !decidedPlayerIds.Contains(p.Id))
            .Select(p => new ScheduledActionRecipientDto(p.UserId, p.Id, p.Name, type == "event" ? "Sem resposta definitiva" : "Sem voto"))
            .ToListAsync(ct);

        return CreateNotificationAction(job, type == "event" ? "Lembrete de evento" : "Lembrete de votacao", $"Lembrete para responder \"{title}\".", type == "event" ? "Membros sem Sim/Nao" : "Membros sem voto", recipients);
    }

    private async Task<ScheduledActionDto> BuildCalendarAsync(ScheduledNotificationJobEntity job, string title, Guid groupId, CancellationToken ct)
    {
        var recipients = await GetGroupMemberRecipientsAsync(groupId, ct);
        return CreateNotificationAction(job, "Lembrete de calendario", $"Evento \"{title}\".", "Grupo inteiro", recipients);
    }

    private async Task<bool> BelongsToGroupAsync(ScheduledNotificationJobEntity job, Guid groupId, CancellationToken ct)
        => job.EntityType switch
        {
            "match" or "match_noquorum" or "mvp" => await _db.Matches.AnyAsync(x => x.Id == job.EntityId && x.GroupId == groupId, ct),
            "poll" => await _db.Polls.AnyAsync(x => x.Id == job.EntityId && x.GroupId == groupId, ct),
            "calendar" => await _db.CalendarEvents.AnyAsync(x => x.Id == job.EntityId && x.GroupId == groupId, ct),
            _ => false
        };

    private async Task<List<ScheduledActionRecipientDto>> GetGroupAdminRecipientsAsync(Guid groupId, CancellationToken ct)
        => await _db.GroupAdmins
            .AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .Select(x => new ScheduledActionRecipientDto(x.UserId, null, (x.User.FirstName + " " + x.User.LastName).Trim(), "Admin"))
            .ToListAsync(ct);

    private async Task<List<ScheduledActionRecipientDto>> GetGroupMemberRecipientsAsync(Guid groupId, CancellationToken ct)
        => await _db.Players
            .AsNoTracking()
            .Where(p => p.GroupId == groupId && p.UserId != null && !p.IsGuest)
            .Select(p => new ScheduledActionRecipientDto(p.UserId, p.Id, p.Name, "Mensalista"))
            .ToListAsync(ct);

    private async Task<List<ScheduledActionRecipientDto>> GetPendingPaymentRecipientsAsync(Guid groupId, int year, int month, CancellationToken ct)
        => await _db.MonthlyPayments
            .AsNoTracking()
            .Where(mp => mp.GroupId == groupId && mp.Year == year && mp.Month == month && mp.Status == PaymentStatus.Pending)
            .Join(_db.Players.AsNoTracking(),
                payment => payment.PlayerId,
                player => player.Id,
                (payment, player) => new { payment, player })
            .Where(x => x.player.UserId != null && !x.player.IsGuest)
            .Select(x => new ScheduledActionRecipientDto(x.player.UserId, x.player.Id, x.player.Name, "Mensalidade pendente"))
            .Distinct()
            .ToListAsync(ct);

    private static ScheduledActionDto CreateNotificationAction(
        ScheduledNotificationJobEntity job,
        string title,
        string description,
        string targetAudience,
        IReadOnlyList<ScheduledActionRecipientDto> recipients)
    {
        var local = ToBrazilLocal(job.ScheduledForUtc);
        return new ScheduledActionDto(
            job.Id,
            "notification",
            job.EntityType,
            job.EntityId,
            job.TriggerType,
            title,
            description,
            EnsureUtc(job.ScheduledForUtc),
            local,
            FormatDateTime(local),
            "Agendado",
            targetAudience,
            recipients,
            recipients.Count,
            true,
            job.HangfireJobId);
    }

    private static ScheduledActionDto CreateConfiguredAction(
        Guid id,
        string entityType,
        string triggerType,
        string title,
        string description,
        DateTime local,
        string targetAudience,
        IReadOnlyList<ScheduledActionRecipientDto> recipients,
        string status)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new ScheduledActionDto(
            id,
            "configured",
            entityType,
            null,
            triggerType,
            title,
            description,
            ToUtc(local),
            local,
            FormatDateTime(local),
            status,
            targetAudience,
            recipients,
            recipients.Count,
            false,
            null);
    }

    private static DateTime NextLocalOccurrence(DateTime nowLocal, DayOfWeek dayOfWeek, TimeSpan time)
    {
        var days = ((int)dayOfWeek - (int)nowLocal.DayOfWeek + 7) % 7;
        var next = nowLocal.Date.AddDays(days).Add(time);
        return next <= nowLocal ? next.AddDays(7) : next;
    }

    private static DateTime ResolveRecurringMatchTime(GroupSettingsEntity setting, DateTime triggerLocal)
    {
        if (!setting.DefaultDayOfWeek.HasValue || !setting.DefaultKickoffTime.HasValue)
            return triggerLocal;

        var daysUntilMatch = ((int)setting.DefaultDayOfWeek.Value - (int)triggerLocal.DayOfWeek + 7) % 7;
        var playedAt = triggerLocal.Date.AddDays(daysUntilMatch).Add(setting.DefaultKickoffTime.Value);
        return playedAt < triggerLocal ? playedAt.AddDays(7) : playedAt;
    }

    private static DateTime NextPaymentReminderDate(DateTime nowLocal, int dueDay)
    {
        var candidates = new List<DateTime>();
        for (var monthOffset = 0; monthOffset <= 2; monthOffset++)
        {
            var month = new DateTime(nowLocal.Year, nowLocal.Month, 1).AddMonths(monthOffset);
            if (dueDay > 1)
                candidates.Add(month.Date);
            candidates.Add(new DateTime(month.Year, month.Month, dueDay));
        }

        return candidates
            .Select(x => x.Date.AddHours(8))
            .Where(x => x > nowLocal)
            .OrderBy(x => x)
            .First();
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime ToBrazilLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utc), BrazilTimeZone);

    private static DateTime ToUtc(DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), BrazilTimeZone);

    private static string FormatDateTime(DateTime local)
        => local.ToString("dd/MM/yyyy 'as' HH:mm");

    private static Guid StableGuid(Guid groupId, string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{groupId:N}:{value}"));
        return new Guid(bytes[..16]);
    }

    private static TimeZoneInfo ResolveBrazilTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time"); }
    }
}

public sealed record ScheduledActionDto(
    Guid Id,
    string Source,
    string EntityType,
    Guid? EntityId,
    string TriggerType,
    string Title,
    string Description,
    DateTime ScheduledForUtc,
    DateTime ScheduledForLocal,
    string ScheduledForDisplay,
    string Status,
    string TargetAudience,
    IReadOnlyList<ScheduledActionRecipientDto> Recipients,
    int RecipientCount,
    bool CanCancel,
    string? HangfireJobId);

public sealed record ScheduledActionRecipientDto(
    Guid? UserId,
    Guid? PlayerId,
    string Name,
    string Role);
