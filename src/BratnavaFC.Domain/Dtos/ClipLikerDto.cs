namespace BratnavaFC.Domain.Dtos;

public record ClipLikerDto(
    Guid           UserId,
    string         UserName,
    DateTimeOffset LikedAt
);
