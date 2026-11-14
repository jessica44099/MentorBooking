
using MentorBooking.Api.Data;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MentorBooking.Api.Services;

// =====================================================
// STEP 30.5 — EMAIL DELIVERY OUTCOMES
// =====================================================

public enum EmailDeliveryOutcome
{
    Delivered,
    Previewed
}

// =====================================================
// EMAIL SENDER INTERFACE
// =====================================================

public interface IQueuedEmailSender
{
    Task<EmailDeliveryOutcome> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct = default);
}

// =====================================================
// DEVELOPMENT EMAIL PREVIEW SENDER
// =====================================================

// This is separate from FakeEmailService so
// we do not create duplicate EmailLogs records.

public sealed class DevelopmentQueuedEmailSender
    : IQueuedEmailSender
{
    private readonly IWebHostEnvironment _environment;

    public DevelopmentQueuedEmailSender(
        IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<EmailDeliveryOutcome> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Development email preview is not " +
                "available outside Development.");
        }

        Console.WriteLine();
        Console.WriteLine(
            "========== QUEUED EMAIL PREVIEW ==========");

        Console.WriteLine($"To: {to}");
        Console.WriteLine($"Subject: {subject}");
        Console.WriteLine();

        Console.WriteLine(textBody);

        Console.WriteLine(
            "==========================================");

        Console.WriteLine();

        // No real email was sent.
        return Task.FromResult(
            EmailDeliveryOutcome.Previewed);
    }
}

// =====================================================
// BACKGROUND EMAIL DELIVERY WORKER
// =====================================================

