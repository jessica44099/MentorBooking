
using System.Net;
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IEmailNotificationQueue
{
    Task<bool> QueueAsync(
        Guid recipientUserId,
        string subject,
        string message,
        Guid? sessionId = null,
        CancellationToken ct = default,
        Guid? emailId = null);
}

public sealed class EmailNotificationQueue
    : IEmailNotificationQueue
{
    private readonly AppDbContext _db;

    public EmailNotificationQueue(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> QueueAsync(
        Guid recipientUserId,
        string subject,
        string message,
        Guid? sessionId = null,
        CancellationToken ct = default,
        Guid? emailId = null)
    {
        // =================================================
        // 1. VALIDATE INPUT
        // =================================================

        if (recipientUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid recipient is required.");
        }

        if (emailId == Guid.Empty)
        {
            throw new ArgumentException(
                "Email ID cannot be empty.");
        }

        var cleanSubject = subject?.Trim() ?? "";
        var cleanMessage = message?.Trim() ?? "";

        if (cleanSubject.Length == 0 ||
            cleanSubject.Length > 250)
        {
            throw new ArgumentException(
                "Email subject must contain 1 to 250 characters.");
        }

        if (cleanMessage.Length == 0 ||
            cleanMessage.Length > 2000)
        {
            throw new ArgumentException(
                "Email message must contain 1 to 2000 characters.");
        }

        // =================================================
        // 2. DETERMINISTIC EMAIL ID
        // =================================================

        // When emailId is supplied by a notification,
        // use the notification's ID for the email.
        //
        // Otherwise generate a new ID for standalone
        // queue operations.

        var id = emailId ?? Guid.NewGuid();

        // Prevent the same email from being staged
        // twice within this DbContext.

        var alreadyTracked = _db.EmailLogs.Local
            .Any(x => x.Id == id);

        if (alreadyTracked)
        {
            return false;
        }

        // =================================================
        // 3. VALIDATE RECIPIENT
        // =================================================

        var recipient = await _db.Users
            .AsNoTracking()
            .Where(x =>
                x.Id == recipientUserId &&
                !x.IsDeleted &&
                x.IsEmailVerified &&
                (
                    x.Role == UserRole.Student ||
                    x.Role == UserRole.Mentor
                ))
            .Select(x => new
            {
                x.Email
            })
            .FirstOrDefaultAsync(ct);

        if (recipient == null ||
            string.IsNullOrWhiteSpace(recipient.Email))
        {
            return false;
        }

        // =================================================
        // 4. PREPARE CONTENT
        // =================================================

        var safeSubject =
            WebUtility.HtmlEncode(cleanSubject);

        var safeMessage =
            WebUtility.HtmlEncode(cleanMessage);

        var htmlBody =
            "<!DOCTYPE html>" +
            "<html><body>" +
            "<h2>" + safeSubject + "</h2>" +
            "<p>" + safeMessage + "</p>" +
            "<p>Log in to MentorBooking " +
            "to view your session details.</p>" +
            "</body></html>";

        var textBody =
            cleanSubject +
            Environment.NewLine +
            Environment.NewLine +
            cleanMessage +
            Environment.NewLine +
            Environment.NewLine +
            "Log in to MentorBooking " +
            "to view your session details.";

        var now = DateTime.UtcNow;

        // =================================================
        // 5. ADD EMAIL TO QUEUE
        // =================================================

        var email = new EmailLog
        {
            Id = id,
            ToAddress = recipient.Email,
            Subject = cleanSubject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            Status = EmailStatus.Queued,
            AttemptCount = 0,
            NextAttemptAtUtc = now,
            LastError = null,
            CreatedAtUtc = now,
            SentAtUtc = null
        };

        _db.EmailLogs.Add(email);

        // The caller saves this email within its
        // existing database transaction.
        //
        // Do not call SaveChangesAsync here.

        return true;
    }
}
