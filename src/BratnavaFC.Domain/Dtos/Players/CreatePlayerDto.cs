using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Players;

 public sealed record CreatePlayerDto(string Name, Guid UserId, decimal SkillPoints, Status Status);
