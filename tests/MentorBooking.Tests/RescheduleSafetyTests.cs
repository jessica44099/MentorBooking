
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MentorBooking.Tests;

public sealed class RescheduleSafetyTests
{
    private static RescheduleSafetyService Service(
        TestDatabase test)
    {
        return new RescheduleSafetyService(
            test.Db,
            test.Configuration);
    }

    private static (
        User Mentor,
        User Student,
        Session Session,
        AvailabilitySlot NewSlot,
        DateTime NewStart
    ) Setup(TestDatabase test)
    {
        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var oldStart = TestDatabase.FutureStartUtc();
        var newStart = oldStart.AddDays(1);

        var oldSlot = test.AddSlot(
            mentor,
            oldStart);

        var newSlot = test.AddSlot(
            mentor,
            newStart);

        var session = test.AddSession(
            mentor,
            student,
            oldSlot,
            oldStart,
            SessionStatus.Approved);

        return (
            mentor,
            student,
            session,
            newSlot,
            newStart
        );
    }

    private static void AddShift(
        TestDatabase test,
        Session session,
        DateTime oldStart,
        DateTime newStart,
        string reason,
        DateTime createdAt)
    {
        test.Db.ShiftHistories.Add(
            new ShiftHistory
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                OldStartUtc = oldStart,
                NewStartUtc = newStart,
                MinutesShifted = (int)
                    (newStart - oldStart).TotalMinutes,
                Reason = reason,
                CreatedAtUtc = createdAt
            });

