using System;

namespace BratnavaFC.Domain.Entities;

public sealed class RefreshTokenEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Token { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset Expiration { get; set; }
    public UserEntity User { get; set; }
}
