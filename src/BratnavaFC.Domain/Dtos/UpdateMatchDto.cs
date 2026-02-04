namespace BratnavaFC.Domain.Dtos;

public sealed record UpdateMatchDto(
    Guid? Id,
    DateTime PlayedAt,
    string PlaceName
);
