
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public sealed class PendingExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingExpirationWorker> _logger;

    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromSeconds(30);

    private const int BatchSize = 50;

    public PendingExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Pending expiration worker started.");

        // Check immediately when the backend starts.
        await RunSafelyAsync(stoppingToken);

        // Continue checking every 30 seconds.
        using var timer = new PeriodicTimer(CheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                stoppingToken))
            {
                await RunSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }

        _logger.LogInformation(
            "Pending expiration worker stopped.");
    }

    private async Task RunSafelyAsync(
        CancellationToken ct)
    {
        try
        {
            // A hosted service is a singleton.
            // DbContext is scoped, so create a scope.
            await using var scope =
                _scopeFactory.CreateAsyncScope();

            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var expiredCount = await ExpirePendingAsync(
                db, ct);

            if (expiredCount > 0)
            {
                _logger.LogInformation(
                    "Automatically expired {Count} " +
                    "pending booking(s).",
                    expiredCount);
            }
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            // Shutdown requested.
        }
        catch (Exception ex)
        {
            // Log the error and try again next cycle.
            _logger.LogError(
                ex,
                "Pending booking expiration failed. " +
                "The worker will retry.");
        }
    }

    private static async Task<int> ExpirePendingAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        var totalExpired = 0;

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var approvalDeadline = now.AddHours(-48);

            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            // A Pending session expires when:
            // 1. Its creation time is at least 48 hours old.
            // 2. OR its scheduled start has arrived.
            var dueSessions = await db.Sessions
                .AsNoTracking()
                .Where(x =>
                    x.Status == SessionStatus.Pending &&
                    (
                        x.CreatedAtUtc <= approvalDeadline ||
                        x.ScheduledStartUtc <= now
                    ))
                .OrderBy(x => x.CreatedAtUtc)
                .Take(BatchSize)
                .Select(x => new
                {
                    x.Id,
                    x.RowVersion,
                    x.CreatedAtUtc,
                    x.ScheduledStartUtc
                })
                .ToListAsync(ct);

            if (dueSessions.Count == 0)
            {
                await transaction.CommitAsync(ct);
                break;
            }

            var batchExpired = 0;

            foreach (var session in dueSessions)
            {
                // Determine why this booking expired.
                var reason =
                    session.ScheduledStartUtc <= now
                        ? "Pending booking expired because " +
                          "its scheduled start time arrived."
                        : "Pending booking expired because " +
                          "it was not approved within 48 hours.";

                var nextVersion = Guid.NewGuid();

                // Update only if the booking is STILL
                // Pending and has not been changed.
                //
                // The status and RowVersion conditions
                // protect against conflicting updates.
                var updatedRows = await db.Sessions
                    .Where(x =>
                        x.Id == session.Id &&
                        x.Status == SessionStatus.Pending &&
                        x.RowVersion == session.RowVersion &&
                        (
                            x.CreatedAtUtc <= approvalDeadline ||
                            x.ScheduledStartUtc <= now
                        ))
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                SessionStatus.Expired)
                            .SetProperty(
                                x => x.RowVersion,
                                nextVersion),
                        ct);

                // Another operation changed this session.
                // Do not create a false history entry.
                if (updatedRows != 1)
                {
                    continue;
                }

                db.SessionStatusHistory.Add(
                    new SessionStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        SessionId = session.Id,
                        FromStatus = SessionStatus.Pending,
                        ToStatus = SessionStatus.Expired,
                        ChangedByUserId = null,
                        Reason = reason,
                        ChangedAtUtc = now
                    });

                batchExpired++;
            }

            // Both the expiration and its history
            // are committed in the same transaction.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            totalExpired += batchExpired;

            if (dueSessions.Count < BatchSize ||
                batchExpired == 0)
            {
                break;
            }
        }

        return totalExpired;
    }
}
