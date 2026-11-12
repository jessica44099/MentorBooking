
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MentorBooking.Tests;

public sealed class SessionRecordCorrectionTests
{
    private static (User Mentor, User Student, Session Session)
        CreateCompletedSession(TestDatabase test)
    {
        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = new DateTime(
            2026, 11, 1, 4, 30, 0,
            DateTimeKind.Utc);

        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Completed);

        test.Db.SessionRecords.Add(new SessionRecord
        {
            SessionId = session.Id,
            DoubtsCleared = 5,
            Summary = "Original session summary.",
            Resolution = ResolutionStatus.FullyResolved,
            PrivateMentorNote = "Original private note.",
            CompletedAtUtc = start.AddHours(1)
        });

        test.Db.SaveChanges();

        return (mentor, student, session);
    }

    private static UpdateSessionRecordRequest Correction(
        int doubtsCleared = 4)
    {
        return new UpdateSessionRecordRequest(
            DoubtsCleared: doubtsCleared,
            Summary: "Corrected session summary.",
            Resolution: "PartlyResolved",
            PrivateMentorNote: "Corrected private note.",
            ChangeReason: "Correcting the completion report."
        );
    }

    [Fact]
    public async Task Correction_updates_record_and_saves_audit()
    {
        using var test = new TestDatabase();

        var (mentor, _, session) =
            CreateCompletedSession(test);

        var service = new SessionRecordCorrectionService(
            test.Db);

        var result = await service.UpdateAsync(
            mentor.Id,
            session.Id,
            Correction());

        Assert.Equal(4, result.DoubtsCleared);
        Assert.Equal(
            "Corrected session summary.",
            result.Summary);

        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.SessionRecords
            .AsNoTracking()
            .SingleAsync(x => x.SessionId == session.Id);

        Assert.Equal(4, saved.DoubtsCleared);
        Assert.Equal(
            ResolutionStatus.PartlyResolved,
            saved.Resolution);
        Assert.Equal(
            "Corrected private note.",
            saved.PrivateMentorNote);

        var audit = await test.Db.SessionRecordAudits
            .AsNoTracking()
            .SingleAsync(x => x.SessionId == session.Id);

        Assert.Equal(5, audit.OldValue);
        Assert.Equal(4, audit.NewValue);

        Assert.Equal(
            "Original session summary.",
            audit.OldSummary);
        Assert.Equal(
            "Corrected session summary.",
            audit.NewSummary);

        Assert.Equal("FullyResolved", audit.OldResolution);
        Assert.Equal("PartlyResolved", audit.NewResolution);

        Assert.Equal(
            "Original private note.",
            audit.OldPrivateMentorNote);
        Assert.Equal(
            "Corrected private note.",
            audit.NewPrivateMentorNote);

        Assert.Equal(
            "Correcting the completion report.",
            audit.ChangeReason);

        Assert.Equal(mentor.Id, audit.ChangedByMentorId);
    }

    [Fact]
    public async Task Correction_does_not_change_completion_timestamp()
    {
        using var test = new TestDatabase();

        var (mentor, _, session) =
            CreateCompletedSession(test);

        var originalTime = await test.Db.SessionRecords
            .AsNoTracking()
            .Where(x => x.SessionId == session.Id)
            .Select(x => x.CompletedAtUtc)
            .SingleAsync();

        var service = new SessionRecordCorrectionService(
            test.Db);

        await service.UpdateAsync(
            mentor.Id,
            session.Id,
            Correction());

        var savedTime = await test.Db.SessionRecords
            .AsNoTracking()
            .Where(x => x.SessionId == session.Id)
            .Select(x => x.CompletedAtUtc)
            .SingleAsync();

        Assert.Equal(originalTime, savedTime);
    }

    [Fact]
    public async Task Another_mentor_cannot_correct_or_read_audit()
    {
        using var test = new TestDatabase();

        var (_, _, session) =
            CreateCompletedSession(test);

        var otherMentor = test.AddUser(UserRole.Mentor);

        var service = new SessionRecordCorrectionService(
            test.Db);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.UpdateAsync(
                otherMentor.Id,
                session.Id,
                Correction()));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetAuditAsync(
                otherMentor.Id,
                session.Id));

        Assert.Equal(
            0,
            await test.Db.SessionRecordAudits.CountAsync());
    }

    [Fact]
    public async Task Identical_values_do_not_create_an_audit()
    {
        using var test = new TestDatabase();

        var (mentor, _, session) =
            CreateCompletedSession(test);

        var service = new SessionRecordCorrectionService(
            test.Db);

        var unchanged = new UpdateSessionRecordRequest(
            DoubtsCleared: 5,
            Summary: "Original session summary.",
            Resolution: "FullyResolved",
            PrivateMentorNote: "Original private note.",
            ChangeReason: "Checking the same information."
        );

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(
                mentor.Id,
                session.Id,
                unchanged));

        Assert.Equal(
            0,
            await test.Db.SessionRecordAudits.CountAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task Invalid_doubt_counts_are_rejected(
        int count)
    {
        using var test = new TestDatabase();

        var (mentor, _, session) =
            CreateCompletedSession(test);

        var service = new SessionRecordCorrectionService(
            test.Db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(
                mentor.Id,
                session.Id,
                Correction(count)));

        Assert.Equal(
            0,
            await test.Db.SessionRecordAudits.CountAsync());
    }

    [Fact]
    public async Task Only_completed_sessions_can_be_corrected()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        var pendingSession = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Pending);

        var service = new SessionRecordCorrectionService(
            test.Db);

        await Assert.ThrowsAsync<
            InvalidSessionTransitionException>(
            () => service.UpdateAsync(
                mentor.Id,
                pendingSession.Id,
                Correction()));
    }

    [Fact]
    public async Task Audit_read_returns_full_history()
    {
        using var test = new TestDatabase();

        var (mentor, _, session) =
            CreateCompletedSession(test);

        var service = new SessionRecordCorrectionService(
            test.Db);

        await service.UpdateAsync(
            mentor.Id,
            session.Id,
            Correction());

        var history = await service.GetAuditAsync(
            mentor.Id,
            session.Id);

        Assert.Single(history);

        Assert.Equal(
            "Original private note.",
            history[0].OldPrivateMentorNote);

        Assert.Equal(
            "Corrected private note.",
            history[0].NewPrivateMentorNote);
    }

    [Fact]
    public async Task Dashboard_uses_corrected_doubts_count()
    {
        using var test = new TestDatabase();

        var (mentor, _, session) =
            CreateCompletedSession(test);

        var correctionService =
            new SessionRecordCorrectionService(test.Db);

        await correctionService.UpdateAsync(
            mentor.Id,
            session.Id,
            Correction());

        var statsService =
            new MentorDashboardStatsService(test.Db);

        var stats = await statsService.GetMineAsync(
            mentor.Id);

        Assert.Equal(4, stats.DoubtsCleared);
        Assert.Equal(1, stats.CompletedSessions);
    }
}
