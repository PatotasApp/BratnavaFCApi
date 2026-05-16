namespace BratnavaFC.Domain.Dtos.Groups;

public record GroupPendingInviteAdminDto(
    Guid     Id,
    Guid     TargetUserId,
    string   TargetUserFullName,
    string   TargetUserLogin,
    DateTime CreateDate
);
