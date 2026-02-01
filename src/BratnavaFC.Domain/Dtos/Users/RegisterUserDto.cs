namespace BratnavaFC.Domain.Dtos.Users;

public sealed record CreateUserDto(string FirstName, string LastName, string Email, string Password, string? Phone, DateTimeOffset? BirthDate);
