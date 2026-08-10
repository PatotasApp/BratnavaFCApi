namespace BratnavaFC.Domain.Dtos.Groups;

public record GroupInviteDto(
    Guid   Id,
    Guid   GroupId,
    string GroupName,
    Guid   TargetUserId,
    Guid?  GuestPlayerId,
    string? GuestPlayerName,
    int    Status,          // 1=Pending 2=Accepted 3=Rejected
    DateTime CreateDate,
    string? GroupLogoUrl
);
