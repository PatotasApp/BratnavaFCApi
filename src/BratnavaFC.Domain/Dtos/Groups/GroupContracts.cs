using System;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Groups;

public static class GroupContracts
{
    public record CreateGroupRequest(string Name, Guid UserAdminId, DateTimeOffset? ScheduleMatchDate);
    public record UpdateGroupRequest(Guid Id, string Name, DateTimeOffset? ScheduleMatchDate, Status Status);
    public record DeleteGroupRequest(Guid GroupId);
    public sealed record GetResponse(Guid Id, string Name, DateTimeOffset? ScheduleMatchDate, Guid AdminId, Status Status, List<PlayerDto> Players);
}
