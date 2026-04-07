namespace BratnavaFC.Domain.Entities;

public sealed class ReplayLikeEntity : BaseEntity
{
    public Guid ClipId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core
    private ReplayLikeEntity() { }

    public ReplayLikeEntity(Guid clipId, Guid userId)
    {
        ClipId    = clipId;
        UserId    = userId;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
