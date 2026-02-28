using System;

namespace BratnavaFC.Domain.Dtos;

public sealed record MatchHistoryItemDto(
    Guid Id,
    DateTime PlayedAt,
    int TeamAGoals,
    int TeamBGoals,
    int Status,
    string StatusName,
    string? PlaceName,
    string? TeamAColorHex,
    string? TeamBColorHex
);