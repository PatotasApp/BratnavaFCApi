using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Players;

public sealed record PlayerDto(
    Guid Id,
    string Name,
    Guid? UserId,
    string? UserName,
    decimal SkillPoints,
    bool IsGoalkeeper,
    bool IsGuest,
    Status Status,
    int? GuestStarRating,
    int? AttackRating,
    int? DefenseRating,
    int? OverallRating);
