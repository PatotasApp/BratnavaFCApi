namespace BratnavaFC.Domain.Entities;

public enum MatchApplicationStatus { PendingAdmin = 1, PendingPlayer = 2, Accepted = 3, Rejected = 4, Cancelled = 5 }
public enum MatchApplicationSource { PlayerRequest = 1, AdminInvite = 2 }
public enum GuestOrigin { Manual = 1, ExternalPlayer = 2 }
public enum MatchOpeningPosition { Any = 1, Line = 2, Goalkeeper = 3 }

public sealed class MatchApplicationEntity : BaseEntity
{
    private MatchApplicationEntity() { }

    public MatchApplicationEntity(Guid groupId, Guid matchId, Guid userId, MatchApplicationSource source,
        Guid? invitedByUserId = null, Guid? openingId = null, string? message = null)
    {
        if (groupId == Guid.Empty || matchId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Group, match and user are required.");
        GroupId = groupId;
        MatchId = matchId;
        UserId = userId;
        Source = source;
        InvitedByUserId = invitedByUserId;
        OpeningId = openingId;
        Message = NormalizeText(message);
        Status = source == MatchApplicationSource.PlayerRequest
            ? MatchApplicationStatus.PendingAdmin
            : MatchApplicationStatus.PendingPlayer;
    }

    public Guid GroupId { get; private set; }
    public GroupEntity Group { get; private set; } = null!;
    public Guid MatchId { get; private set; }
    public MatchEntity Match { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public UserEntity User { get; private set; } = null!;
    public Guid? InvitedByUserId { get; private set; }
    public Guid? OpeningId { get; private set; }
    public MatchOpeningEntity? Opening { get; private set; }
    public Guid? GuestPlayerId { get; private set; }
    public PlayerEntity? GuestPlayer { get; private set; }
    public MatchApplicationSource Source { get; private set; }
    public MatchApplicationStatus Status { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    public string? Message { get; private set; }
    public string? DecisionReason { get; private set; }

    public void ApproveByAdmin(Guid? guestPlayerId, string? reason = null)
    {
        if (Status != MatchApplicationStatus.PendingAdmin) throw new InvalidOperationException("Application is not pending admin approval.");
        GuestPlayerId = guestPlayerId;
        DecisionReason = NormalizeText(reason);
        Status = MatchApplicationStatus.PendingPlayer;
        RespondedAt = DateTimeOffset.UtcNow;
    }

    public void SelectGuest(Guid? guestPlayerId) => GuestPlayerId = guestPlayerId;

    public void AcceptByPlayer(Guid guestPlayerId)
    {
        if (Status != MatchApplicationStatus.PendingPlayer) throw new InvalidOperationException("Invitation is not pending player confirmation.");
        GuestPlayerId = guestPlayerId;
        Status = MatchApplicationStatus.Accepted;
        RespondedAt = DateTimeOffset.UtcNow;
    }

    public void Reject(string? reason = null) { DecisionReason = NormalizeText(reason); Status = MatchApplicationStatus.Rejected; RespondedAt = DateTimeOffset.UtcNow; }
    public void Cancel() { Status = MatchApplicationStatus.Cancelled; RespondedAt = DateTimeOffset.UtcNow; }

    private static string? NormalizeText(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > 500) throw new InvalidOperationException("A mensagem deve ter no máximo 500 caracteres.");
        return text;
    }
}


public sealed class MatchOpeningEntity : BaseEntity
{
    private MatchOpeningEntity() { }

    public MatchOpeningEntity(Guid groupId, Guid matchId, int slots, MatchOpeningPosition position)
    {
        if (groupId == Guid.Empty || matchId == Guid.Empty) throw new ArgumentException("Group and match are required.");
        Update(slots, position);
        GroupId = groupId;
        MatchId = matchId;
    }

    public Guid GroupId { get; private set; }
    public GroupEntity Group { get; private set; } = null!;
    public Guid MatchId { get; private set; }
    public MatchEntity Match { get; private set; } = null!;
    public int Slots { get; private set; }
    public MatchOpeningPosition Position { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(int slots, MatchOpeningPosition position)
    {
        if (slots < 1 || slots > 99) throw new InvalidOperationException("Open slots must be between 1 and 99.");
        Slots = slots;
        Position = position;
        UpdateDate = DateTime.UtcNow;
    }

    public void Close() { IsActive = false; UpdateDate = DateTime.UtcNow; }
}