public sealed class QueuedEmailDeliveryWorker
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<EmailDeliveryOptions> _options;
    private readonly ILogger<QueuedEmailDeliveryWorker> _logger;

    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ProcessingLease =
        TimeSpan.FromMinutes(5);

    private const int BatchSize = 20;
    private const int MaxAttempts = 5;

    public QueuedEmailDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<EmailDeliveryOptions> options,
        ILogger<QueuedEmailDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    // =====================================================
    // 1. START BACKGROUND WORKER
    // =====================================================

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation(
                "Email delivery worker is disabled.");

            return;
        }

        _logger.LogInformation(
            "Email delivery worker started.");

        // Check immediately when the application starts.
        await ProcessSafelyAsync(stoppingToken);

        using var timer =
            new PeriodicTimer(CheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                stoppingToken))
            {
                await ProcessSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }

        _logger.LogInformation(
            "Email delivery worker stopped.");
    }

    // =====================================================
    // 2. PROCESS SAFELY
    // =====================================================

    private async Task ProcessSafelyAsync(
        CancellationToken ct)
    {
        try
        {
            await ProcessBatchAsync(ct);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Email queue processing failed. " +
                "It will retry on the next cycle.");
        }
    }

    // =====================================================
    // 3. FIND EMAILS READY FOR DELIVERY
    // =====================================================

    public async Task ProcessBatchAsync(
        CancellationToken ct)
    {
        await using var scope =
            _scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var sender = scope.ServiceProvider
            .GetRequiredService<IQueuedEmailSender>();

        var now = DateTime.UtcNow;

        // A processing lease can expire if the
        // application crashes during delivery.
        //
        // If all attempts were already consumed,
        // mark the record permanently failed.

        await db.EmailLogs
            .Where(x =>
                x.Status == EmailStatus.Processing &&
                x.AttemptCount >= MaxAttempts &&
                x.NextAttemptAtUtc != null &&
                x.NextAttemptAtUtc <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.Status,
                        EmailStatus.Failed)
                    .SetProperty(
                        x => x.NextAttemptAtUtc,
                        (DateTime?)null)
                    .SetProperty(
                        x => x.LastError,
                        "Delivery stopped after 5 attempts."),
                ct);

        // Include:
        // Queued emails
        // Failed emails due for retry
        // Processing emails with expired leases

        var emailIds = await db.EmailLogs
            .AsNoTracking()
            .Where(x =>
                (
                    x.Status == EmailStatus.Queued ||
                    x.Status == EmailStatus.Failed ||
                    x.Status == EmailStatus.Processing
                ) &&
                x.AttemptCount < MaxAttempts &&
                x.NextAttemptAtUtc != null &&
                x.NextAttemptAtUtc <= now)
            .OrderBy(x => x.NextAttemptAtUtc)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var emailId in emailIds)
        {
            ct.ThrowIfCancellationRequested();

            await ProcessOneAsync(
                db,
                sender,
                emailId,
                ct);
        }
    }

    // =====================================================
    // 4. CLAIM AND PROCESS ONE EMAIL
    // =====================================================

    private async Task ProcessOneAsync(
        AppDbContext db,
        IQueuedEmailSender sender,
        Guid emailId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var leaseUntil = now.Add(ProcessingLease);

        // Claim the email with an atomic SQL update.
        //
        // Only one worker can successfully change
        // the record from eligible to Processing.

        var claimed = await db.EmailLogs
            .Where(x =>
                x.Id == emailId &&
                (
                    x.Status == EmailStatus.Queued ||
                    x.Status == EmailStatus.Failed ||
                    x.Status == EmailStatus.Processing
                ) &&
                x.AttemptCount < MaxAttempts &&
                x.NextAttemptAtUtc != null &&
                x.NextAttemptAtUtc <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.Status,
                        EmailStatus.Processing)
                    .SetProperty(
                        x => x.AttemptCount,
                        x => x.AttemptCount + 1)
                    .SetProperty(
                        x => x.NextAttemptAtUtc,
                        (DateTime?)leaseUntil)
                    .SetProperty(
                        x => x.LastError,
                        (string?)null),
                ct);

        if (claimed != 1)
        {
            // Another worker may have claimed it.
            return;
        }

        var email = await db.EmailLogs
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == emailId,
                ct);

        try
        {
            // =================================================
            // 5. DELIVER OR PREVIEW EMAIL
            // =====================================================

            var outcome = await sender.SendAsync(
                email.ToAddress,
                email.Subject,
                email.HtmlBody,
                email.TextBody,
                ct);

            var isPreview =
                outcome == EmailDeliveryOutcome.Previewed;

            var completedAt = DateTime.UtcNow;

            // Only an actual delivery is marked Sent.
            var finalStatus = isPreview
                ? EmailStatus.Previewed
                : EmailStatus.Sent;

            // AttemptCount acts as a fencing token.
            // An older worker cannot update a job
            // after another worker has reclaimed it.

            var finalized = await db.EmailLogs
                .Where(x =>
                    x.Id == emailId &&
                    x.Status == EmailStatus.Processing &&
                    x.AttemptCount == email.AttemptCount)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.Status,
                            finalStatus)
                        .SetProperty(
                            x => x.SentAtUtc,
                            isPreview
                                ? (DateTime?)null
                                : completedAt)
                        .SetProperty(
                            x => x.NextAttemptAtUtc,
                            (DateTime?)null)
                        .SetProperty(
                            x => x.LastError,
                            (string?)null),
                    ct);

            if (finalized != 1)
            {
                _logger.LogWarning(
                    "Email {EmailId} could not be finalized. " +
                    "Its processing lease may have expired.",
                    emailId);

                return;
            }

            _logger.LogInformation(
                "Email {EmailId} processed: {Status}.",
                emailId,
                finalStatus);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            // Leave Processing status in place.
            // The lease allows a future retry.
            throw;
        }
        catch (Exception ex)
        {
            // =================================================
            // 6. HANDLE FAILED DELIVERY
            // =====================================================

            var attemptsExhausted =
                email.AttemptCount >= MaxAttempts;

            // Backoff:
            // Attempt 1 -> 2 minutes
            // Attempt 2 -> 4 minutes
            // Attempt 3 -> 8 minutes
            // Attempt 4 -> 16 minutes
            // Attempt 5 -> stop

            var retryDelay = TimeSpan.FromMinutes(
                Math.Pow(2, email.AttemptCount));

            DateTime? retryAt = attemptsExhausted
                ? null
                : DateTime.UtcNow.Add(retryDelay);

            var errorDescription = attemptsExhausted
                ? "Delivery stopped after 5 attempts."
                : "Delivery failed. Retry scheduled.";

            await db.EmailLogs
                .Where(x =>
                    x.Id == emailId &&
                    x.Status == EmailStatus.Processing &&
                    x.AttemptCount == email.AttemptCount)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.Status,
                            EmailStatus.Failed)
                        .SetProperty(
                            x => x.NextAttemptAtUtc,
                            retryAt)
                        .SetProperty(
                            x => x.LastError,
                            errorDescription),
                    ct);

            // Log the exception type without persisting
            // potentially sensitive exception messages.

            _logger.LogWarning(
                "Email {EmailId} failed on attempt " +
                "{Attempt}. Error type: {ErrorType}.",
                emailId,
                email.AttemptCount,
                ex.GetType().Name);
        }
    }
}
