namespace BratnavaFC.Domain.Dtos;

public record UpdateMatchDto(Guid? Id, DateTime PlayedAt, string PlaceName);
