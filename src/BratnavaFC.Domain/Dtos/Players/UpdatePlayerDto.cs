using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Players;

public sealed record UpdatePlayerDto(string Name, Guid GroupId, decimal SkillPoints, bool IsGoalkeeper, bool IsGuest, Status Status);
