namespace BratnavaFC.Domain.Entities;

public sealed class ReplayFavoriteEntity : BaseEntity
{
    public Guid ClipId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // EF Core
    private ReplayFavoriteEntity() { }

    public ReplayFavoriteEntity(Guid clipId, Guid userId)
    {
        ClipId    = clipId;
        UserId    = userId;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
