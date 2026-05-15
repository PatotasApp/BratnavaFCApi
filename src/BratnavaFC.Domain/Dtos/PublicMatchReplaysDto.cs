namespace BratnavaFC.Domain.Dtos;

public record PublicMatchReplaysDto(
    Guid MatchId,
    DateTime PlayedAt,
    string? PlaceName,
    int? TeamAGoals,
    int? TeamBGoals,
    string? TeamAColorName,
    string? TeamAColorHex,
    string? TeamBColorName,
    string? TeamBColorHex,
    List<PublicClipDto> Clips
);
