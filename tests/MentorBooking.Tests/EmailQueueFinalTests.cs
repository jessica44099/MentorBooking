
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace MentorBooking.Tests;

public sealed class EmailQueueFinalTests
{
    // TEST 1 — PREVENT DUPLICATE QUEUED EMAILS

    [Fact]
    public async Task Bridge_called_twice_creates_one_email()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();

        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Pending);

        NotificationEvents.BookingRequested(
            test.Db,
            session,
            DateTime.UtcNow);

        await NotificationEmailBridge.QueuePendingAsync(
            test.Db);

        await NotificationEmailBridge.QueuePendingAsync(
            test.Db);

        var notifications = test.Db.ChangeTracker
            .Entries<Notification>()
            .Where(x => x.State == EntityState.Added)
            .ToList();

        var emails = test.Db.ChangeTracker
            .Entries<EmailLog>()
            .Where(x => x.State == EntityState.Added)
            .ToList();

        Assert.Single(notifications);
        Assert.Single(emails);

        Assert.Equal(
            notifications[0].Entity.Id,
            emails[0].Entity.Id);

        await test.Db.SaveChangesAsync();

        Assert.Equal(
            1,
            await test.Db.Notifications.CountAsync());

        Assert.Equal(
            1,
            await test.Db.EmailLogs.CountAsync());
    }

    // TEST 2 — DISABLED SMTP MUST NOT SEND

    [Fact]
    public async Task Disabled_smtp_refuses_delivery()
    {
        var sender = new SmtpQueuedEmailSender(
            Options.Create(
                new EmailDeliveryOptions
                {
                    Enabled = false
                }));

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => sender.SendAsync(
                "test@example.com",
                "Test",
                "<p>Test</p>",
                "Test"));
    }

    // TEST 3 — INCOMPLETE SMTP SETTINGS

    [Fact]
    public async Task Missing_smtp_settings_are_rejected()
    {
        var sender = new SmtpQueuedEmailSender(
            Options.Create(
                new EmailDeliveryOptions
                {
                    Enabled = true,
                    SmtpHost = "",
                    SmtpPort = 587,
                    RequireStartTls = true
                }));

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => sender.SendAsync(
                "test@example.com",
                "Test",
                "<p>Test</p>",
                "Test"));
    }

    // TEST 4 — REJECT UNENCRYPTED SMTP

    [Fact]
    public async Task Unencrypted_smtp_is_rejected()
    {
        var sender = new SmtpQueuedEmailSender(
            Options.Create(
                new EmailDeliveryOptions
                {
                    Enabled = true,
                    SmtpHost = "smtp.example.com",
                    SmtpPort = 587,
                    SmtpUsername = "test",
                    SmtpPassword = "test-password",
                    FromAddress = "sender@example.com",
                    RequireStartTls = false
                }));

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => sender.SendAsync(
                "recipient@example.com",
                "Test",
                "<p>Test</p>",
                "Test"));
    }
}
