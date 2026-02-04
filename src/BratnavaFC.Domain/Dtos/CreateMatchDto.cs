namespace BratnavaFC.Domain.Dtos;

public sealed record CreateMatchDto(
    DateTime PlayedAt,
    string PlaceName
);
