using BratnavaFC.Domain.Dtos.Calendar;

namespace BratnavaFC.Application.Abstractions;

public interface ICalendarService
{
    Task<List<CalendarEventDto>> GetEventsAsync(Guid groupId, DateOnly start, DateOnly end, CancellationToken ct = default);

    Task<CalendarEventDto> CreateEventAsync(Guid groupId, Guid userId, CreateCalendarEventDto dto, CancellationToken ct = default);
    Task<CalendarEventDto> UpdateEventAsync(Guid groupId, Guid eventId, UpdateCalendarEventDto dto, CancellationToken ct = default);
    Task DeleteEventAsync(Guid groupId, Guid eventId, CancellationToken ct = default);

    Task<List<CalendarCategoryDto>> GetCategoriesAsync(Guid groupId, CancellationToken ct = default);
    Task<CalendarCategoryDto> CreateCategoryAsync(Guid groupId, CreateCalendarCategoryDto dto, CancellationToken ct = default);
    Task<CalendarCategoryDto> UpdateCategoryAsync(Guid groupId, Guid categoryId, UpdateCalendarCategoryDto dto, CancellationToken ct = default);
    Task DeleteCategoryAsync(Guid groupId, Guid categoryId, CancellationToken ct = default);
}
