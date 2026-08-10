using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Users;

public sealed class UserDto
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }

    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public DateTimeOffset? BirthDate { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTimeOffset? PhotoUpdatedAt { get; set; }

    public UserRole Role { get; set; }
    public Status Status { get; set; }

    public DateTimeOffset CreateDate { get; set; }
    public DateTimeOffset? UpdateDate { get; set; }
    public DateTime? InactivatedAt { get; set; }

    public Guid[] PlayerIds { get; set; } = Array.Empty<Guid>();
    public Guid[] GroupAdminIds { get; set; } = Array.Empty<Guid>();
}
