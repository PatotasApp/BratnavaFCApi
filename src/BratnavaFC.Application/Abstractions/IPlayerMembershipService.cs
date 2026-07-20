using BratnavaFC.Domain.Common;

namespace BratnavaFC.Application.Abstractions;

public enum PlayerUnlinkReason
{
    SelfLeave = 1,
    AdminRemove = 2,
    AccountDeletion = 3
}

public sealed record PlayerUnlinkResult(
    Guid PlayerId,
    Guid GroupId,
    string PlayerName,
    Guid? RemovedUserId);

public interface IPlayerMembershipService
{
    Task<Result<PlayerUnlinkResult>> UnlinkPlayerFromGroupAsync(
        Guid playerId,
        PlayerUnlinkReason reason,
        Guid? requestingUserId,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<PlayerUnlinkResult>>> UnlinkUserFromAllGroupsAsync(
        Guid userId,
        PlayerUnlinkReason reason,
        CancellationToken cancellationToken);
}
