using System;
using BratnavaFC.Domain.Dtos.Players;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Groups;

public static class GroupContracts
{
    public record CreateGroupRequest(string Name, Guid[] UserAdminIds, DateTimeOffset? ScheduleMatchDate);
    public record UpdateGroupRequest(string Name, DateTimeOffset? ScheduleMatchDate, Status Status);
    public record DeleteGroupRequest(Guid GroupId);
    public sealed record GetResponse(Guid Id, string Name, DateTimeOffset? ScheduleMatchDate, Guid[] AdminIds, Status Status, List<PlayerDto> Players);
}
