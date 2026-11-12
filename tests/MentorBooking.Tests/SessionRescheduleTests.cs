
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MentorBooking.Tests;

public sealed class SessionRescheduleTests
{
    private static SessionRescheduleService CreateService(
        TestDatabase test)
    {
        return new SessionRescheduleService(
            test.Db,
            test.Configuration);
    }

    private static ProposeRescheduleRequest CreateRequest(
        AvailabilitySlot slot,
        DateTime newStartUtc)
    {
        return new ProposeRescheduleRequest(
            NewSlotId: slot.Id,
            NewStartUtc: newStartUtc,
            Reason: "Mentor needs to change the session time."
        );
    }

    // Create a mentor, a student, one Approved session
    // and a second Available slot for rescheduling.
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

    // TEST 1
    // Mentor can propose a new session time.
    [Fact]
    public async Task Mentor_can_propose_new_time()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, newSlot, newStart) =
            Setup(test);

        var originalStart = session.OriginalStartUtc;

        var service = CreateService(test);

        var result = await service.ProposeAsync(
            mentor.Id,
            session.Id,
            CreateRequest(newSlot, newStart));

        Assert.Equal("NeedsReschedule", result.Status);

        Assert.Equal(
            newStart,
            result.ScheduledStartUtc);

        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.Sessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.Equal(
            SessionStatus.NeedsReschedule,
            saved.Status);

        Assert.Equal(newSlot.Id, saved.SlotId);

        Assert.Equal(
            newStart,
            saved.ScheduledStartUtc);

        Assert.Equal(
            originalStart,
            saved.OriginalStartUtc);
    }

    // TEST 2
    // The proposed change must be recorded in history.
    [Fact]
    public async Task Proposal_creates_shift_history()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, newSlot, newStart) =
            Setup(test);

        var oldStart = session.ScheduledStartUtc;

        var service = CreateService(test);

        await service.ProposeAsync(
            mentor.Id,
            session.Id,
            CreateRequest(newSlot, newStart));

        var history = await test.Db.ShiftHistories
            .AsNoTracking()
            .SingleAsync(x => x.SessionId == session.Id);

        Assert.Equal(oldStart, history.OldStartUtc);

        Assert.Equal(newStart, history.NewStartUtc);

        Assert.Equal(1440, history.MinutesShifted);

        Assert.Equal(
            "Mentor needs to change the session time.",
            history.Reason);

        Assert.True(
            await test.Db.SessionStatusHistory
                .AsNoTracking()
                .AnyAsync(x =>
                    x.SessionId == session.Id &&
                    x.FromStatus ==
                        SessionStatus.Approved &&
                    x.ToStatus ==
                        SessionStatus.NeedsReschedule));
    }

    // TEST 3
    // Assigned student can accept the new session time.
    [Fact]
    public async Task Student_can_accept_reschedule()
    {
        using var test = new TestDatabase();

        var (mentor, student, session, newSlot, newStart) =
            Setup(test);

        var service = CreateService(test);

        await service.ProposeAsync(
            mentor.Id,
            session.Id,
            CreateRequest(newSlot, newStart));

        var result = await service.AcceptAsync(
            student.Id,
            session.Id);

        Assert.Equal("Approved", result.Status);

        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.Sessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.Equal(
            SessionStatus.Approved,
            saved.Status);

        Assert.Equal(
            newStart,
            saved.ScheduledStartUtc);

        Assert.Equal(
            newSlot.Id,
            saved.SlotId);

        Assert.True(
            await test.Db.SessionStatusHistory
                .AsNoTracking()
                .AnyAsync(x =>
                    x.SessionId == session.Id &&
                    x.FromStatus ==
                        SessionStatus.NeedsReschedule &&
                    x.ToStatus ==
                        SessionStatus.Approved &&
                    x.ChangedByUserId == student.Id));
    }

    // TEST 4
    // Another mentor cannot reschedule the session.
    [Fact]
    public async Task Another_mentor_cannot_propose()
    {
        using var test = new TestDatabase();

        var (_, _, session, newSlot, newStart) =
            Setup(test);

        var otherMentor = test.AddUser(
            UserRole.Mentor);

        var service = CreateService(test);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ProposeAsync(
                otherMentor.Id,
                session.Id,
                CreateRequest(newSlot, newStart)));
    }

    // TEST 5
    // Another student cannot accept the proposal.
    [Fact]
    public async Task Another_student_cannot_accept()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, newSlot, newStart) =
            Setup(test);

        var otherStudent = test.AddUser(
            UserRole.Student);

        var service = CreateService(test);

        await service.ProposeAsync(
            mentor.Id,
            session.Id,
            CreateRequest(newSlot, newStart));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.AcceptAsync(
                otherStudent.Id,
                session.Id));
    }

    // TEST 6
    // Pending sessions cannot be rescheduled.
    [Fact]
    public async Task Pending_session_cannot_be_rescheduled()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, newSlot, newStart) =
            Setup(test);

        session.Status = SessionStatus.Pending;

        await test.Db.SaveChangesAsync();

        var service = CreateService(test);

        await Assert.ThrowsAsync<
            InvalidSessionTransitionException>(
            () => service.ProposeAsync(
                mentor.Id,
                session.Id,
                CreateRequest(newSlot, newStart)));
    }

    // TEST 7
    // Mentor cannot reschedule into another session.
    [Fact]
    public async Task Mentor_booking_conflict_is_rejected()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, newSlot, newStart) =
            Setup(test);

        var otherStudent = test.AddUser(
            UserRole.Student);

        test.AddSession(
            mentor,
            otherStudent,
            newSlot,
            newStart,
            SessionStatus.Approved);

        var service = CreateService(test);

        await Assert.ThrowsAsync<
            SessionLifecycleConflictException>(
            () => service.ProposeAsync(
                mentor.Id,
                session.Id,
                CreateRequest(newSlot, newStart)));

        Assert.Equal(
            0,
            await test.Db.ShiftHistories.CountAsync());
    }

    // TEST 8
    // Student cannot have two overlapping sessions.
    [Fact]
    public async Task Student_booking_conflict_is_rejected()
    {
        using var test = new TestDatabase();

        var (mentor, student, session, newSlot, newStart) =
            Setup(test);

        var otherMentor = test.AddUser(
            UserRole.Mentor);

        var otherSlot = test.AddSlot(
            otherMentor,
            newStart);

        test.AddSession(
            otherMentor,
            student,
            otherSlot,
            newStart,
            SessionStatus.Approved);

        var service = CreateService(test);

        await Assert.ThrowsAsync<
            SessionLifecycleConflictException>(
            () => service.ProposeAsync(
                mentor.Id,
                session.Id,
                CreateRequest(newSlot, newStart)));
    }

    // TEST 9
    // NeedsReschedule must reserve the proposed time.
    [Fact]
    public async Task Proposed_time_blocks_other_bookings()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, newSlot, newStart) =
            Setup(test);

        var otherStudent = test.AddUser(
            UserRole.Student);

        var service = CreateService(test);

        await service.ProposeAsync(
            mentor.Id,
            session.Id,
            CreateRequest(newSlot, newStart));

        var bookingService = new BookingService(
            test.Db,
            test.Configuration);

        var bookingRequest = new CreateBookingRequest(
            MentorId: mentor.Id,
            SlotId: newSlot.Id,
            Topic: "Percentages",
            Description:
                "I need help with percentages and ratios.",
            ScheduledStartUtc: newStart,
            DurationMinutes: 60,
            Mode: "Zoom"
        );

        await Assert.ThrowsAsync<BookingConflictException>(
            () => bookingService.CreateAsync(
                otherStudent.Id,
                bookingRequest));
    }

    // TEST 10
    // Student can decline by cancelling the proposal.
    [Fact]
    public async Task Student_can_decline_by_cancelling()
    {
        using var test = new TestDatabase();

        var (mentor, student, session, newSlot, newStart) =
            Setup(test);

        var service = CreateService(test);

        await service.ProposeAsync(
            mentor.Id,
            session.Id,
            CreateRequest(newSlot, newStart));

        var lifecycleService =
            new SessionLifecycleService(test.Db);

        var result = await lifecycleService.CancelAsync(
            student.Id,
            session.Id,
            new CancelSessionRequest(
                "I cannot attend the proposed new time."));

        Assert.Equal("Cancelled", result.Status);

        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.Sessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.Equal(
            SessionStatus.Cancelled,
            saved.Status);
    }

    // TEST 11
    // Mentor must have a verified email.
    [Fact]
    public async Task Unverified_mentor_cannot_reschedule()
    {
        using var test = new TestDatabase();

        var (mentor, _, session, newSlot, newStart) =
            Setup(test);

        mentor.IsEmailVerified = false;

        await test.Db.SaveChangesAsync();

        var service = CreateService(test);

        await Assert.ThrowsAsync<
            SessionLifecycleForbiddenException>(
            () => service.ProposeAsync(
                mentor.Id,
                session.Id,
                CreateRequest(newSlot, newStart)));
    }
}
