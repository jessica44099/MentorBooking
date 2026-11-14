
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface ISessionRescheduleService
{
    Task<RescheduleResponse> ProposeAsync(
        Guid mentorId,
        Guid sessionId,
        ProposeRescheduleRequest request,
        CancellationToken ct = default);

    Task<RescheduleResponse> AcceptAsync(
        Guid studentId,
        Guid sessionId,
        CancellationToken ct = default);
}

public sealed class SessionRescheduleService
    : ISessionRescheduleService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public SessionRescheduleService(
        AppDbContext db,
        IConfiguration configuration)
    {
        _db = db;

        var timeZoneId =
            configuration["TimeZone:Id"] ?? "Asia/Kolkata";

        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                timeZoneId);
        }
        catch (TimeZoneNotFoundException)
            when (timeZoneId == "Asia/Kolkata")
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                "India Standard Time");
        }
    }

    // =====================================================
    // 1. MENTOR PROPOSES A NEW SESSION TIME
    // =====================================================

    public async Task<RescheduleResponse> ProposeAsync(
        Guid mentorId,
        Guid sessionId,
        ProposeRescheduleRequest request,
        CancellationToken ct = default)
    {
        if (request == null ||
            request.NewSlotId == Guid.Empty ||
            request.NewStartUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "A valid slot and UTC start time are required.");
        }

        var reason = request.Reason?.Trim() ?? "";

        if (reason.Length < 10 || reason.Length > 500)
        {
            throw new ArgumentException(
                "Reason must contain 10 to 500 characters.");
        }

        try
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            // Validate mentor.
            await EnsureActiveUserAsync(
                mentorId,
                UserRole.Mentor,
                ct);

            // Find the session belonging to this mentor.
            var session = await _db.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == sessionId &&
                         x.MentorId == mentorId,
                    ct);

            if (session == null)
            {
                throw new KeyNotFoundException(
                    "Session not found.");
            }

            if (session.Status != SessionStatus.Approved)
            {
                throw new InvalidSessionTransitionException(
                    "Only Approved sessions can be rescheduled.");
            }

            var now = DateTime.UtcNow;

            var oldStart = AsUtc(
                session.ScheduledStartUtc);

            if (oldStart <= now)
            {
                throw new InvalidSessionTransitionException(
                    "Cannot reschedule a session " +
                    "after its scheduled start.");
            }

            if (session.ActualStartUtc.HasValue)
            {
                throw new InvalidSessionTransitionException(
                    "A started session cannot be rescheduled.");
            }

            // =================================================
            // 2. VALIDATE NEW START TIME
            // =================================================

            var newStart = request.NewStartUtc;

            if (newStart <= now.AddHours(2))
            {
                throw new ArgumentException(
                    "New start must be more than " +
                    "two hours in the future.");
            }

            if (newStart > now.AddDays(365))
            {
                throw new ArgumentException(
                    "New start must be within 365 days.");
            }

            var duration =
                session.ScheduledEndUtc -
                session.ScheduledStartUtc;

            if (duration != TimeSpan.FromMinutes(30) &&
                duration != TimeSpan.FromMinutes(60))
            {
                throw new InvalidSessionTransitionException(
                    "Session duration must be 30 or 60 minutes.");
            }

            var newEnd = newStart.Add(duration);

            ValidateLocalTime(newStart, newEnd);

            if (newStart == oldStart)
            {
                throw new ArgumentException(
                    "Choose a different start time.");
            }

            // =================================================
            // 3. CHECK NEW AVAILABILITY SLOT
            // =================================================

            var newSlot = await _db.AvailabilitySlots
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.NewSlotId &&
                         x.MentorId == mentorId &&
                         x.Type == AvailabilityType.Available,
                    ct);

            if (newSlot == null)
            {
                throw new KeyNotFoundException(
                    "New Available slot not found.");
            }

            // Validate both mentor and student calendars.
            await EnsureTargetIsFreeAsync(
                session,
                newSlot,
                newStart,
                newEnd,
                now,
                ct);

            // =================================================
            // 4. UPDATE SESSION WITH CONCURRENCY CHECK
            // =================================================

            var nextVersion = Guid.NewGuid();

            var changedRows = await _db.Sessions
                .Where(x =>
                    x.Id == session.Id &&
                    x.MentorId == mentorId &&
                    x.Status == SessionStatus.Approved &&
                    x.RowVersion == session.RowVersion &&
                    x.ScheduledStartUtc > now)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.SlotId,
                            newSlot.Id)
                        .SetProperty(
                            x => x.ScheduledStartUtc,
                            newStart)
                        .SetProperty(
                            x => x.ScheduledEndUtc,
                            newEnd)
                        .SetProperty(
                            x => x.Status,
                            SessionStatus.NeedsReschedule)
                        .SetProperty(
                            x => x.RowVersion,
                            nextVersion),
                    ct);

            if (changedRows != 1)
            {
                throw new SessionLifecycleConflictException(
                    "Session changed during rescheduling. " +
                    "Refresh and try again.");
            }

            // =================================================
            // 5. SAVE SHIFT HISTORY
            // =================================================

            _db.ShiftHistories.Add(new ShiftHistory
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                CausedBySessionId = null,
                OldStartUtc = oldStart,
                NewStartUtc = newStart,
                MinutesShifted = checked(
                    (int)(newStart - oldStart).TotalMinutes),
                Reason = reason,
                CreatedAtUtc = now
            });

            // =================================================
            // 6. SAVE STATUS HISTORY
            // =================================================

            _db.SessionStatusHistory.Add(
                new SessionStatusHistory
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    FromStatus = SessionStatus.Approved,
                    ToStatus = SessionStatus.NeedsReschedule,
                    ChangedByUserId = mentorId,
                    Reason = reason,
                    ChangedAtUtc = now
                });

            // =================================================
            // 7. STEP 29 — NOTIFY STUDENT
            // =================================================

            // The mentor proposed a new session time.
            // Create an unread notification for the student.

            NotificationEvents.RescheduleProposed(
                _db,
                session,
                now);

            // =================================================
            // 8. SAVE CHANGES AND COMMIT
            // =================================================

            // Session, shift history, status history,
            // and notification are saved in one transaction.

            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            return new RescheduleResponse(
                SessionId: session.Id,
                SlotId: newSlot.Id,
                OriginalStartUtc: AsUtc(
                    session.OriginalStartUtc),
                ScheduledStartUtc: newStart,
                ScheduledEndUtc: newEnd,
                Status: "NeedsReschedule",
                Message: "New time proposed. " +
                         "Student acceptance is required.");
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new SessionLifecycleConflictException(
                "Calendar is busy. Refresh and retry.",
                ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SessionLifecycleConflictException(
                "Session changed during rescheduling.",
                ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode is 5 or 6 or 19)
        {
            throw new SessionLifecycleConflictException(
                "Rescheduling could not be saved.",
                ex);
        }
    }

    // =====================================================
    // 9. STUDENT ACCEPTS THE NEW SESSION TIME
    // =====================================================

    public async Task<RescheduleResponse> AcceptAsync(
        Guid studentId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        try
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            // Validate student.
            await EnsureActiveUserAsync(
                studentId,
                UserRole.Student,
                ct);

            // Only the assigned student can accept.
            var session = await _db.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == sessionId &&
                         x.StudentId == studentId,
                    ct);

            if (session == null)
            {
                throw new KeyNotFoundException(
                    "Session not found.");
            }

            if (session.Status !=
                SessionStatus.NeedsReschedule)
            {
                throw new InvalidSessionTransitionException(
                    "There is no reschedule proposal " +
                    "waiting for acceptance.");
            }

            var now = DateTime.UtcNow;

            if (session.ScheduledStartUtc <= now)
            {
                throw new InvalidSessionTransitionException(
                    "The proposed session time has passed.");
            }

            // =================================================
            // 10. CHECK THAT THE PROPOSED SLOT STILL EXISTS
            // =================================================

            var slot = await _db.AvailabilitySlots
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == session.SlotId &&
                         x.MentorId == session.MentorId &&
                         x.Type == AvailabilityType.Available,
                    ct);

            if (slot == null)
            {
                throw new SessionLifecycleConflictException(
                    "Proposed availability no longer exists.");
            }

            await EnsureTargetIsFreeAsync(
                session,
                slot,
                AsUtc(session.ScheduledStartUtc),
                AsUtc(session.ScheduledEndUtc),
                now,
                ct);

            // =================================================
            // 11. ACCEPT WITH CONCURRENCY PROTECTION
            // =================================================

            var nextVersion = Guid.NewGuid();

            var changedRows = await _db.Sessions
                .Where(x =>
                    x.Id == sessionId &&
                    x.StudentId == studentId &&
                    x.Status ==
                        SessionStatus.NeedsReschedule &&
                    x.RowVersion == session.RowVersion &&
                    x.ScheduledStartUtc > now)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.Status,
                            SessionStatus.Approved)
                        .SetProperty(
                            x => x.RowVersion,
                            nextVersion),
                    ct);

            if (changedRows != 1)
            {
                throw new SessionLifecycleConflictException(
                    "The proposal changed. Refresh and retry.");
            }

            // =================================================
            // 12. SAVE ACCEPTANCE STATUS HISTORY
            // =================================================

            _db.SessionStatusHistory.Add(
                new SessionStatusHistory
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    FromStatus =
                        SessionStatus.NeedsReschedule,
                    ToStatus = SessionStatus.Approved,
                    ChangedByUserId = studentId,
                    Reason = "Student accepted the new time.",
                    ChangedAtUtc = now
                });

            // =================================================
            // 13. STEP 29 — NOTIFY MENTOR
            // =================================================

            // The student accepted the proposed time.
            // Create an unread notification for the mentor.

            NotificationEvents.RescheduleAccepted(
                _db,
                session,
                now);

            // =================================================
            // 14. SAVE CHANGES AND COMMIT
            // =================================================

            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            return new RescheduleResponse(
                SessionId: session.Id,
                SlotId: session.SlotId,
                OriginalStartUtc: AsUtc(
                    session.OriginalStartUtc),
                ScheduledStartUtc: AsUtc(
                    session.ScheduledStartUtc),
                ScheduledEndUtc: AsUtc(
                    session.ScheduledEndUtc),
                Status: "Approved",
                Message: "New session time accepted.");
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new SessionLifecycleConflictException(
                "Session is being updated. Please retry.",
                ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SessionLifecycleConflictException(
                "Session changed during acceptance.",
                ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode is 5 or 6 or 19)
        {
            throw new SessionLifecycleConflictException(
                "Acceptance could not be saved.",
                ex);
        }
    }

    // =====================================================
    // 15. VALIDATE TARGET SLOT AND BOOKING CONFLICTS
    // =====================================================

    private async Task EnsureTargetIsFreeAsync(
        Session session,
        AvailabilitySlot slot,
        DateTime startUtc,
        DateTime endUtc,
        DateTime now,
        CancellationToken ct)
    {
        if (slot.Type != AvailabilityType.Available ||
            slot.MentorId != session.MentorId ||
            startUtc < slot.StartUtc ||
            endUtc > slot.EndUtc)
        {
            throw new SessionLifecycleConflictException(
                "The proposed time must fit completely " +
                "inside an Available slot.");
        }

        // Check Busy calendar blocks.
        var busyConflict = await _db.AvailabilitySlots
            .AsNoTracking()
            .AnyAsync(
                x => x.MentorId == session.MentorId &&
                     x.Type == AvailabilityType.Busy &&
                     x.StartUtc < endUtc &&
                     x.EndUtc > startUtc,
                ct);

        if (busyConflict)
        {
            throw new SessionLifecycleConflictException(
                "The mentor is busy at the proposed time.");
        }

        var approvalCutoff = now.AddHours(
            -PendingBookingDeadline.ApprovalHours);

        // Protect both mentor and student calendars.
        // Exclude the current session.

        var conflict = await _db.Sessions
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != session.Id &&
                     (
                         x.MentorId == session.MentorId ||
                         x.StudentId == session.StudentId
                     ) &&
                     x.ScheduledStartUtc < endUtc &&
                     x.ScheduledEndUtc > startUtc &&
                     (
                         x.Status == SessionStatus.Approved ||
                         x.Status == SessionStatus.InProgress ||
                         x.Status ==
                             SessionStatus.NeedsReschedule ||
                         (
                             x.Status == SessionStatus.Pending &&
                             x.CreatedAtUtc > approvalCutoff &&
                             x.ScheduledStartUtc > now
                         )
                     ),
                ct);

        if (conflict)
        {
            throw new SessionLifecycleConflictException(
                "The proposed time overlaps " +
                "another active booking.");
        }
    }

    // =====================================================
    // 16. VALIDATE LOCAL CALENDAR RULES
    // =====================================================

    private void ValidateLocalTime(
        DateTime startUtc,
        DateTime endUtc)
    {
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(
            startUtc,
            _timeZone);

        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(
            endUtc,
            _timeZone);

        if (localStart.Date != localEnd.Date)
        {
            throw new ArgumentException(
                "Sessions cannot cross midnight in IST.");
        }

        if (localStart.Minute % 30 != 0 ||
            localStart.Ticks %
                TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException(
                "Start must follow the 30-minute grid.");
        }
    }

    // =====================================================
    // 17. VALIDATE USER ACCOUNT
    // =====================================================

    private async Task EnsureActiveUserAsync(
        Guid userId,
        UserRole role,
        CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(x =>
                x.Id == userId &&
                x.Role == role &&
                !x.IsDeleted)
            .Select(x => new
            {
                x.IsEmailVerified
            })
            .FirstOrDefaultAsync(ct);

        if (user == null)
        {
            throw new SessionLifecycleForbiddenException(
                "An active account with the correct " +
                "role is required.");
        }

        if (!user.IsEmailVerified)
        {
            throw new SessionLifecycleForbiddenException(
                "Verify your email before rescheduling.");
        }
    }

    // =====================================================
    // 18. NORMALIZE DATABASE UTC VALUES
    // =====================================================

    private static DateTime AsUtc(DateTime value)
    {
        return DateTime.SpecifyKind(
            value,
            DateTimeKind.Utc);
    }
}
