
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MentorBooking.Tests;

public sealed class BookingTests
{
    private static CreateBookingRequest Request(
        User mentor,
        AvailabilitySlot slot,
        DateTime startUtc,
        int durationMinutes = 60)
    {
        return new CreateBookingRequest(
            mentor.Id,
            slot.Id,
            "Percentages and Ratios",
            "I need help understanding percentages and ratios.",
            startUtc,
            durationMinutes,
            "Zoom");
    }

    [Fact]
    public async Task Fresh_pending_booking_blocks_overlap()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var firstStudent = test.AddUser(UserRole.Student);
        var secondStudent = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        test.AddSession(
            mentor,
            firstStudent,
            slot,
            start,
            SessionStatus.Pending);

        var service = new BookingService(
            test.Db,
            test.Configuration);

        await Assert.ThrowsAsync<BookingConflictException>(
            () => service.CreateAsync(
                secondStudent.Id,
                Request(mentor, slot, start)));
    }

    [Fact]
    public async Task Overdue_pending_booking_releases_time()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var firstStudent = test.AddUser(UserRole.Student);
        var secondStudent = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        // The old booking exceeded its 48-hour deadline.
        test.AddSession(
            mentor,
            firstStudent,
            slot,
            start,
            SessionStatus.Pending,
            createdAtUtc: DateTime.UtcNow.AddHours(-49));

        var service = new BookingService(
            test.Db,
            test.Configuration);

        var result = await service.CreateAsync(
            secondStudent.Id,
            Request(mentor, slot, start));

        Assert.Equal("Pending", result.Status);

        // Confirm the new session was saved.
        Assert.Equal(
            2,
            await test.Db.Sessions.CountAsync());
    }

    [Fact]
    public async Task Approved_booking_blocks_overlap()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var firstStudent = test.AddUser(UserRole.Student);
        var secondStudent = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        test.AddSession(
            mentor,
            firstStudent,
            slot,
            start,
            SessionStatus.Approved);

        var service = new BookingService(
            test.Db,
            test.Configuration);

        await Assert.ThrowsAsync<BookingConflictException>(
            () => service.CreateAsync(
                secondStudent.Id,
                Request(mentor, slot, start)));
    }

    [Fact]
    public async Task Student_cannot_book_overlapping_mentors()
    {
        using var test = new TestDatabase();

        var mentor1 = test.AddUser(UserRole.Mentor);
        var mentor2 = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();

        var slot1 = test.AddSlot(mentor1, start);
        var slot2 = test.AddSlot(mentor2, start);

        test.AddSession(
            mentor1,
            student,
            slot1,
            start,
            SessionStatus.Pending);

        var service = new BookingService(
            test.Db,
            test.Configuration);

        await Assert.ThrowsAsync<BookingConflictException>(
            () => service.CreateAsync(
                student.Id,
                Request(mentor2, slot2, start)));
    }

    [Fact]
    public async Task Adjacent_bookings_are_allowed()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var firstStudent = test.AddUser(UserRole.Student);
        var secondStudent = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        // First booking: 10:00-10:30 AM IST.
        test.AddSession(
            mentor,
            firstStudent,
            slot,
            start,
            SessionStatus.Pending,
            durationMinutes: 30);

        var service = new BookingService(
            test.Db,
            test.Configuration);

        // Second booking: 10:30-11:00 AM IST.
        var result = await service.CreateAsync(
            secondStudent.Id,
            Request(
                mentor,
                slot,
                start.AddMinutes(30),
                durationMinutes: 30));

        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task Unverified_student_cannot_book()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);

        var student = test.AddUser(
            UserRole.Student,
            verified: false);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        var service = new BookingService(
            test.Db,
            test.Configuration);

        await Assert.ThrowsAsync<BookingForbiddenException>(
            () => service.CreateAsync(
                student.Id,
                Request(mentor, slot, start)));
    }
}

public sealed class SessionLifecycleTests
{
    [Fact]
    public async Task Mentor_can_approve_fresh_pending_session()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Pending);

        var originalVersion = session.RowVersion;

        var service = new SessionLifecycleService(
            test.Db);

        var result = await service.ApproveAsync(
            mentor.Id,
            session.Id,
            new ApproveSessionRequest(
                "https://zoom.us/j/12345678901"));

        Assert.Equal("Approved", result.Status);

        // ExecuteUpdate bypasses EF change tracking,
        // so clear tracked values before reading.
        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.Sessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.Equal(
            SessionStatus.Approved,
            saved.Status);

        Assert.NotEqual(
            originalVersion,
            saved.RowVersion);

        Assert.True(
            await test.Db.SessionStatusHistory.AnyAsync(
                x => x.SessionId == session.Id &&
                     x.FromStatus == SessionStatus.Pending &&
                     x.ToStatus == SessionStatus.Approved));
    }

    [Fact]
    public async Task Overdue_pending_session_cannot_be_approved()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Pending,
            createdAtUtc: DateTime.UtcNow.AddHours(-49));

        var service = new SessionLifecycleService(
            test.Db);

        await Assert.ThrowsAsync<
            InvalidSessionTransitionException>(
            () => service.ApproveAsync(
                mentor.Id,
                session.Id,
                new ApproveSessionRequest(
                    "https://zoom.us/j/12345678901")));

        // The expiration worker, not approval, will
        // eventually change Pending to Expired.
        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.Sessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.Equal(
            SessionStatus.Pending,
            saved.Status);
    }

    [Fact]
    public async Task Approved_session_cannot_be_rejected()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Approved);

        var service = new SessionLifecycleService(
            test.Db);

        await Assert.ThrowsAsync<
            InvalidSessionTransitionException>(
            () => service.RejectAsync(
                mentor.Id,
                session.Id,
                new RejectSessionRequest(
                    "No longer available")));
    }

    [Fact]
    public async Task Student_cannot_cancel_approved_session_too_late()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        // Less than two hours before the session.
        var start = DateTime.UtcNow.AddMinutes(90);
        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Approved);

        var service = new SessionLifecycleService(
            test.Db);

        await Assert.ThrowsAsync<
            InvalidSessionTransitionException>(
            () => service.CancelAsync(
                student.Id,
                session.Id,
                new CancelSessionRequest(
                    "I cannot attend")));
    }

    [Fact]
    public async Task Student_can_cancel_pending_session()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();
        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Pending);

        var service = new SessionLifecycleService(
            test.Db);

        var result = await service.CancelAsync(
            student.Id,
            session.Id,
            new CancelSessionRequest(
                "I no longer need the session"));

        Assert.Equal("Cancelled", result.Status);

        test.Db.ChangeTracker.Clear();

        var saved = await test.Db.Sessions
            .AsNoTracking()
            .SingleAsync(x => x.Id == session.Id);

        Assert.Equal(
            SessionStatus.Cancelled,
            saved.Status);

        Assert.True(
            await test.Db.SessionStatusHistory.AnyAsync(
                x => x.SessionId == session.Id &&
                     x.ToStatus == SessionStatus.Cancelled));
    }
}
