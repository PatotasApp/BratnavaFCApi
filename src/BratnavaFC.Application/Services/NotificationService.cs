using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Notifications;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BratnavaFC.Application.Services;

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) => _db = db;

    public async Task<Result<NotificationPageDto>> GetMineAsync(
        Guid userId, Guid? groupId, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(page, 1);

        var query = _db.UserNotifications.Where(n => n.UserId == userId);

        if (groupId.HasValue)
            query = query.Where(n => n.GroupId == groupId.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(n => n.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationItemDto(
                n.Id,
                n.Title,
                n.Body,
                n.Type,
                n.DataJson,
                n.IsRead,
                n.CreateDate,
                n.GroupId))
            .ToListAsync(ct);

        return Result<NotificationPageDto>.Ok(new NotificationPageDto(items, total, page, pageSize));
    }

    public async Task<Result<int>> GetUnreadCountAsync(
        Guid userId, Guid? groupId, CancellationToken ct)
    {
        var query = _db.UserNotifications.Where(n => n.UserId == userId && !n.IsRead);

        if (groupId.HasValue)
            query = query.Where(n => n.GroupId == groupId.Value);

        var count = await query.CountAsync(ct);
        return Result<int>.Ok(count);
    }

    public async Task<Result> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken ct)
    {
        var n = await _db.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, ct);

        if (n is null) return Result.Fail("Notificação não encontrada.", ResultStatus.NotFound);

        n.MarkAsRead();
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> MarkAllReadAsync(Guid userId, Guid? groupId, CancellationToken ct)
    {
        var query = _db.UserNotifications.Where(n => n.UserId == userId && !n.IsRead);

        if (groupId.HasValue)
            query = query.Where(n => n.GroupId == groupId.Value);

        var unread = await query.ToListAsync(ct);
        foreach (var n in unread) n.MarkAsRead();
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(Guid notificationId, Guid userId, CancellationToken ct)
    {
        var n = await _db.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, ct);

        if (n is not null)
        {
            _db.UserNotifications.Remove(n);
            await _db.SaveChangesAsync(ct);
        }

        return Result.Ok();
    }
}
