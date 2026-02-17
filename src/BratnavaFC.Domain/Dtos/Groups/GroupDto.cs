using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Groups;


public record GroupDto(Guid Id, string Name, DateTimeOffset? ScheduleMatchDate, Guid[] AdminIds, Status Status, List<PlayerDto> Players);