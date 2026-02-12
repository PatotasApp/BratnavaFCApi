using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Users;

public record UserDto
{
    public Guid Id { get; set; }
    public Guid[] GroupAdminIds { get; set; } = [];
    public Guid[] PlayerIds { get; set; } = [];
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public DateTimeOffset? BirthDate { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public Status Status { get; set; }
}
