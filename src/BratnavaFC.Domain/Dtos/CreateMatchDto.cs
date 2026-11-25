namespace BratnavaFC.Domain.Dtos;

public sealed record CreateMatchDto(
    DateTime PlayedAt,
    string PlaceName,
    string? PlaceAddress = null,
    double? PlaceLatitude = null,
    double? PlaceLongitude = null
);
