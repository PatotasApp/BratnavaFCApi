using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Groups;

public record UpdateGroupDto(string Name, DateTimeOffset? ScheduleMatchDate, Status Status);