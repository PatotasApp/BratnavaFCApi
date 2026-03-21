using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Calendar;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class CalendarService : ICalendarService
{
    private static readonly CalendarEventDtoComparer EventComparer = new();

    /// <summary>
    /// Fuso horário de Brasília (UTC-3 / UTC-2 no horário de verão).
    /// Tenta o ID do Windows primeiro, depois o IANA (Linux/macOS/Docker).
    /// </summary>
    private static readonly TimeZoneInfo BrazilTz = ResolveBrazilTimeZone();

    private static TimeZoneInfo ResolveBrazilTimeZone()
    {
        foreach (var id in new[] { "E. South America Standard Time", "America/Sao_Paulo" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        // Fallback: UTC-3 fixo (sem horário de verão)
        return TimeZoneInfo.CreateCustomTimeZone(
            "BRT", TimeSpan.FromHours(-3), "Brasília Time", "BRT");
    }

    private readonly AppDbContext _context;
    private readonly IHolidayService _holidays;

    public CalendarService(AppDbContext context, IHolidayService holidays)
    {
        _context = context;
        _holidays = holidays;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Result<List<CalendarEventDto>>> GetEventsAsync(
        Guid groupId,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<List<CalendarEventDto>>.Fail(groupCheck.Error!, groupCheck.Status);

        if (start > end)
            return Result<List<CalendarEventDto>>.Fail("A data inicial não pode ser maior que a data final.", ResultStatus.BadRequest);

        var (startUtc, endUtc) = BuildUtcRange(start, end);

        // ── DbContext não é thread-safe: queries do banco devem ser sequenciais ──

        var manualEvents = await _context.CalendarEvents
            .AsNoTracking()
            .Where(e => e.GroupId == groupId && e.EventDate >= start && e.EventDate <= end)
            .Select(e => new CalendarEventDto
            {
                Id = e.Id,
                Type = CalendarEventTypes.Manual,
                Title = e.Title,
                Date = e.EventDate.ToString("yyyy-MM-dd"),
                Time = e.EventTime.HasValue ? e.EventTime.Value.ToString("HH:mm") : null,
                TimeTBD = e.TimeTBD,
                CategoryId = e.CategoryId,
                CategoryName = e.Category != null ? e.Category.Name : null,
                CategoryColor = e.Category != null ? e.Category.Color : null,
                CategoryIcon = e.Category != null ? e.Category.Icon : null,
                Description = e.Description,
                SourceId = null
            })
            .ToListAsync(ct);

        var birthdays = await _context.Players
            .AsNoTracking()
            .Where(p =>
                p.GroupId == groupId &&
                p.Status == Status.Active &&
                !p.IsGuest &&
                p.UserId != null &&
                p.User != null &&
                p.User.BirthDate != null)
            .Select(p => new BirthdayProjection
            {
                PlayerId = p.Id,
                PlayerName = p.Name,
                BirthDate = p.User!.BirthDate!.Value
            })
            .ToListAsync(ct);

        var matches = await _context.Matches
            .AsNoTracking()
            .Where(m =>
                m.GroupId == groupId &&
                m.PlayedAt >= startUtc &&
                m.PlayedAt <= endUtc)
            .Select(m => new MatchProjection
            {
                MatchId = m.Id,
                PlayedAt = m.PlayedAt,
                PlaceName = m.PlaceName
            })
            .ToListAsync(ct);

        // Feriados nacionais: chamadas HTTP externas, podem correr em paralelo
        var years = Enumerable.Range(start.Year, end.Year - start.Year + 1);
        var holidayTasks = years
            .Select(y => _holidays.GetHolidaysAsync(y, ct))
            .ToArray();

        await Task.WhenAll(holidayTasks);

        var allHolidays = holidayTasks.SelectMany(t => t.Result.Data ?? Array.Empty<HolidayDto>()).ToArray();

        var result = new List<CalendarEventDto>(
            manualEvents.Count +
            birthdays.Count +
            matches.Count +
            allHolidays.Length);

        result.AddRange(manualEvents);
        result.AddRange(BuildBirthdayEvents(birthdays, start, end));
        result.AddRange(BuildMatchEvents(matches, start, end));
        result.AddRange(BuildHolidayEvents(allHolidays, start, end));

        result.Sort(EventComparer);
        return Result<List<CalendarEventDto>>.Ok(result);
    }

    public async Task<Result<CalendarEventDto>> CreateEventAsync(
        Guid groupId,
        Guid userId,
        CreateCalendarEventDto dto,
        CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<CalendarEventDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var dateResult = ParseRequiredDate(dto.Date);
        if (!dateResult.Success) return Result<CalendarEventDto>.Fail(dateResult.Error!, dateResult.Status);
        var eventDate = dateResult.Data!;

        var timeResult = ParseEventTime(dto.Time, dto.TimeTBD);
        if (!timeResult.Success) return Result<CalendarEventDto>.Fail(timeResult.Error!, timeResult.Status);
        var (eventTime, timeTbd) = timeResult.Data!;

        CalendarCategoryEntity? category = null;
        if (dto.CategoryId.HasValue)
        {
            var categoryResult = await GetCategoryOrFailAsync(groupId, dto.CategoryId.Value, ct);
            if (!categoryResult.Success) return Result<CalendarEventDto>.Fail(categoryResult.Error!, categoryResult.Status);
            category = categoryResult.Data!;
        }

        var ev = new CalendarEventEntity(
            groupId,
            dto.Title,
            dto.Description,
            dto.CategoryId,
            eventDate,
            eventTime,
            timeTbd,
            userId);

        _context.CalendarEvents.Add(ev);
        await _context.SaveChangesAsync(ct);

        return Result<CalendarEventDto>.Ok(MapEventToDto(ev, category), "Evento criado com sucesso.", ResultStatus.Created);
    }

    public async Task<Result<CalendarEventDto>> UpdateEventAsync(
        Guid groupId,
        Guid eventId,
        UpdateCalendarEventDto dto,
        CancellationToken ct = default)
    {
        var ev = await _context.CalendarEvents
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.GroupId == groupId, ct);

        if (ev is null)
            return Result<CalendarEventDto>.Fail("Evento não encontrado.", ResultStatus.NotFound);

        DateOnly? newDate = null;
        if (dto.Date is not null)
        {
            var dateResult = ParseRequiredDate(dto.Date);
            if (!dateResult.Success) return Result<CalendarEventDto>.Fail(dateResult.Error!, dateResult.Status);
            newDate = dateResult.Data!;
        }

        var effectiveTimeTbd = dto.TimeTBD ?? ev.TimeTBD;
        var timeResult = ResolveUpdatedTime(ev, dto, effectiveTimeTbd);
        if (!timeResult.Success) return Result<CalendarEventDto>.Fail(timeResult.Error!, timeResult.Status);
        var effectiveTime = timeResult.Data;

        Guid? newCategoryId = dto.CategoryId ?? ev.CategoryId;
        CalendarCategoryEntity? category = ev.Category;

        if (dto.CategoryId.HasValue)
        {
            var categoryResult = await GetCategoryOrFailAsync(groupId, dto.CategoryId.Value, ct);
            if (!categoryResult.Success) return Result<CalendarEventDto>.Fail(categoryResult.Error!, categoryResult.Status);
            category = categoryResult.Data!;
        }

        ev.Update(
            dto.Title,
            dto.Description,
            newCategoryId,
            newDate,
            effectiveTime,
            dto.TimeTBD);

        await _context.SaveChangesAsync(ct);

        return Result<CalendarEventDto>.Ok(MapEventToDto(ev, category), "Evento atualizado com sucesso.");
    }

    public async Task<Result> DeleteEventAsync(
        Guid groupId,
        Guid eventId,
        CancellationToken ct = default)
    {
        var ev = await _context.CalendarEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.GroupId == groupId, ct);

        if (ev is null)
            return Result.Ok("Evento removido com sucesso.");

        _context.CalendarEvents.Remove(ev);
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Evento removido com sucesso.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Categories
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Result<List<CalendarCategoryDto>>> GetCategoriesAsync(
        Guid groupId,
        CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<List<CalendarCategoryDto>>.Fail(groupCheck.Error!, groupCheck.Status);

        var categories = await _context.CalendarCategories
            .AsNoTracking()
            .Where(c => c.GroupId == groupId)
            .OrderBy(c => c.Name)
            .Select(c => MapCategoryToDto(c))
            .ToListAsync(ct);

        return Result<List<CalendarCategoryDto>>.Ok(categories);
    }

    public async Task<Result<CalendarCategoryDto>> CreateCategoryAsync(
        Guid groupId,
        CreateCalendarCategoryDto dto,
        CancellationToken ct = default)
    {
        var groupCheck = await EnsureGroupExistsAsync(groupId, ct);
        if (!groupCheck.Success) return Result<CalendarCategoryDto>.Fail(groupCheck.Error!, groupCheck.Status);

        var category = new CalendarCategoryEntity(groupId, dto.Name, dto.Color, dto.Icon);

        _context.CalendarCategories.Add(category);
        await _context.SaveChangesAsync(ct);

        return Result<CalendarCategoryDto>.Ok(MapCategoryToDto(category), "Categoria criada com sucesso.", ResultStatus.Created);
    }

    public async Task<Result<CalendarCategoryDto>> UpdateCategoryAsync(
        Guid groupId,
        Guid categoryId,
        UpdateCalendarCategoryDto dto,
        CancellationToken ct = default)
    {
        var category = await _context.CalendarCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.GroupId == groupId, ct);

        if (category is null)
            return Result<CalendarCategoryDto>.Fail("Categoria não encontrada.", ResultStatus.NotFound);

        if (dto.Name is not null)
            category.Rename(dto.Name);

        if (dto.Color is not null)
            category.SetColor(dto.Color);

        if (dto.Icon is not null)
            category.SetIcon(dto.Icon);

        await _context.SaveChangesAsync(ct);

        return Result<CalendarCategoryDto>.Ok(MapCategoryToDto(category), "Categoria atualizada com sucesso.");
    }

    public async Task<Result> DeleteCategoryAsync(
        Guid groupId,
        Guid categoryId,
        CancellationToken ct = default)
    {
        var category = await _context.CalendarCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.GroupId == groupId, ct);

        if (category is null)
            return Result.Ok("Categoria removida com sucesso.");

        if (category.IsSystem)
            return Result.Fail("Categorias do sistema não podem ser excluídas.", ResultStatus.BadRequest);

        _context.CalendarCategories.Remove(category);
        await _context.SaveChangesAsync(ct);

        return Result.Ok("Categoria removida com sucesso.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Result> EnsureGroupExistsAsync(Guid groupId, CancellationToken ct)
    {
        if (groupId == Guid.Empty)
            return Result.Fail("GroupId inválido.", ResultStatus.BadRequest);

        var exists = await _context.Groups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId, ct);

        if (!exists)
            return Result.Fail("Grupo não encontrado.", ResultStatus.NotFound);

        return Result.Ok();
    }

    private async Task<Result<CalendarCategoryEntity>> GetCategoryOrFailAsync(
        Guid groupId,
        Guid categoryId,
        CancellationToken ct)
    {
        var category = await _context.CalendarCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.GroupId == groupId, ct);

        if (category is null)
            return Result<CalendarCategoryEntity>.Fail("Categoria não encontrada.", ResultStatus.NotFound);

        return Result<CalendarCategoryEntity>.Ok(category);
    }

    private static Result<DateOnly> ParseRequiredDate(string date)
    {
        if (!DateOnly.TryParse(date, out var parsed))
            return Result<DateOnly>.Fail("Data inválida. Use o formato YYYY-MM-DD.", ResultStatus.BadRequest);

        return Result<DateOnly>.Ok(parsed);
    }

    private static Result<(TimeOnly? Time, bool TimeTbd)> ParseEventTime(string? time, bool timeTbd)
    {
        if (timeTbd)
            return Result<(TimeOnly? Time, bool TimeTbd)>.Ok((null, true));

        if (string.IsNullOrWhiteSpace(time))
            return Result<(TimeOnly? Time, bool TimeTbd)>.Ok((null, false));

        if (!TimeOnly.TryParse(time, out var parsed))
            return Result<(TimeOnly? Time, bool TimeTbd)>.Fail("Horário inválido. Use o formato HH:mm.", ResultStatus.BadRequest);

        return Result<(TimeOnly? Time, bool TimeTbd)>.Ok((parsed, false));
    }

    private static Result<TimeOnly?> ResolveUpdatedTime(
        CalendarEventEntity currentEvent,
        UpdateCalendarEventDto dto,
        bool effectiveTimeTbd)
    {
        if (effectiveTimeTbd)
            return Result<TimeOnly?>.Ok(null);

        if (dto.Time is not null)
        {
            if (!TimeOnly.TryParse(dto.Time, out var parsed))
                return Result<TimeOnly?>.Fail("Horário inválido. Use o formato HH:mm.", ResultStatus.BadRequest);

            return Result<TimeOnly?>.Ok(parsed);
        }

        return Result<TimeOnly?>.Ok(currentEvent.EventTime);
    }

    private static (DateTime StartUtc, DateTime EndUtc) BuildUtcRange(DateOnly start, DateOnly end)
    {
        // Expande 4h em cada lado para cobrir a diferença de fuso do Brasil (UTC-3 / UTC-2).
        // BuildMatchEvents filtra em memória pelo horário local após a conversão.
        var startRaw = start.ToDateTime(TimeOnly.MinValue).AddHours(-4);
        var endRaw   = end.ToDateTime(TimeOnly.MaxValue).AddHours(4);
        return (
            DateTime.SpecifyKind(startRaw, DateTimeKind.Utc),
            DateTime.SpecifyKind(endRaw,   DateTimeKind.Utc)
        );
    }

    private static IEnumerable<CalendarEventDto> BuildBirthdayEvents(
        IReadOnlyCollection<BirthdayProjection> birthdays,
        DateOnly start,
        DateOnly end)
    {
        if (birthdays.Count == 0)
            yield break;

        for (var year = start.Year; year <= end.Year; year++)
        {
            foreach (var player in birthdays)
            {
                var birthDate = player.BirthDate;

                if (!IsValidDate(year, birthDate.Month, birthDate.Day))
                    continue;

                var birthdayDate = new DateOnly(year, birthDate.Month, birthDate.Day);

                if (birthdayDate < start || birthdayDate > end)
                    continue;

                yield return new CalendarEventDto
                {
                    Id = null,
                    Type = CalendarEventTypes.Birthday,
                    Title = player.PlayerName,
                    Date = birthdayDate.ToString("yyyy-MM-dd"),
                    Time = null,
                    TimeTBD = true,
                    CategoryId = null,
                    CategoryName = "Aniversário",
                    CategoryColor = "#ec4899",
                    CategoryIcon = "🎂",
                    SourceId = player.PlayerId,
                    Description = null
                };
            }
        }
    }

    private static IEnumerable<CalendarEventDto> BuildMatchEvents(
        IReadOnlyCollection<MatchProjection> matches,
        DateOnly start,
        DateOnly end)
    {
        foreach (var match in matches)
        {
            // PlayedAt é UTC (Npgsql). Converte para horário de Brasília antes de exibir.
            var localDt   = TimeZoneInfo.ConvertTimeFromUtc(match.PlayedAt, BrazilTz);
            var matchDate = DateOnly.FromDateTime(localDt);

            // O range UTC foi expandido para não perder jogos na virada do dia;
            // filtramos aqui pelo intervalo de datas locais solicitado.
            if (matchDate < start || matchDate > end)
                continue;

            yield return new CalendarEventDto
            {
                Id = null,
                Type = CalendarEventTypes.Match,
                Title = string.IsNullOrWhiteSpace(match.PlaceName) ? "Jogo" : match.PlaceName,
                Date = matchDate.ToString("yyyy-MM-dd"),
                Time = localDt.ToString("HH:mm"),
                TimeTBD = false,
                CategoryId = null,
                CategoryName = "Jogo",
                CategoryColor = "#22c55e",
                CategoryIcon = "⚽",
                SourceId = match.MatchId,
                Description = null
            };
        }
    }

    private static IEnumerable<CalendarEventDto> BuildHolidayEvents(
        IReadOnlyCollection<HolidayDto> holidays,
        DateOnly start,
        DateOnly end)
    {
        foreach (var h in holidays)
        {
            if (!DateOnly.TryParse(h.Date, out var date)) continue;
            if (date < start || date > end) continue;

            yield return new CalendarEventDto
            {
                Id            = null,
                Type          = CalendarEventTypes.Holiday,
                Title         = h.Name,
                Date          = date.ToString("yyyy-MM-dd"),
                Time          = null,
                TimeTBD       = true,
                CategoryId    = null,
                CategoryName  = "Feriado",
                CategoryColor = "#f59e0b",
                CategoryIcon  = "🎉",
                SourceId      = null,
                Description   = null,
            };
        }
    }

    private static bool IsValidDate(int year, int month, int day)
    {
        try
        {
            _ = new DateOnly(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static CalendarEventDto MapEventToDto(CalendarEventEntity ev, CalendarCategoryEntity? category)
    {
        return new CalendarEventDto
        {
            Id = ev.Id,
            Type = CalendarEventTypes.Manual,
            Title = ev.Title,
            Date = ev.EventDate.ToString("yyyy-MM-dd"),
            Time = ev.EventTime.HasValue ? ev.EventTime.Value.ToString("HH:mm") : null,
            TimeTBD = ev.TimeTBD,
            CategoryId = ev.CategoryId,
            CategoryName = category?.Name,
            CategoryColor = category?.Color,
            CategoryIcon = category?.Icon,
            Description = ev.Description,
            SourceId = null
        };
    }

    private static CalendarCategoryDto MapCategoryToDto(CalendarCategoryEntity category)
    {
        return new CalendarCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Color = category.Color,
            Icon = category.Icon,
            IsSystem = category.IsSystem
        };
    }

    private static class CalendarEventTypes
    {
        public const string Manual   = "manual";
        public const string Birthday = "birthday";
        public const string Match    = "match";
        public const string Holiday  = "holiday";
    }

    private sealed class CalendarEventDtoComparer : IComparer<CalendarEventDto>
    {
        public int Compare(CalendarEventDto? x, CalendarEventDto? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var dateComparison = string.CompareOrdinal(x.Date, y.Date);
            if (dateComparison != 0) return dateComparison;

            var xTime = x.TimeTBD ? "99:99" : (x.Time ?? "99:99");
            var yTime = y.TimeTBD ? "99:99" : (y.Time ?? "99:99");

            var timeComparison = string.CompareOrdinal(xTime, yTime);
            if (timeComparison != 0) return timeComparison;

            return string.Compare(x.Title, y.Title, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class BirthdayProjection
    {
        public Guid PlayerId { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        /// <summary>
        /// Mantém o tipo nativo do banco (DateTimeOffset) para que o EF Core
        /// possa projetar sem conversão. Apenas Month e Day são usados.
        /// </summary>
        public DateTimeOffset BirthDate { get; init; }
    }

    private sealed class MatchProjection
    {
        public Guid MatchId { get; init; }
        public DateTime PlayedAt { get; init; }
        public string? PlaceName { get; init; }
    }
}
