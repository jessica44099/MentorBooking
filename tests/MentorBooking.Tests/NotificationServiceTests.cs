
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MentorBooking.Tests;

public sealed class NotificationServiceTests
{
    // =====================================================
    // HELPER — CREATE TEST NOTIFICATION
    // =====================================================

    private static Notification AddNotification(
        TestDatabase test,
        User user,
        string title,
        bool isRead = false,
        DateTime? createdAtUtc = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = null,
            Title = title,
            Message = "Automated notification test.",
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            ReadAtUtc = isRead
                ? DateTime.UtcNow
                : null
        };

        test.Db.Notifications.Add(notification);
        test.Db.SaveChanges();

        return notification;
    }

    // =====================================================
    // TEST 1 — NEW USER HAS EMPTY INBOX
    // =====================================================

    [Fact]
    public async Task New_user_has_empty_inbox()
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);

        var service = new NotificationService(test.Db);

        var result = await service.GetMineAsync(
            student.Id);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.UnreadCount);
    }

    // =====================================================
    // TEST 2 — USERS SEE ONLY THEIR OWN NOTIFICATIONS
    // =====================================================

    [Fact]
    public async Task Users_only_see_their_own_notifications()
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);
        var mentor = test.AddUser(UserRole.Mentor);

        AddNotification(
            test,
            student,
            "Student notification");

        AddNotification(
            test,
            mentor,
            "Mentor notification");

        var service = new NotificationService(test.Db);

        var studentInbox = await service.GetMineAsync(
            student.Id);

        var mentorInbox = await service.GetMineAsync(
            mentor.Id);

        Assert.Single(studentInbox.Items);
        Assert.Single(mentorInbox.Items);

        Assert.Equal(
            "Student notification",
            studentInbox.Items[0].Title);

        Assert.Equal(
            "Mentor notification",
            mentorInbox.Items[0].Title);
    }

    // =====================================================
    // TEST 3 — UNREAD NOTIFICATION COUNT
    // =====================================================

    [Fact]
    public async Task Unread_count_is_correct()
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);

        AddNotification(
            test,
            student,
            "Unread notification one");

        AddNotification(
            test,
            student,
            "Unread notification two");

        AddNotification(
            test,
            student,
            "Already read",
            isRead: true);

        var service = new NotificationService(test.Db);

        var count = await service.GetUnreadCountAsync(
            student.Id);

        Assert.Equal(2, count.UnreadCount);

        var unreadOnly = await service.GetMineAsync(
            student.Id,
            unreadOnly: true);

        Assert.Equal(2, unreadOnly.TotalCount);
        Assert.Equal(2, unreadOnly.UnreadCount);
        Assert.Equal(2, unreadOnly.Items.Count);
    }

    // =====================================================
    // TEST 4 — PAGINATION
    // =====================================================

    [Fact]
    public async Task Notifications_support_pagination()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);

        var baseTime = DateTime.UtcNow.AddDays(-1);

        AddNotification(
            test,
            mentor,
            "First notification",
            createdAtUtc: baseTime);

        AddNotification(
            test,
            mentor,
            "Second notification",
            createdAtUtc: baseTime.AddMinutes(1));

        AddNotification(
            test,
            mentor,
            "Third notification",
            createdAtUtc: baseTime.AddMinutes(2));

        var service = new NotificationService(test.Db);

        var firstPage = await service.GetMineAsync(
            mentor.Id,
            page: 1,
            pageSize: 2);

        var secondPage = await service.GetMineAsync(
            mentor.Id,
            page: 2,
            pageSize: 2);

        Assert.Equal(3, firstPage.TotalCount);

        Assert.Equal(2, firstPage.Items.Count);

        Assert.Single(secondPage.Items);

        Assert.Equal(
            "Third notification",
            firstPage.Items[0].Title);

        Assert.Equal(
            "Second notification",
            firstPage.Items[1].Title);

        Assert.Equal(
            "First notification",
            secondPage.Items[0].Title);
    }

    // =====================================================
    // TEST 5 — MARK ONE NOTIFICATION AS READ
    // =====================================================

    [Fact]
    public async Task Mark_read_updates_notification()
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);

        var notification = AddNotification(
            test,
            student,
            "Session approved");

        var service = new NotificationService(test.Db);

        await service.MarkReadAsync(
            student.Id,
            notification.Id);

        var saved = await test.Db.Notifications
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == notification.Id);

        Assert.NotNull(saved.ReadAtUtc);

        var originalReadTime = saved.ReadAtUtc;

        // Marking the same notification again
        // should not overwrite the read timestamp.

        await service.MarkReadAsync(
            student.Id,
            notification.Id);

        var savedAgain = await test.Db.Notifications
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == notification.Id);

        Assert.Equal(
            originalReadTime,
            savedAgain.ReadAtUtc);
    }

    // =====================================================
    // TEST 6 — CANNOT MARK ANOTHER USER'S NOTIFICATION
    // =====================================================

    [Fact]
    public async Task Cannot_mark_another_users_notification()
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);
        var mentor = test.AddUser(UserRole.Mentor);

        var notification = AddNotification(
            test,
            mentor,
            "Private mentor notification");

        var service = new NotificationService(test.Db);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.MarkReadAsync(
                student.Id,
                notification.Id));

        var saved = await test.Db.Notifications
            .AsNoTracking()
            .SingleAsync(
                x => x.Id == notification.Id);

        Assert.Null(saved.ReadAtUtc);
    }

    // =====================================================
    // TEST 7 — MARK ALL NOTIFICATIONS AS READ
    // =====================================================

    [Fact]
    public async Task Mark_all_read_only_updates_current_user()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        AddNotification(test, mentor, "Mentor one");
        AddNotification(test, mentor, "Mentor two");

        AddNotification(test, student, "Student one");

        var service = new NotificationService(test.Db);

        var result = await service.MarkAllReadAsync(
            mentor.Id);

        Assert.Equal(2, result.UpdatedCount);

        var mentorUnread =
            await service.GetUnreadCountAsync(
                mentor.Id);

        var studentUnread =
            await service.GetUnreadCountAsync(
                student.Id);

        Assert.Equal(0, mentorUnread.UnreadCount);

        Assert.Equal(1, studentUnread.UnreadCount);
    }

    // =====================================================
    // TEST 8 — BOOKING CREATES MENTOR NOTIFICATION
    // =====================================================

    [Fact]
    public async Task Booking_notifies_mentor()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();

        var slot = test.AddSlot(
            mentor,
            start);

        var bookingService = new BookingService(
            test.Db,
            test.Configuration);

        var request = new CreateBookingRequest(
            MentorId: mentor.Id,
            SlotId: slot.Id,
            Topic: "Percentages",
            Description:
                "I need guidance with percentage questions.",
            ScheduledStartUtc: start,
            DurationMinutes: 60,
            Mode: "Zoom");

        var booking = await bookingService.CreateAsync(
            student.Id,
            request);

        var notification = await test.Db.Notifications
            .AsNoTracking()
            .SingleAsync(
                x => x.UserId == mentor.Id &&
                     x.SessionId == booking.Id);

        Assert.Equal(
            "New booking request",
            notification.Title);

        Assert.Null(notification.ReadAtUtc);

        // The student should not receive the
        // mentor's booking-request notification.

        var studentNotificationExists =
            await test.Db.Notifications.AnyAsync(
                x => x.UserId == student.Id &&
                     x.SessionId == booking.Id);

        Assert.False(studentNotificationExists);
    }

    // =====================================================
    // TEST 9 — APPROVAL CREATES STUDENT NOTIFICATION
    // =====================================================

    [Fact]
    public async Task Approval_notifies_student()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var start = TestDatabase.FutureStartUtc();

        var slot = test.AddSlot(
            mentor,
            start);

        var session = test.AddSession(
            mentor,
            student,
            slot,
            start,
            SessionStatus.Pending);

        var lifecycleService =
            new SessionLifecycleService(test.Db);

        await lifecycleService.ApproveAsync(
            mentor.Id,
            session.Id,
            new ApproveSessionRequest(
                "https://zoom.us/j/1234567890"));

        var notification = await test.Db.Notifications
            .AsNoTracking()
            .SingleAsync(
                x => x.UserId == student.Id &&
                     x.SessionId == session.Id);

        Assert.Equal(
            "Session approved",
            notification.Title);

        Assert.Null(notification.ReadAtUtc);
    }

    // =====================================================
    // TEST 10 — RESCHEDULE NOTIFIES STUDENT AND MENTOR
    // =====================================================

    [Fact]
    public async Task Reschedule_notifies_both_participants()
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

        var rescheduleService =
            new SessionRescheduleService(
                test.Db,
                test.Configuration);

        // Mentor proposes a new time.

        await rescheduleService.ProposeAsync(
            mentor.Id,
            session.Id,
            new ProposeRescheduleRequest(
                NewSlotId: newSlot.Id,
                NewStartUtc: newStart,
                Reason:
                    "Changing the time for this mentoring session."));

        var studentNotified =
            await test.Db.Notifications
                .AsNoTracking()
                .AnyAsync(
                    x => x.UserId == student.Id &&
                         x.SessionId == session.Id &&
                         x.Title == "Reschedule proposed");

        Assert.True(studentNotified);

        // Student accepts the proposal.

        await rescheduleService.AcceptAsync(
            student.Id,
            session.Id);

        var mentorNotified =
            await test.Db.Notifications
                .AsNoTracking()
                .AnyAsync(
                    x => x.UserId == mentor.Id &&
                         x.SessionId == session.Id &&
                         x.Title == "Reschedule accepted");

        Assert.True(mentorNotified);
    }

    // =====================================================
    // TEST 11 — REJECTION NOTIFIES STUDENT
    // =====================================================

    [Fact]
    public async Task Rejection_notifies_student()
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

        var notified = await test.Db.Notifications
            .AsNoTracking()
            .AnyAsync(x =>
                x.UserId == student.Id &&
                x.SessionId == session.Id &&
                x.Title == "Session rejected");

        Assert.True(notified);
    }

    // =====================================================
    // TEST 12 — CANCELLATION NOTIFIES OTHER PARTICIPANT
    // =====================================================

    [Fact]
    public async Task Student_cancellation_notifies_mentor()
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

        await lifecycle.CancelAsync(
            student.Id,
            session.Id,
            new CancelSessionRequest(
                "I cannot attend this session."));

        var notified = await test.Db.Notifications
            .AsNoTracking()
            .AnyAsync(x =>
                x.UserId == mentor.Id &&
                x.SessionId == session.Id &&
                x.Title == "Session cancelled");

        Assert.True(notified);
    }

    // =====================================================
    // TEST 13 — INVALID PAGINATION
    // =====================================================

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task Invalid_pagination_is_rejected(
        int page,
        int pageSize)
    {
        using var test = new TestDatabase();

        var student = test.AddUser(UserRole.Student);

        var service = new NotificationService(test.Db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetMineAsync(
                student.Id,
                page,
                pageSize));
    }
}