        test.Db.SaveChanges();
    }

    [Fact]
    public async Task Mentor_can_read_shift_history()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, _, newStart) =
            Setup(test);

        AddShift(
            test,
            session,
            session.ScheduledStartUtc,
            newStart,
            "Mentor changed the schedule.",
            DateTime.UtcNow);

        var service = Service(test);

        var result = await service.GetHistoryAsync(
            mentor.Id,
            UserRole.Mentor,
            session.Id);

        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Entries);
        Assert.Equal(newStart, result.Entries[0].NewStartUtc);
    }

    [Fact]
    public async Task Student_can_read_own_shift_history()
    {
        using var test = new TestDatabase();

        var (_, student, session, _, newStart) =
            Setup(test);

        AddShift(
            test,
            session,
            session.ScheduledStartUtc,
            newStart,
            "Mentor proposed another time.",
            DateTime.UtcNow);

        var service = Service(test);

        var result = await service.GetHistoryAsync(
            student.Id,
            UserRole.Student,
            session.Id);

        Assert.Single(result.Entries);
        Assert.Equal(
            "Mentor proposed another time.",
            result.Entries[0].Reason);
    }

    [Fact]
    public async Task Unrelated_student_cannot_read_history()
    {
        using var test = new TestDatabase();

        var (_, _, session, _, _) = Setup(test);

        var anotherStudent =
            test.AddUser(UserRole.Student);

        var service = Service(test);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetHistoryAsync(
                anotherStudent.Id,
                UserRole.Student,
                session.Id));
    }

    [Fact]
    public async Task History_supports_pagination()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, _, newStart) =
            Setup(test);

        var now = DateTime.UtcNow;

        AddShift(
            test,
            session,
            session.ScheduledStartUtc,
            newStart,
            "First change.",
            now.AddMinutes(-5));

        AddShift(
            test,
            session,
            newStart,
            newStart.AddDays(1),
            "Second change.",
            now);

        var service = Service(test);

        var firstPage = await service.GetHistoryAsync(
            mentor.Id,
            UserRole.Mentor,
            session.Id,
            page: 1,
            pageSize: 1);

        var secondPage = await service.GetHistoryAsync(
            mentor.Id,
            UserRole.Mentor,
            session.Id,
            page: 2,
            pageSize: 1);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Single(firstPage.Entries);
        Assert.Single(secondPage.Entries);

        Assert.Equal(
            "Second change.",
            firstPage.Entries[0].Reason);

        Assert.Equal(
            "First change.",
            secondPage.Entries[0].Reason);
    }

    [Fact]
    public async Task Free_time_passes_conflict_preview()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, slot, newStart) =
            Setup(test);

        var service = Service(test);

        var result = await service.CheckAsync(
            mentor.Id,
            session.Id,
            new CheckRescheduleRequest(
                slot.Id,
                newStart));

        Assert.True(result.CanReschedule);
        Assert.Empty(result.Conflicts);

        // Preview must not change the database.
        Assert.Equal(
            0,
            await test.Db.ShiftHistories.CountAsync());

        var savedStatus = await test.Db.Sessions
            .AsNoTracking()
            .Where(x => x.Id == session.Id)
            .Select(x => x.Status)
            .SingleAsync();

        Assert.Equal(
            SessionStatus.Approved,
            savedStatus);
    }

    [Fact]
    public async Task Mentor_booking_conflict_is_reported()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, slot, newStart) =
            Setup(test);

        var anotherStudent =
            test.AddUser(UserRole.Student);

        test.AddSession(
            mentor,
            anotherStudent,
            slot,
            newStart,
            SessionStatus.Approved);

        var result = await Service(test).CheckAsync(
            mentor.Id,
            session.Id,
            new CheckRescheduleRequest(
                slot.Id,
                newStart));

        Assert.False(result.CanReschedule);

        Assert.Contains(
            result.Conflicts,
            x => x.Contains("mentor has another"));
    }

    [Fact]
    public async Task Student_booking_conflict_is_reported()
    {
        using var test = new TestDatabase();

        var (mentor, student, session, slot, newStart) =
            Setup(test);

        var anotherMentor =
            test.AddUser(UserRole.Mentor);

        var anotherSlot = test.AddSlot(
            anotherMentor,
            newStart);

        test.AddSession(
            anotherMentor,
            student,
            anotherSlot,
            newStart,
            SessionStatus.Approved);

        var result = await Service(test).CheckAsync(
            mentor.Id,
            session.Id,
            new CheckRescheduleRequest(
                slot.Id,
                newStart));

        Assert.False(result.CanReschedule);

        Assert.Contains(
            result.Conflicts,
            x => x.Contains("student has another"));
    }

    [Fact]
    public async Task Pending_reschedule_reserves_its_time()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, slot, newStart) =
            Setup(test);

        var anotherStudent =
            test.AddUser(UserRole.Student);

        test.AddSession(
            mentor,
            anotherStudent,
            slot,
            newStart,
            SessionStatus.NeedsReschedule);

        var result = await Service(test).CheckAsync(
            mentor.Id,
            session.Id,
            new CheckRescheduleRequest(
                slot.Id,
                newStart));

        Assert.False(result.CanReschedule);
    }

    [Fact]
    public async Task Busy_calendar_block_is_reported()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, slot, newStart) =
            Setup(test);

        // Insert a Busy block directly to simulate
        // an existing calendar conflict.
        test.Db.AvailabilitySlots.Add(
            new AvailabilitySlot
            {
                Id = Guid.NewGuid(),
                MentorId = mentor.Id,
                Type = AvailabilityType.Busy,
                StartUtc = newStart.AddMinutes(30),
                EndUtc = newStart.AddHours(1),
                Note = "Unavailable",
                CreatedAtUtc = DateTime.UtcNow
            });

        await test.Db.SaveChangesAsync();

        var result = await Service(test).CheckAsync(
            mentor.Id,
            session.Id,
            new CheckRescheduleRequest(
                slot.Id,
                newStart));

        Assert.False(result.CanReschedule);

        Assert.Contains(
            result.Conflicts,
            x => x.Contains("Busy calendar block"));
    }

    [Fact]
    public async Task Invalid_start_grid_is_rejected()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, slot, newStart) =
            Setup(test);

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service(test).CheckAsync(
                mentor.Id,
                session.Id,
                new CheckRescheduleRequest(
                    slot.Id,
                    newStart.AddMinutes(5))));
    }

    [Fact]
    public async Task Unverified_mentor_cannot_check()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, slot, newStart) =
            Setup(test);

        mentor.IsEmailVerified = false;

        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<
            SessionLifecycleForbiddenException>(
            () => Service(test).CheckAsync(
                mentor.Id,
                session.Id,
                new CheckRescheduleRequest(
                    slot.Id,
                    newStart)));
    }

    [Fact]
    public async Task Overdue_proposal_expires_with_history()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var past = DateTime.UtcNow.AddHours(-2);

        var slot = test.AddSlot(mentor, past);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            past,
            SessionStatus.NeedsReschedule);

        var expired = await RescheduleProposalExpiration
            .ExpireDueAsync(test.Db);

        Assert.Equal(1, expired);

        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.Sessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.Equal(
            SessionStatus.Expired,
            saved.Status);

        var history = await test.Db.SessionStatusHistory
            .AsNoTracking()
            .SingleAsync(
                x => x.SessionId == session.Id);

        Assert.Equal(
            SessionStatus.NeedsReschedule,
            history.FromStatus);

        Assert.Equal(
            SessionStatus.Expired,
            history.ToStatus);

        Assert.Null(history.ChangedByUserId);

        // Running the same check again changes nothing.
        var secondRun = await RescheduleProposalExpiration
            .ExpireDueAsync(test.Db);

        Assert.Equal(0, secondRun);

        Assert.Equal(
            1,
            await test.Db.SessionStatusHistory
                .CountAsync(x => x.SessionId == session.Id));
    }

    [Fact]
    public async Task Future_proposals_do_not_expire()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var future = TestDatabase.FutureStartUtc();

        var slot = test.AddSlot(mentor, future);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            future,
            SessionStatus.NeedsReschedule);

        var expired = await RescheduleProposalExpiration
            .ExpireDueAsync(test.Db);

        Assert.Equal(0, expired);

        var status = await test.Db.Sessions
            .AsNoTracking()
            .Where(x => x.Id == session.Id)
            .Select(x => x.Status)
            .SingleAsync();

        Assert.Equal(
            SessionStatus.NeedsReschedule,
            status);
    }
}
