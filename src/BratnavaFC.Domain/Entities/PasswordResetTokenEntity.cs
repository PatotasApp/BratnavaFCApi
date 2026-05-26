namespace BratnavaFC.Domain.Entities;

public sealed class PasswordResetTokenEntity : BaseEntity
{
    public Guid UserId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsUsed { get; private set; }

    // EF
    private PasswordResetTokenEntity() { }

    public PasswordResetTokenEntity(Guid userId, string code, DateTimeOffset expiresAt)
    {
        UserId = userId;
        Code = code;
        ExpiresAt = expiresAt;
        IsUsed = false;
    }

    public void MarkAsUsed() => IsUsed = true;
}
