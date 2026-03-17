namespace BratnavaFC.Domain.Dtos.Groups;

public record CreateGroupDto(string Name, Guid[] UserAdminIds, DateTimeOffset? ScheduleMatchDate, Guid CreatedByUserId);
