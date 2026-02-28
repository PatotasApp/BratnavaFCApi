namespace BratnavaFC.Domain.Dtos.Groups;

public record CreateGroupInviteDto(Guid TargetUserId, Guid? GuestPlayerId);
