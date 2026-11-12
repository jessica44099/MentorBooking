

using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IMentorDashboardStatsService
{
    Task<MentorDashboardStatsResponse> GetMineAsync(
        Guid mentorId,
        CancellationToken ct = default);
}

public sealed class MentorDashboardStatsService
    : IMentorDashboardStatsService
{
    private readonly AppDbContext _db;

    public MentorDashboardStatsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<MentorDashboardStatsResponse> GetMineAsync(
        Guid mentorId,
        CancellationToken ct = default)
    {
        // Verify the mentor from the actual database.
        // An old JWT must not grant access to a deleted
        // account or an account whose role has changed.
        var mentorExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user =>
                    user.Id == mentorId &&
                    user.Role == UserRole.Mentor &&
                    !user.IsDeleted,
                ct);

        if (!mentorExists)
        {
            throw new KeyNotFoundException(
                "Active mentor account not found.");
        }

        // Capture time once for consistent counters.
        var now = DateTime.UtcNow;
        var approvalCutoff = now.AddHours(
            -PendingBookingDeadline.ApprovalHours);

        // Retrieve only the fields needed to calculate
        // statistics about completed sessions.
        var completed = await _db.Sessions
            .AsNoTracking()
            .Where(session =>
                session.MentorId == mentorId &&
                session.Status == SessionStatus.Completed)
            .Select(session => new
            {
                session.StudentId,
                session.ActualStartUtc,
                session.ActualEndUtc
            })
            .ToListAsync(ct);

        var completedSessions = completed.Count;

        // A student who attended multiple completed
        // sessions counts only once.
        var studentsHelped = completed
            .Select(session => session.StudentId)
            .Distinct()
            .Count();

        // Count only doubts stored in completion records
        // belonging to this mentor's completed sessions.
        // Sessions without records contribute zero.
        var doubtsCleared = await (
            from record in _db.SessionRecords.AsNoTracking()
            join session in _db.Sessions.AsNoTracking()
                on record.SessionId equals session.Id
            where session.MentorId == mentorId &&
                  session.Status == SessionStatus.Completed
            select record.DoubtsCleared
        ).SumAsync(ct);

        // Calculate hours from actual session timestamps.
        // Ignore missing, zero-length, or reversed times.
        double totalMinutes = 0;

        foreach (var session in completed)
        {
            if (session.ActualStartUtc is DateTime start &&
                session.ActualEndUtc is DateTime end &&
                end > start)
            {
                totalMinutes += (end - start).TotalMinutes;
            }
        }

        var hoursMentored = Math.Round(
            totalMinutes / 60.0,
            2,
            MidpointRounding.AwayFromZero);

        // Only reviews of Completed sessions count.
        var mentorReviews =
            from review in _db.Reviews.AsNoTracking()
            join session in _db.Sessions.AsNoTracking()
                on review.SessionId equals session.Id
            where session.MentorId == mentorId &&
                  session.Status == SessionStatus.Completed
            select review.Rating;

        var reviewCount = await mentorReviews.CountAsync(ct);

        double? averageRating = null;

        if (reviewCount > 0)
        {
            var average = await mentorReviews
                .AverageAsync(
                    rating => (double)rating,
                    ct);

            averageRating = Math.Round(
                average,
                2,
                MidpointRounding.AwayFromZero);
        }

        // Count only Pending requests whose deadlines
        // have not already passed.
        var pendingRequests = await _db.Sessions
            .AsNoTracking()
            .CountAsync(
                session =>
                    session.MentorId == mentorId &&
                    session.Status == SessionStatus.Pending &&
                    session.CreatedAtUtc > approvalCutoff &&
                    session.ScheduledStartUtc > now,
                ct);

        // Count future Approved sessions.
        var upcomingApproved = await _db.Sessions
            .AsNoTracking()
            .CountAsync(
                session =>
                    session.MentorId == mentorId &&
                    session.Status == SessionStatus.Approved &&
                    session.ScheduledStartUtc > now,
                ct);

        return new MentorDashboardStatsResponse(
            MentorId: mentorId,
            CompletedSessions: completedSessions,
            StudentsHelped: studentsHelped,
            DoubtsCleared: doubtsCleared,
            HoursMentored: hoursMentored,
            AverageRating: averageRating,
            ReviewCount: reviewCount,
            PendingRequests: pendingRequests,
            UpcomingApprovedSessions: upcomingApproved
        );
    }
}
