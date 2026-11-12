
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public static class RescheduleProposalExpiration
{
    private const int BatchSize = 50;

    // Expire up to 50 overdue proposals per call.
    // Returns the number successfully expired.
    public static async Task<int> ExpireDueAsync(
        AppDbContext db,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

        var dueSessions = await db.Sessions
            .AsNoTracking()
            .Where(x =>
                x.Status == SessionStatus.NeedsReschedule &&
                x.ScheduledStartUtc <= now)
            .OrderBy(x => x.ScheduledStartUtc)
            .Take(BatchSize)
            .Select(x => new
            {
                x.Id,
                x.RowVersion
            })
            .ToListAsync(ct);

        var expiredCount = 0;

        foreach (var session in dueSessions)
        {
            var nextVersion = Guid.NewGuid();

            // Conditional update prevents overwriting
            // an acceptance or cancellation.
            var changedRows = await db.Sessions
                .Where(x =>
                    x.Id == session.Id &&
                    x.Status ==
                        SessionStatus.NeedsReschedule &&
                    x.RowVersion == session.RowVersion &&
                    x.ScheduledStartUtc <= now)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.Status,
                            SessionStatus.Expired)
                        .SetProperty(
                            x => x.RowVersion,
                            nextVersion),
                    ct);

            if (changedRows != 1)
            {
                // The session has changed since it was read.
                continue;
            }

            // Keep an explanation in status history.
            db.SessionStatusHistory.Add(
                new SessionStatusHistory
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    FromStatus =
                        SessionStatus.NeedsReschedule,
                    ToStatus = SessionStatus.Expired,
                    ChangedByUserId = null,
                    Reason =
                        "Reschedule proposal expired " +
                        "because the proposed start time " +
                        "arrived without student acceptance.",
                    ChangedAtUtc = now
                });

            expiredCount++;
        }

        // Commit the status change and history together.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return expiredCount;
    }
}
