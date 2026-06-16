using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Notifications;

namespace BratnavaFC.Application.Abstractions;

public interface INotificationService
{
    Task<Result<NotificationPageDto>> GetMineAsync(
        Guid userId, Guid? groupId, int page, int pageSize, CancellationToken ct);

    Task<Result<int>> GetUnreadCountAsync(
        Guid userId, Guid? groupId, CancellationToken ct);

    Task<Result> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken ct);

    Task<Result> MarkAllReadAsync(Guid userId, Guid? groupId, CancellationToken ct);

    Task<Result> DeleteAsync(Guid notificationId, Guid userId, CancellationToken ct);
}
