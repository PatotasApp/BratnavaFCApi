namespace BratnavaFC.Domain.Entities;

public enum GroupInviteStatus
{
    Pending  = 1,
    Accepted = 2,
    Rejected = 3,
}

public class GroupInviteEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public GroupEntity Group { get; private set; } = null!;

    public Guid TargetUserId { get; private set; }
    public UserEntity TargetUser { get; private set; } = null!;

    /// <summary>
    /// Player convidado (guest) que será vinculado ao usuário ao aceitar.
    /// Null = criar um novo player.
    /// </summary>
    public Guid? GuestPlayerId { get; private set; }
    public PlayerEntity? GuestPlayer { get; private set; }

    public GroupInviteStatus Status { get; private set; } = GroupInviteStatus.Pending;

    // EF
    private GroupInviteEntity() { }

    public GroupInviteEntity(Guid groupId, Guid targetUserId, Guid? guestPlayerId)
    {
        if (groupId == Guid.Empty)   throw new ArgumentException("GroupId is required.");
        if (targetUserId == Guid.Empty) throw new ArgumentException("TargetUserId is required.");

        GroupId       = groupId;
        TargetUserId  = targetUserId;
        GuestPlayerId = guestPlayerId;
        Status        = GroupInviteStatus.Pending;
    }

    public void Accept() => Status = GroupInviteStatus.Accepted;
    public void Reject() => Status = GroupInviteStatus.Rejected;
}
