namespace BratnavaFC.Domain.Dtos.Players;

public record BirthdayStatusDto(
    Guid   PlayerId,
    string Name,
    bool   HasBirthday,
    string? BirthDate,   // "dd/MM/yyyy"
    int?   BirthMonth,
    int?   BirthDay
);
