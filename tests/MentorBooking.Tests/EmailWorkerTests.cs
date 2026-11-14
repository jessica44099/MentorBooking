
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MentorBooking.Tests;

public sealed class EmailWorkerTests
{
    // =====================================================
    // TEST INFRASTRUCTURE
    // =====================================================

    // Record delivery attempts without sending emails.
    private sealed class RecordingEmailSender
        : IQueuedEmailSender
    {
        public bool ShouldFail { get; set; }

        public EmailDeliveryOutcome Outcome { get; set; }
            = EmailDeliveryOutcome.Delivered;

        public int Calls { get; private set; }

        public List<string> Recipients { get; } = new();

        public Task<EmailDeliveryOutcome> SendAsync(
            string to,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            Calls++;
            Recipients.Add(to);

            if (ShouldFail)
            {
                throw new InvalidOperationException(
                    "Simulated email delivery failure.");
            }

            return Task.FromResult(Outcome);
        }
    }

    // Create a separate scoped DbContext for the worker.
    // Both contexts use the SAME open in-memory SQLite
    // connection owned by TestDatabase.
    private static ServiceProvider CreateProvider(
        TestDatabase test,
        IQueuedEmailSender sender)
    {
        var services = new ServiceCollection();

        var connection =
            test.Db.Database.GetDbConnection();

        services.AddDbContext<AppDbContext>(
            options => options.UseSqlite(connection));

        services.AddScoped<IQueuedEmailSender>(
            _ => sender);

        return services.BuildServiceProvider();
    }

    private static QueuedEmailDeliveryWorker CreateWorker(
        IServiceProvider provider)
    {
        return new QueuedEmailDeliveryWorker(
            provider.GetRequiredService<
                IServiceScopeFactory>(),
            Options.Create(
                new EmailDeliveryOptions
                {
                    Enabled = true
                }),
            NullLogger<
                QueuedEmailDeliveryWorker>.Instance);
    }

    // Insert an email that is ready to process.
    private static async Task<Guid> AddQueuedEmailAsync(
        TestDatabase test,
        DateTime? dueAtUtc = null)
    {
        var email = new EmailLog
        {
            Id = Guid.NewGuid(),

            ToAddress = "recipient@mentorbooking.test",

            Subject = "Session approved",

            HtmlBody = "<p>Session approved</p>",

            TextBody = "Session approved",

            Status = EmailStatus.Queued,

            AttemptCount = 0,

            NextAttemptAtUtc =
                dueAtUtc ?? DateTime.UtcNow.AddMinutes(-1),

            CreatedAtUtc = DateTime.UtcNow,

            SentAtUtc = null
        };

        test.Db.EmailLogs.Add(email);

        await test.Db.SaveChangesAsync();

        test.Db.ChangeTracker.Clear();

        return email.Id;
    }

    private static async Task<EmailLog> LoadEmailAsync(
        TestDatabase test,
        Guid emailId)
    {
        return await test.Db.EmailLogs
            .AsNoTracking()
            .SingleAsync(x => x.Id == emailId);
    }

    // =====================================================
    // TEST 1 — SUCCESSFUL DELIVERY
    // =====================================================

    [Fact]
    public async Task Queued_email_is_sent_successfully()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(test);

        var sender = new RecordingEmailSender();

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var email = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(EmailStatus.Sent, email.Status);

        Assert.Equal(1, email.AttemptCount);

        Assert.NotNull(email.SentAtUtc);

        Assert.Null(email.NextAttemptAtUtc);

        Assert.Equal(1, sender.Calls);

