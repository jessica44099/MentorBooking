
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public static class NotificationEmailBridge
{
    public static async Task QueuePendingAsync(
        AppDbContext db,
        CancellationToken ct = default)
    {
        // =================================================
        // 1. FIND NEW NOTIFICATIONS
        // =================================================

        var notifications = db.ChangeTracker
            .Entries<Notification>()
            .Where(x =>
                x.State == EntityState.Added)
            .Select(x => x.Entity)
            .ToList();

        if (notifications.Count == 0)
        {
            return;
        }

        // =================================================
        // 2. CREATE EMAIL QUEUE
        // =================================================

        var queue = new EmailNotificationQueue(db);

        foreach (var notification in notifications)
        {
            // =================================================
            // 3. PREVENT DUPLICATE EMAILS
            // =================================================

            // Notification ID is also the email ID.
            //
            // Check whether this email has already
            // been staged in the current DbContext.

            var alreadyQueued = db.EmailLogs.Local
                .Any(x => x.Id == notification.Id);

            if (alreadyQueued)
            {
                continue;
            }

            // =================================================
            // 4. QUEUE EMAIL
            // =================================================

            await queue.QueueAsync(
                recipientUserId: notification.UserId,
                subject: notification.Title,
                message: notification.Message,
                sessionId: notification.SessionId,
                ct: ct,
                emailId: notification.Id);
        }

        // Do not save here.
        // The booking or session service will commit
        // the notification and email together.
    }
}
