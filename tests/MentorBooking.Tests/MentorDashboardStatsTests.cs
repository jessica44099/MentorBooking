
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Xunit;

namespace MentorBooking.Tests;

public sealed class MentorDashboardStatsTests
{
    [Fact]
    public async Task New_mentor_has_zero_statistics()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);

        var service = new MentorDashboardStatsService(test.Db);

        var stats = await service.GetMineAsync(mentor.Id);

        Assert.Equal(mentor.Id, stats.MentorId);
        Assert.Equal(0, stats.CompletedSessions);
        Assert.Equal(0, stats.StudentsHelped);
        Assert.Equal(0, stats.DoubtsCleared);
        Assert.Equal(0, stats.HoursMentored);
        Assert.Null(stats.AverageRating);
        Assert.Equal(0, stats.ReviewCount);
        Assert.Equal(0, stats.PendingRequests);
        Assert.Equal(0, stats.UpcomingApprovedSessions);
    }

    [Fact]
    public async Task Completed_sessions_produce_correct_totals()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student1 = test.AddUser(UserRole.Student);
        var student2 = test.AddUser(UserRole.Student);

        var start = new DateTime(
            2026, 11, 1, 4, 30, 0,
            DateTimeKind.Utc);

        var slot = test.AddSlot(
            mentor, start, durationMinutes: 360);

        var first = test.AddSession(
            mentor, student1, slot,
            start, SessionStatus.Completed);

        var second = test.AddSession(
            mentor, student1, slot,
            start.AddHours(2),
            SessionStatus.Completed);

        var third = test.AddSession(
            mentor, student2, slot,
            start.AddHours(4),
            SessionStatus.Completed);

        // 60 minutes actually mentored.
        first.ActualStartUtc = start;
        first.ActualEndUtc = start.AddMinutes(60);

        // 30 minutes actually mentored.
        second.ActualStartUtc = start.AddHours(2);
        second.ActualEndUtc = start.AddHours(2)
            .AddMinutes(30);

        // Third session has no actual timestamps.
        // It counts as Completed but adds no hours.
        await test.Db.SaveChangesAsync();

        test.Db.SessionRecords.AddRange(
            new SessionRecord
            {
                SessionId = first.Id,
                DoubtsCleared = 4,
                Summary = "Solved four problems.",
                Resolution = ResolutionStatus.FullyResolved,
                CompletedAtUtc = start.AddHours(1)
            },
            new SessionRecord
            {
                SessionId = second.Id,
                DoubtsCleared = 3,
                Summary = "Solved three problems.",
                Resolution = ResolutionStatus.PartlyResolved,
                CompletedAtUtc = start.AddHours(2)
                    .AddMinutes(30)
            });

        // No record is added for the third session.
        // This verifies that missing records add zero.
        test.Db.Reviews.AddRange(
            new Review
            {
                SessionId = first.Id,
                Rating = 5,
                Comment = "Excellent.",
                CreatedAtUtc = start.AddHours(2)
            },
            new Review
            {
                SessionId = second.Id,
                Rating = 4,
                Comment = "Helpful.",
                CreatedAtUtc = start.AddHours(3)
            });

        await test.Db.SaveChangesAsync();

        var service = new MentorDashboardStatsService(test.Db);

        var stats = await service.GetMineAsync(mentor.Id);

        Assert.Equal(3, stats.CompletedSessions);
        Assert.Equal(2, stats.StudentsHelped);
        Assert.Equal(7, stats.DoubtsCleared);
        Assert.Equal(1.5, stats.HoursMentored);
        Assert.Equal(2, stats.ReviewCount);
        Assert.Equal(4.5, stats.AverageRating);
    }

    [Fact]
    public async Task Pending_and_upcoming_counters_ignore_old_sessions()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var future = TestDatabase.FutureStartUtc();

        var slot = test.AddSlot(
            mentor, future, durationMinutes: 360);

        // Fresh Pending request: counted.
        test.AddSession(
            mentor, student, slot,
            future, SessionStatus.Pending);

        // Pending request older than 48 hours:
        // excluded even before the worker runs.
        test.AddSession(
            mentor, student, slot,
            future.AddHours(1),
            SessionStatus.Pending,
            createdAtUtc: DateTime.UtcNow.AddHours(-49));

        // Pending request whose start has passed:
        // excluded even before the worker runs.
        var pastStart = DateTime.UtcNow.AddHours(-3);
        var pastSlot = test.AddSlot(mentor, pastStart);

        test.AddSession(
            mentor, student, pastSlot,
            pastStart, SessionStatus.Pending);

        // Future Approved session: counted.
        test.AddSession(
            mentor, student, slot,
            future.AddHours(2),
            SessionStatus.Approved);

        // Past Approved session: not upcoming.
        test.AddSession(
            mentor, student, pastSlot,
            pastStart, SessionStatus.Approved);

        var service = new MentorDashboardStatsService(test.Db);

        var stats = await service.GetMineAsync(mentor.Id);

        Assert.Equal(1, stats.PendingRequests);
        Assert.Equal(1, stats.UpcomingApprovedSessions);
    }

    [Fact]
    public async Task Students_and_deleted_mentors_cannot_read_stats()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var service = new MentorDashboardStatsService(test.Db);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetMineAsync(student.Id));

        mentor.IsDeleted = true;
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetMineAsync(mentor.Id));
    }
}