        Assert.Contains(
            "recipient@mentorbooking.test",
            sender.Recipients);
    }

    // =====================================================
    // TEST 2 — DEVELOPMENT PREVIEW
    // =====================================================

    [Fact]
    public async Task Preview_does_not_claim_real_delivery()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(test);

        var sender = new RecordingEmailSender
        {
            Outcome = EmailDeliveryOutcome.Previewed
        };

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var email = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(
            EmailStatus.Previewed,
            email.Status);

        Assert.Equal(1, email.AttemptCount);

        // Preview is not actual email delivery.
        Assert.Null(email.SentAtUtc);

        Assert.Null(email.NextAttemptAtUtc);

        Assert.Equal(1, sender.Calls);
    }

    // =====================================================
    // TEST 3 — FAILED EMAIL SCHEDULES RETRY
    // =====================================================

    [Fact]
    public async Task Failed_delivery_schedules_retry()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(test);

        var sender = new RecordingEmailSender
        {
            ShouldFail = true
        };

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var email = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(
            EmailStatus.Failed,
            email.Status);

        Assert.Equal(1, email.AttemptCount);

        Assert.Null(email.SentAtUtc);

        Assert.NotNull(email.NextAttemptAtUtc);

        // First retry uses approximately 2 minutes.
        Assert.InRange(
            email.NextAttemptAtUtc!.Value,
            DateTime.UtcNow.AddMinutes(1),
            DateTime.UtcNow.AddMinutes(3));

        Assert.Equal(1, sender.Calls);
    }

    // =====================================================
    // TEST 4 — FAILED EMAIL CAN SUCCEED ON RETRY
    // =====================================================

    [Fact]
    public async Task Failed_email_can_succeed_on_retry()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(test);

        var sender = new RecordingEmailSender
        {
            ShouldFail = true
        };

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        // First attempt fails.
        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var failed = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(
            EmailStatus.Failed,
            failed.Status);

        Assert.Equal(1, failed.AttemptCount);

        // Move the retry time into the past.
        // This avoids waiting two real minutes.
        test.Db.ChangeTracker.Clear();

        var tracked = await test.Db.EmailLogs
            .SingleAsync(x => x.Id == emailId);

        tracked.NextAttemptAtUtc =
            DateTime.UtcNow.AddMinutes(-1);

        await test.Db.SaveChangesAsync();

        test.Db.ChangeTracker.Clear();

        // Simulate email provider recovery.
        sender.ShouldFail = false;

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var delivered = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(
            EmailStatus.Sent,
            delivered.Status);

        Assert.Equal(2, delivered.AttemptCount);

        Assert.NotNull(delivered.SentAtUtc);

        Assert.Equal(2, sender.Calls);
    }

    // =====================================================
    // TEST 5 — STOP AFTER FIVE FAILED ATTEMPTS
    // =====================================================

    [Fact]
    public async Task Worker_stops_after_five_failures()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(test);

        var sender = new RecordingEmailSender
        {
            ShouldFail = true
        };

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await worker.ProcessBatchAsync(
                CancellationToken.None);

            var email = await LoadEmailAsync(
                test,
                emailId);

            Assert.Equal(
                attempt,
                email.AttemptCount);

            Assert.Equal(
                EmailStatus.Failed,
                email.Status);

            if (attempt < 5)
            {
                // Make the next retry immediately due.
                test.Db.ChangeTracker.Clear();

                var tracked = await test.Db.EmailLogs
                    .SingleAsync(x => x.Id == emailId);

                tracked.NextAttemptAtUtc =
                    DateTime.UtcNow.AddMinutes(-1);

                await test.Db.SaveChangesAsync();

                test.Db.ChangeTracker.Clear();
            }
        }

        var finalEmail = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(5, finalEmail.AttemptCount);

        Assert.Equal(
            EmailStatus.Failed,
            finalEmail.Status);

        Assert.Null(finalEmail.NextAttemptAtUtc);

        Assert.Equal(
            "Delivery stopped after 5 attempts.",
            finalEmail.LastError);

        Assert.Equal(5, sender.Calls);

        // A sixth processing pass must not retry it.
        await worker.ProcessBatchAsync(
            CancellationToken.None);

        Assert.Equal(5, sender.Calls);
    }

    // =====================================================
    // TEST 6 — FUTURE EMAIL IS NOT PROCESSED
    // =====================================================

    [Fact]
    public async Task Email_not_yet_due_is_skipped()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(
            test,
            DateTime.UtcNow.AddHours(1));

        var sender = new RecordingEmailSender();

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var email = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(
            EmailStatus.Queued,
            email.Status);

        Assert.Equal(0, email.AttemptCount);

        Assert.Equal(0, sender.Calls);
    }

    // =====================================================
    // TEST 7 — EXPIRED PROCESSING LEASE
    // =====================================================

    [Fact]
    public async Task Expired_processing_lease_is_recovered()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(test);

        // Simulate a worker that crashed
        // after claiming an email.
        var tracked = await test.Db.EmailLogs
            .SingleAsync(x => x.Id == emailId);

        tracked.Status = EmailStatus.Processing;

        tracked.AttemptCount = 1;

        tracked.NextAttemptAtUtc =
            DateTime.UtcNow.AddMinutes(-1);

        await test.Db.SaveChangesAsync();

        test.Db.ChangeTracker.Clear();

        var sender = new RecordingEmailSender();

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var recovered = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(
            EmailStatus.Sent,
            recovered.Status);

        Assert.Equal(2, recovered.AttemptCount);

        Assert.Equal(1, sender.Calls);
    }

    // =====================================================
    // TEST 8 — SENT EMAIL IS NOT SENT AGAIN
    // =====================================================

    [Fact]
    public async Task Sent_email_is_not_processed_again()
    {
        using var test = new TestDatabase();

        var emailId = await AddQueuedEmailAsync(test);

        var sender = new RecordingEmailSender();

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var email = await LoadEmailAsync(
            test,
            emailId);

        Assert.Equal(
            EmailStatus.Sent,
            email.Status);

        Assert.Equal(1, email.AttemptCount);

        Assert.Equal(1, sender.Calls);
    }

    // =====================================================
    // TEST 9 — MULTIPLE EMAILS
    // =====================================================

    [Fact]
    public async Task Worker_processes_multiple_queued_emails()
    {
        using var test = new TestDatabase();

        var firstId = await AddQueuedEmailAsync(test);

        var secondId = await AddQueuedEmailAsync(test);

        var sender = new RecordingEmailSender();

        using var provider =
            CreateProvider(test, sender);

        var worker = CreateWorker(provider);

        await worker.ProcessBatchAsync(
            CancellationToken.None);

        var first = await LoadEmailAsync(
            test,
            firstId);

        var second = await LoadEmailAsync(
            test,
            secondId);

        Assert.Equal(
            EmailStatus.Sent,
            first.Status);

        Assert.Equal(
            EmailStatus.Sent,
            second.Status);

        Assert.Equal(2, sender.Calls);
    }
}
