
namespace MentorBooking.Api.DTOs;

// Represents a notification shown in the user's inbox.
public sealed record NotificationItemResponse(
    Guid Id,
    Guid? SessionId,
    string Title,
    string Message,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc
);

// Paginated notification inbox.
public sealed record NotificationListResponse(
    int TotalCount,
    int UnreadCount,
    int Page,
    int PageSize,
    IReadOnlyList<NotificationItemResponse> Items
);

// Number of unread notifications.
public sealed record UnreadNotificationCountResponse(
    int UnreadCount
);

// Response after marking all notifications as read.
public sealed record MarkAllNotificationsReadResponse(
    int UpdatedCount
);
