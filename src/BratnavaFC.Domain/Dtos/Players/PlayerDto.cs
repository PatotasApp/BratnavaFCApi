using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Players;

public sealed record PlayerDto(Guid Id, string Name, Guid UserId, decimal SkillPoints, bool IsGoalkeeper, Status Status);
