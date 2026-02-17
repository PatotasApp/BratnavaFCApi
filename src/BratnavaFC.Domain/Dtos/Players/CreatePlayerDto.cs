using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Players;

 public sealed record CreatePlayerDto(string Name, Guid GroupId, Guid UserId, decimal SkillPoints, bool IsGoalkeeper, Status Status);