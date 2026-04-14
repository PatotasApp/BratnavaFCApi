using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Players;

public sealed record CreatePlayerDto(
    string Name,
    Guid GroupId,
    Guid? UserId,
    decimal SkillPoints,
    bool IsGoalkeeper,
    bool IsGuest,
    Status Status,
    int? GuestStarRating = null,
    int? AttackRating = null,
    int? DefenseRating = null,
    int? OverallRating = null);
