namespace BratnavaFC.Domain.Entities;

public sealed class ExitDebtAlertEntity : BaseEntity
{
    private ExitDebtAlertEntity() { } // EF

    public ExitDebtAlertEntity(
        Guid groupId,
        Guid playerId,
        string playerName,
        int count,
        decimal total)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId e obrigatorio.");
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId e obrigatorio.");
        if (string.IsNullOrWhiteSpace(playerName)) throw new InvalidOperationException("PlayerName e obrigatorio.");
        if (count <= 0) throw new InvalidOperationException("Count deve ser maior que zero.");
        if (total < 0) throw new InvalidOperationException("Total nao pode ser negativo.");

        GroupId = groupId;
        PlayerId = playerId;
        PlayerName = playerName.Trim();
        Count = count;
        Total = total;
    }

    public Guid GroupId { get; private set; }
    public Guid PlayerId { get; private set; }
    public string PlayerName { get; private set; } = null!;
    public int Count { get; private set; }
    public decimal Total { get; private set; }

    public DateTime? ResolvedAt { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? Resolution { get; private set; }

    public bool IsResolved => ResolvedAt.HasValue;

    public GroupEntity? Group { get; private set; }
    public PlayerEntity? Player { get; private set; }
    public UserEntity? ResolvedByUser { get; private set; }

    public void ResolveKeep(Guid userId) => Resolve(userId, "keep");

    public void ResolvePaid(Guid userId) => Resolve(userId, "mark_paid");

    private void Resolve(Guid userId, string resolution)
    {
        if (userId == Guid.Empty) throw new InvalidOperationException("UserId e obrigatorio.");
        if (IsResolved) return;

        ResolvedAt = DateTime.UtcNow;
        ResolvedByUserId = userId;
        Resolution = resolution;
        UpdateDate = DateTime.UtcNow;
    }
}
