
using MentorBooking.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MentorBooking.Api.Services;

public sealed class RescheduleExpirationWorker
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RescheduleExpirationWorker> _logger;

    public RescheduleExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RescheduleExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Reschedule expiration worker started.");

        // Run once immediately after API startup.
        await RunSafelyAsync(stoppingToken);

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
            // Normal shutdown.
        }

        _logger.LogInformation(
            "Reschedule expiration worker stopped.");
    }

    private async Task RunSafelyAsync(
        CancellationToken ct)
    {
        try
        {
            // Hosted services are singletons.
            // AppDbContext is scoped.
            await using var scope =
                _scopeFactory.CreateAsyncScope();

            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var expiredCount =
                await RescheduleProposalExpiration
                    .ExpireDueAsync(db, ct);

            if (expiredCount > 0)
            {
                _logger.LogInformation(
                    "Expired {Count} unanswered " +
                    "rescheduling proposal(s).",
                    expiredCount);
            }
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Log and retry on the next cycle.
            _logger.LogError(
                ex,
                "Reschedule expiration check failed.");
        }
    }
}
