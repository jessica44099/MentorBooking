
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface INotificationService
{
    Task<NotificationListResponse> GetMineAsync(
        Guid userId,
        int page = 1,
        int pageSize = 20,
        bool unreadOnly = false,
        CancellationToken ct = default);

    Task<UnreadNotificationCountResponse> GetUnreadCountAsync(
        Guid userId,
        CancellationToken ct = default);

    Task MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken ct = default);

    Task<MarkAllNotificationsReadResponse> MarkAllReadAsync(
        Guid userId,
        CancellationToken ct = default);
}

public sealed class NotificationService
    : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    // Get notifications for the logged-in user.
    public async Task<NotificationListResponse> GetMineAsync(
        Guid userId,
        int page = 1,
        int pageSize = 20,
        bool unreadOnly = false,
        CancellationToken ct = default)
    {
        if (page < 1 ||
            pageSize < 1 ||
            pageSize > 50 ||
            ((long)page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentException(
                "Page must be positive and pageSize " +
                "must be between 1 and 50.");
        }

        await EnsureActiveUserAsync(userId, ct);

        var mine = _db.Notifications
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        // Count all unread notifications for this user.
        var unreadCount = await mine
            .CountAsync(x => x.ReadAtUtc == null, ct);

        var filtered = unreadOnly
            ? mine.Where(x => x.ReadAtUtc == null)
            : mine;

        var totalCount = await filtered.CountAsync(ct);

        var skip = (int)(((long)page - 1) * pageSize);

        var items = await filtered
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(x => new NotificationItemResponse(
                x.Id,
                x.SessionId,
                x.Title,
                x.Message,
                DateTime.SpecifyKind(
                    x.CreatedAtUtc,
                    DateTimeKind.Utc),
                x.ReadAtUtc == null
                    ? null
                    : DateTime.SpecifyKind(
                        x.ReadAtUtc.Value,
                        DateTimeKind.Utc)
            ))
            .ToListAsync(ct);

        return new NotificationListResponse(
            TotalCount: totalCount,
            UnreadCount: unreadCount,
            Page: page,
            PageSize: pageSize,
            Items: items);
    }

    // Count unread notifications for the logged-in user.
    public async Task<UnreadNotificationCountResponse>
        GetUnreadCountAsync(
            Guid userId,
            CancellationToken ct = default)
    {
        await EnsureActiveUserAsync(userId, ct);

        var count = await _db.Notifications
            .AsNoTracking()
            .CountAsync(
                x => x.UserId == userId &&
                     x.ReadAtUtc == null,
                ct);

        return new UnreadNotificationCountResponse(count);
    }

    // Mark one notification as read.
    public async Task MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken ct = default)
    {
        await EnsureActiveUserAsync(userId, ct);

        var exists = await _db.Notifications
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == notificationId &&
                     x.UserId == userId,
                ct);

        if (!exists)
        {
            throw new KeyNotFoundException(
                "Notification not found.");
        }

        // Preserve the original read time if already read.
        await _db.Notifications
            .Where(x =>
                x.Id == notificationId &&
                x.UserId == userId &&
                x.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.ReadAtUtc,
                    (DateTime?)DateTime.UtcNow),
                ct);
    }

    // Mark all unread notifications as read.
    public async Task<MarkAllNotificationsReadResponse>
        MarkAllReadAsync(
            Guid userId,
            CancellationToken ct = default)
    {
        await EnsureActiveUserAsync(userId, ct);

        var now = DateTime.UtcNow;

        var count = await _db.Notifications
            .Where(x =>
                x.UserId == userId &&
                x.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.ReadAtUtc,
                    (DateTime?)now),
                ct);

        return new MarkAllNotificationsReadResponse(
            UpdatedCount: count);
    }

    // Confirm that the account exists and is active.
    private async Task EnsureActiveUserAsync(
        Guid userId,
        CancellationToken ct)
    {
        var exists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == userId &&
                     !x.IsDeleted &&
                     (
                         x.Role == UserRole.Student ||
                         x.Role == UserRole.Mentor
                     ),
                ct);

        if (!exists)
        {
            throw new UnauthorizedAccessException(
                "An active account is required.");
        }
    }
}
