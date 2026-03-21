using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Calendar;

namespace BratnavaFC.Application.Abstractions;

public interface ICalendarService
{
    Task<Result<List<CalendarEventDto>>> GetEventsAsync(Guid groupId, DateOnly start, DateOnly end, CancellationToken ct = default);

    Task<Result<CalendarEventDto>> CreateEventAsync(Guid groupId, Guid userId, CreateCalendarEventDto dto, CancellationToken ct = default);
    Task<Result<CalendarEventDto>> UpdateEventAsync(Guid groupId, Guid eventId, UpdateCalendarEventDto dto, CancellationToken ct = default);
    Task<Result> DeleteEventAsync(Guid groupId, Guid eventId, CancellationToken ct = default);

    Task<Result<List<CalendarCategoryDto>>> GetCategoriesAsync(Guid groupId, CancellationToken ct = default);
    Task<Result<CalendarCategoryDto>> CreateCategoryAsync(Guid groupId, CreateCalendarCategoryDto dto, CancellationToken ct = default);
    Task<Result<CalendarCategoryDto>> UpdateCategoryAsync(Guid groupId, Guid categoryId, UpdateCalendarCategoryDto dto, CancellationToken ct = default);
    Task<Result> DeleteCategoryAsync(Guid groupId, Guid categoryId, CancellationToken ct = default);
}
