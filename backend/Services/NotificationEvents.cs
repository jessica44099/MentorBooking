
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Services;

public static class NotificationEvents
{
    // =====================================================
    // COMMON METHOD — CREATE A NOTIFICATION
    // =====================================================

    private static void Add(
        AppDbContext db,
        Guid recipientId,
        Guid sessionId,
        string title,
        string message,
        DateTime createdAtUtc)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),

            UserId = recipientId,

            SessionId = sessionId,

            Title = title,

            Message = message,

            ReadAtUtc = null,

            CreatedAtUtc = createdAtUtc
        });
    }

    // =====================================================
    // EVENT 1 — STUDENT REQUESTS A BOOKING
    // =====================================================

    // Notify the mentor when a student requests a session.

    public static void BookingRequested(
        AppDbContext db,
        Session session,
        DateTime now)
    {
        Add(
            db,
            session.MentorId,
            session.Id,
            "New booking request",
            "A student has requested a mentoring session. " +
            "Review the request in your dashboard.",
            now);
    }

    // =====================================================
    // EVENT 2 — BOOKING APPROVED / REJECTED / CANCELLED
    // =====================================================

    public static void LifecycleChanged(
        AppDbContext db,
        Session session,
        SessionStatus newStatus,
        Guid actorId,
        DateTime now)
    {
        // Mentor approves a session.
        // Notify the student.

        if (newStatus == SessionStatus.Approved)
        {
            Add(
                db,
                session.StudentId,
                session.Id,
                "Session approved",
                "Your mentor approved your booking.",
                now);
        }

        // Mentor rejects a session.
        // Notify the student.

        else if (newStatus == SessionStatus.Rejected)
        {
            Add(
                db,
                session.StudentId,
                session.Id,
                "Session rejected",
                "Your booking request was rejected.",
                now);
        }

        // Either participant cancels a session.
        // Notify the other participant.

        else if (newStatus == SessionStatus.Cancelled)
        {
            var recipientId =
                actorId == session.MentorId
                    ? session.StudentId
                    : session.MentorId;

            Add(
                db,
                recipientId,
                session.Id,
                "Session cancelled",
                "A participant cancelled the session. " +
                "Check your dashboard for details.",
                now);
        }
    }

    // =====================================================
    // EVENT 3 — MENTOR PROPOSES RESCHEDULING
    // =====================================================

    // Notify the student about the proposed new time.

    public static void RescheduleProposed(
        AppDbContext db,
        Session session,
        DateTime now)
    {
        Add(
            db,
            session.StudentId,
            session.Id,
            "Reschedule proposed",
            "Your mentor has proposed a new session time. " +
            "Please accept or decline the proposal.",
            now);
    }

    // =====================================================
    // EVENT 4 — STUDENT ACCEPTS RESCHEDULING
    // =====================================================

    // Notify the mentor that the student accepted.

    public static void RescheduleAccepted(
        AppDbContext db,
        Session session,
        DateTime now)
    {
        Add(
            db,
            session.MentorId,
            session.Id,
            "Reschedule accepted",
            "The student accepted the new session time.",
            now);
    }

    // =====================================================
    // EVENT 5 — SESSION COMPLETED OR NO-SHOW
    // =====================================================

    public static void ProgressChanged(
        AppDbContext db,
        Session session,
        SessionStatus newStatus,
        DateTime now)
    {
        // Notify the student when the session is completed.

        if (newStatus == SessionStatus.Completed)
        {
            Add(
                db,
                session.StudentId,
                session.Id,
                "Session completed",
                "Your mentoring session has been completed. " +
                "You can now review the session record.",
                now);
        }

        // Notify the student when a session is marked No-Show.

        else if (newStatus == SessionStatus.NoShow)
        {
            Add(
                db,
                session.StudentId,
                session.Id,
                "Session marked No-Show",
                "Your mentor marked the session No-Show. " +
                "Check your session details.",
                now);
        }
    }
}
