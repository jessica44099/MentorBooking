
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MentorBooking.Tests;

public sealed class EmailNotificationQueueTests
{
    // =====================================================
    // TEST 1 — VERIFIED RECIPIENT CAN BE QUEUED
    // =====================================================

    [Fact]
    public async Task Verified_recipient_can_be_queued()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);

        var queue = new EmailNotificationQueue(
            test.Db);

        var result = await queue.QueueAsync(
            mentor.Id,
            "New booking request",
            "A student has requested a session.");

        Assert.True(result);

        // The queue service only adds the record
        // to the DbContext. It does not save it.
        Assert.Equal(
            0,
            await test.Db.EmailLogs.CountAsync());

        Assert.Single(
            test.Db.ChangeTracker
                .Entries<EmailLog>()
                .Where(x =>
                    x.State == EntityState.Added));

        await test.Db.SaveChangesAsync();

        test.Db.ChangeTracker.Clear();

        var email = await test.Db.EmailLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(mentor.Email, email.ToAddress);

        Assert.Equal(
            "New booking request",
            email.Subject);

        Assert.Equal(EmailStatus.Queued, email.Status);

        Assert.Equal(0, email.AttemptCount);

        Assert.Null(email.SentAtUtc);

        Assert.Null(email.LastError);

        Assert.NotNull(email.NextAttemptAtUtc);
    }

    // =====================================================
    // TEST 2 — UNVERIFIED RECIPIENT IS SKIPPED
    // =====================================================

    [Fact]
    public async Task Unverified_recipient_is_skipped()
    {
        using var test = new TestDatabase();

        var student = test.AddUser(
            UserRole.Student,
            verified: false);

        var queue = new EmailNotificationQueue(
            test.Db);

        var result = await queue.QueueAsync(
            student.Id,
            "Session approved",
            "Your session has been approved.");

        Assert.False(result);

        await test.Db.SaveChangesAsync();

        Assert.Equal(
            0,
            await test.Db.EmailLogs.CountAsync());
    }

    // =====================================================
    // TEST 3 — DELETED RECIPIENT IS SKIPPED
    // =====================================================

    [Fact]
    public async Task Deleted_recipient_is_skipped()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);

        mentor.IsDeleted = true;

        await test.Db.SaveChangesAsync();

        var queue = new EmailNotificationQueue(
            test.Db);

        var result = await queue.QueueAsync(
            mentor.Id,
            "New booking request",
            "A student requested a session.");

        Assert.False(result);

        await test.Db.SaveChangesAsync();

        Assert.Empty(
            await test.Db.EmailLogs.ToListAsync());
    }

    // =====================================================
    // TEST 4 — HTML CONTENT IS ENCODED
    // =====================================================

    [Fact]
    public async Task Email_content_is_html_encoded()
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);

        var queue = new EmailNotificationQueue(
            test.Db);

        await queue.QueueAsync(
            student.Id,
            "<script>Alert</script>",
            "<script>Something happened</script>");

        await test.Db.SaveChangesAsync();

        var email = await test.Db.EmailLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Contains(
            "&lt;script&gt;",
            email.HtmlBody);

        Assert.DoesNotContain(
            "<script>",
            email.HtmlBody);

        // The plain-text version keeps the
        // original message as text.
        Assert.Contains(
            "Something happened",
            email.TextBody);
    }

    // =====================================================
    // TEST 5 — INVALID EMAIL CONTENT IS REJECTED
    // =====================================================

    [Theory]
    [InlineData("", "Valid message")]
    [InlineData("Valid subject", " ")]
    public async Task Invalid_email_content_is_rejected(
        string subject,
        string message)
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);

        var queue = new EmailNotificationQueue(
            test.Db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => queue.QueueAsync(
                student.Id,
                subject,
                message));

        Assert.Equal(
            0,
            await test.Db.EmailLogs.CountAsync());
    }

    // =====================================================
    // TEST 6 — BRIDGE QUEUES NEW NOTIFICATIONS
    // =====================================================

    [Fact]
    public async Task Bridge_queues_new_notifications()
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

        NotificationEvents.BookingRequested(
            test.Db,
            session,
            DateTime.UtcNow);

        await NotificationEmailBridge.QueuePendingAsync(
            test.Db);

        await test.Db.SaveChangesAsync();

        Assert.Equal(
            1,
            await test.Db.Notifications.CountAsync());

        Assert.Equal(
            1,
            await test.Db.EmailLogs.CountAsync());

        // Once saved, the notification is no
        // longer in the Added state.
        // Calling the bridge again should not
        // create another email.

        await NotificationEmailBridge.QueuePendingAsync(
            test.Db);

        await test.Db.SaveChangesAsync();

        Assert.Equal(
            1,
            await test.Db.EmailLogs.CountAsync());
    }

    // =====================================================
    // TEST 7 — TRANSACTION ROLLBACK REMOVES EMAIL
    // =====================================================

    [Fact]
    public async Task Rolled_back_transaction_saves_no_email()
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

        await using (
            var transaction =
                await test.Db.Database
                    .BeginTransactionAsync())
        {
            NotificationEvents.BookingRequested(
                test.Db,
                session,
                DateTime.UtcNow);

            await NotificationEmailBridge.QueuePendingAsync(
                test.Db);

            await test.Db.SaveChangesAsync();

            // Both records are visible inside
            // the current transaction.
            Assert.Equal(
                1,
                await test.Db.Notifications.CountAsync());

            Assert.Equal(
                1,
                await test.Db.EmailLogs.CountAsync());

            await transaction.RollbackAsync();
        }

        test.Db.ChangeTracker.Clear();

        // Neither record should survive rollback.
        Assert.Equal(
            0,
            await test.Db.Notifications.CountAsync());

        Assert.Equal(
            0,
            await test.Db.EmailLogs.CountAsync());
    }

    // =====================================================
    // TEST 8 — BOOKING QUEUES MENTOR EMAIL
    // =====================================================

    [Fact]
    public async Task Booking_queues_mentor_email()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();

        var slot = test.AddSlot(mentor, start);

        var bookingService = new BookingService(
            test.Db,
            test.Configuration);

        var request = new CreateBookingRequest(
            MentorId: mentor.Id,
            SlotId: slot.Id,
            Topic: "Percentages",
            Description:
                "I need help solving percentage problems.",
            ScheduledStartUtc: start,
            DurationMinutes: 60,
            Mode: "Zoom");

        var booking = await bookingService.CreateAsync(
            student.Id,
            request);

        test.Db.ChangeTracker.Clear();

        var email = await test.Db.EmailLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(
            mentor.Email,
            email.ToAddress);

        Assert.Equal(
            "New booking request",
            email.Subject);

        Assert.Equal(
            EmailStatus.Queued,
            email.Status);

        Assert.Null(email.SentAtUtc);

        // The booking notification also exists.
        Assert.True(
            await test.Db.Notifications.AnyAsync(
                x => x.UserId == mentor.Id &&
                     x.SessionId == booking.Id));
    }

    // =====================================================
    // TEST 9 — FAILED BOOKING QUEUES NO EMAIL
    // =====================================================

    [Fact]
    public async Task Failed_booking_queues_no_email()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();

        var slot = test.AddSlot(mentor, start);

        // Another active booking already
        // reserves the same mentor and time.
        var otherStudent = test.AddUser(
            UserRole.Student);

        test.AddSession(
            mentor,
            otherStudent,
            slot,
            start,
            SessionStatus.Approved);

        var service = new BookingService(
            test.Db,
            test.Configuration);

        var request = new CreateBookingRequest(
            MentorId: mentor.Id,
            SlotId: slot.Id,
            Topic: "Ratios",
            Description:
                "I need help with ratio practice problems.",
            ScheduledStartUtc: start,
            DurationMinutes: 60,
            Mode: "Zoom");

        await Assert.ThrowsAsync<BookingConflictException>(
            () => service.CreateAsync(
                student.Id,
                request));

        Assert.Equal(
            0,
            await test.Db.EmailLogs.CountAsync());

        Assert.Equal(
            0,
            await test.Db.Notifications.CountAsync());
    }

    // =====================================================
    // TEST 10 — APPROVAL AND CANCELLATION EMAILS
    // =====================================================

    [Fact]
    public async Task Approval_and_cancellation_queue_correct_emails()
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

        var lifecycle = new SessionLifecycleService(
            test.Db);

        // Mentor approves the booking.
        await lifecycle.ApproveAsync(
            mentor.Id,
            session.Id,
            new ApproveSessionRequest(
                "https://zoom.us/j/1234567890"));

        // Student cancels the approved booking.
        await lifecycle.CancelAsync(
            student.Id,
            session.Id,
            new CancelSessionRequest(
                "I cannot attend this session."));

        var emails = await test.Db.EmailLogs
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, emails.Count);

        Assert.Contains(
            emails,
            x => x.ToAddress == student.Email &&
                 x.Subject == "Session approved");

        Assert.Contains(
            emails,
            x => x.ToAddress == mentor.Email &&
                 x.Subject == "Session cancelled");

        Assert.All(
            emails,
            x => Assert.Equal(
                EmailStatus.Queued,
                x.Status));
    }

    // =====================================================
    // TEST 11 — REJECTION EMAIL
    // =====================================================

    [Fact]
    public async Task Rejection_queues_student_email()
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

        var lifecycle = new SessionLifecycleService(
            test.Db);

        await lifecycle.RejectAsync(
            mentor.Id,
            session.Id,
            new RejectSessionRequest(
                "I am unavailable at this time."));

        var email = await test.Db.EmailLogs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(
            student.Email,
            email.ToAddress);

        Assert.Equal(
            "Session rejected",
            email.Subject);

        Assert.Equal(
            EmailStatus.Queued,
            email.Status);
    }

    // =====================================================
    // TEST 12 — RESCHEDULING EMAILS
    // =====================================================

    [Fact]
    public async Task Rescheduling_queues_correct_emails()
    {
        using var test = new TestDatabase();

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

        var reschedule = new SessionRescheduleService(
            test.Db,
            test.Configuration);

        // Mentor proposes a new time.
        await reschedule.ProposeAsync(
            mentor.Id,
            session.Id,
            new ProposeRescheduleRequest(
                NewSlotId: newSlot.Id,
                NewStartUtc: newStart,
                Reason:
                    "I need to move this session to tomorrow."));

        // Student accepts the proposed time.
        await reschedule.AcceptAsync(
            student.Id,
            session.Id);

        var emails = await test.Db.EmailLogs
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, emails.Count);

        Assert.Contains(
            emails,
            x => x.ToAddress == student.Email &&
                 x.Subject == "Reschedule proposed");

        Assert.Contains(
            emails,
            x => x.ToAddress == mentor.Email &&
                 x.Subject == "Reschedule accepted");
    }

    // =====================================================
    // TEST 13 — COMPLETED AND NO-SHOW EMAILS
    // =====================================================

    [Fact]
    public async Task Completion_and_no_show_queue_student_emails()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        // Prepare an InProgress session
        // with a recorded actual start.
        var completedStart = DateTime.UtcNow
            .AddHours(-3);

        var completedSlot = test.AddSlot(
            mentor,
            completedStart);

        var completedSession = test.AddSession(
            mentor,
            student,
            completedSlot,
            completedStart,
            SessionStatus.InProgress);

        completedSession.ActualStartUtc =
            completedStart;

        await test.Db.SaveChangesAsync();

        var progress = new SessionProgressService(
            test.Db);

        await progress.CompleteAsync(
            mentor.Id,
            completedSession.Id,
            new CompleteSessionRequest(
                DoubtsCleared: 3,
                Summary:
                    "Student completed percentage practice.",
                Resolution: "FullyResolved",
                PrivateMentorNote: null));

        // Prepare another Approved session
        // whose scheduled end has passed.
        var noShowStart = DateTime.UtcNow
            .AddHours(-2);

        var noShowSlot = test.AddSlot(
            mentor,
            noShowStart);

        var noShowSession = test.AddSession(
            mentor,
            student,
            noShowSlot,
            noShowStart,
            SessionStatus.Approved);

        await progress.MarkNoShowAsync(
            mentor.Id,
            noShowSession.Id);

        var emails = await test.Db.EmailLogs
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, emails.Count);

        Assert.Contains(
            emails,
            x => x.ToAddress == student.Email &&
                 x.Subject == "Session completed");

        Assert.Contains(
            emails,
            x => x.ToAddress == student.Email &&
                 x.Subject == "Session marked No-Show");
    }

    // =====================================================
    // TEST 14 — STARTING SESSION DOES NOT QUEUE EMAIL
    // =====================================================

    [Fact]
    public async Task Starting_session_does_not_queue_email()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        // Starting is allowed up to 10 minutes
        // before the scheduled start time.
        var start = DateTime.UtcNow
            .AddMinutes(5);

        var slot = test.AddSlot(mentor, start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Approved);

        var progress = new SessionProgressService(
            test.Db);

        var result = await progress.StartAsync(
            mentor.Id,
            session.Id);

        Assert.Equal(
            "InProgress",
            result.Status);

        // Session start is intentionally silent.
        Assert.Equal(
            0,
            await test.Db.EmailLogs.CountAsync());

        Assert.Equal(
            0,
            await test.Db.Notifications.CountAsync());
    }
}
