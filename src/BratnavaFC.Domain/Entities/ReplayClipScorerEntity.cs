namespace BratnavaFC.Domain.Entities;

/// <summary>
/// Quem fez o gol de um replay. O vídeo não carrega essa informação, então o admin informa
/// na página do telão do Puskas; um registro por clipe.
/// </summary>
public sealed class ReplayClipScorerEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public Guid ClipId { get; private set; }
    public Guid PlayerId { get; private set; }

    // EF Core
    private ReplayClipScorerEntity() { }

    public ReplayClipScorerEntity(Guid groupId, Guid clipId, Guid playerId)
    {
        GroupId = groupId;
        ClipId = clipId;
        PlayerId = playerId;
    }

    public void Reassign(Guid playerId)
    {
        PlayerId = playerId;
        UpdateDate = DateTime.UtcNow;
    }
}
