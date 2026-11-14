
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface ISessionLifecycleService
{
    Task<SessionTransitionResponse> ApproveAsync(
        Guid mentorId,
        Guid sessionId,
        ApproveSessionRequest request,
        CancellationToken ct = default);

    Task<SessionTransitionResponse> RejectAsync(
        Guid mentorId,
        Guid sessionId,
        RejectSessionRequest request,
        CancellationToken ct = default);

    Task<SessionTransitionResponse> CancelAsync(
        Guid actorId,
        Guid sessionId,
        CancelSessionRequest request,
        CancellationToken ct = default);
}

public sealed class SessionLifecycleService
    : ISessionLifecycleService
{
    private readonly AppDbContext _db;

    private enum ActionType
    {
        Approve,
        Reject,
        Cancel
    }

    public SessionLifecycleService(AppDbContext db)
    {
        _db = db;
    }

    // Mentor approves a session.
    public Task<SessionTransitionResponse> ApproveAsync(
        Guid mentorId,
        Guid sessionId,
        ApproveSessionRequest request,
        CancellationToken ct = default)
    {
        return ChangeStatusAsync(
            mentorId,
            sessionId,
            ActionType.Approve,
            request.MeetingInfo,
            null,
            ct);
    }

    // Mentor rejects a session.
    public Task<SessionTransitionResponse> RejectAsync(
        Guid mentorId,
        Guid sessionId,
        RejectSessionRequest request,
        CancellationToken ct = default)
    {
        return ChangeStatusAsync(
            mentorId,
            sessionId,
            ActionType.Reject,
            null,
            request.Reason,
            ct);
    }

    // Student or mentor cancels a session.
    public Task<SessionTransitionResponse> CancelAsync(
        Guid actorId,
        Guid sessionId,
        CancelSessionRequest request,
        CancellationToken ct = default)
    {
        return ChangeStatusAsync(
            actorId,
            sessionId,
            ActionType.Cancel,
            null,
            request.Reason,
            ct);
    }

    // Common lifecycle transition method.
    private async Task<SessionTransitionResponse> ChangeStatusAsync(
        Guid actorId,
        Guid sessionId,
        ActionType action,
        string? meetingInfo,
        string? reason,
        CancellationToken ct)
    {
        reason = NormalizeReason(reason);

        try
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            // Validate current user.
            var actor = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == actorId &&
                         !x.IsDeleted,
                    ct);

            if (actor == null)
            {
                throw new SessionLifecycleForbiddenException(
                    "Account not found or inactive.");
            }

            // Load session without tracking.
            var session = await _db.Sessions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == sessionId,
                    ct);

            if (session == null)
            {
                throw new KeyNotFoundException(
                    "Session not found.");
            }

            // Validate session ownership.
            if (action == ActionType.Approve ||
                action == ActionType.Reject)
            {
                if (actor.Role != UserRole.Mentor)
                {
                    throw new SessionLifecycleForbiddenException(
                        "Only mentors can perform this action.");
                }

                if (session.MentorId != actor.Id)
                {
                    throw new KeyNotFoundException(
                        "Session not found.");
                }
            }
            else
            {
                var ownsSession =
                    actor.Role == UserRole.Student
                        ? session.StudentId == actor.Id
                        : actor.Role == UserRole.Mentor &&
                          session.MentorId == actor.Id;

                if (!ownsSession)
                {
                    throw new KeyNotFoundException(
                        "Session not found.");
                }
            }

            var now = DateTime.UtcNow;

            var startUtc = DateTime.SpecifyKind(
                session.ScheduledStartUtc,
                DateTimeKind.Utc);

            var previousStatus = session.Status;

            SessionStatus newStatus;

            string? validatedMeetingInfo = null;

            // Determine new status.
            switch (action)
            {
                case ActionType.Approve:
                    if (!actor.IsEmailVerified)
                    {
                        throw new SessionLifecycleForbiddenException(
                            "Verify your email before approving sessions.");
                    }

                    EnsurePending(session, "approve");

                    EnsurePendingNotOverdue(session, now);

                    validatedMeetingInfo = ValidateMeetingInfo(
                        session.Mode,
                        meetingInfo);

                    newStatus = SessionStatus.Approved;
                    break;

                case ActionType.Reject:
                    EnsurePending(session, "reject");

                    EnsurePendingNotOverdue(session, now);

                    newStatus = SessionStatus.Rejected;
                    break;

                case ActionType.Cancel:
                    ValidateCancellation(
                        session,
                        actor.Role,
                        startUtc,
                        now);

                    newStatus = SessionStatus.Cancelled;
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unsupported lifecycle action.");
            }

            // Conditional SQL update prevents
            // overwriting another status transition.
            var originalVersion = session.RowVersion;
            var nextVersion = Guid.NewGuid();

            IQueryable<Session> matchingSession =
                _db.Sessions.Where(
                    x => x.Id == session.Id &&
                         x.Status == previousStatus &&
                         x.RowVersion == originalVersion);

            // Enforce Pending deadline again in SQL.
            if (previousStatus == SessionStatus.Pending)
            {
                var approvalCutoff = now.AddHours(
                    -PendingBookingDeadline.ApprovalHours);

                matchingSession = matchingSession.Where(
                    x => x.CreatedAtUtc > approvalCutoff &&
                         x.ScheduledStartUtc > now);
            }

            // Enforce Approved cancellation deadlines.
            if (action == ActionType.Cancel &&
                previousStatus == SessionStatus.Approved)
            {
                if (actor.Role == UserRole.Student)
                {
                    var studentDeadline =
                        now.AddHours(2);

                    matchingSession = matchingSession.Where(
                        x => x.ScheduledStartUtc >
                             studentDeadline);
                }
                else
                {
                    matchingSession = matchingSession.Where(
                        x => x.ScheduledStartUtc > now);
                }
            }

            int updatedRows;

            // Apply approval.
            if (action == ActionType.Approve)
            {
                updatedRows = await matchingSession
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                SessionStatus.Approved)
                            .SetProperty(
                                x => x.RowVersion,
                                nextVersion)
                            .SetProperty(
                                x => x.MeetingInfo,
                                validatedMeetingInfo)
                            .SetProperty(
                                x => x.RejectReason,
                                (string?)null),
                        ct);
            }

            // Apply rejection.
            else if (action == ActionType.Reject)
            {
                updatedRows = await matchingSession
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                SessionStatus.Rejected)
                            .SetProperty(
                                x => x.RowVersion,
                                nextVersion)
                            .SetProperty(
                                x => x.RejectReason,
                                reason),
                        ct);
            }

            // Apply cancellation.
            else
            {
                updatedRows = await matchingSession
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                SessionStatus.Cancelled)
                            .SetProperty(
                                x => x.RowVersion,
                                nextVersion),
                        ct);
            }

            if (updatedRows != 1)
            {
                throw new SessionLifecycleConflictException(
                    "The session changed or its deadline " +
                    "passed. Refresh and try again.");
            }

            // Record status transition.
            string historyReason = action switch
            {
                ActionType.Approve =>
                    "Mentor approved the session.",

                ActionType.Reject =>
                    reason ?? "Mentor rejected the session.",

                _ => reason ?? "Session cancelled."
            };

            _db.SessionStatusHistory.Add(
                new SessionStatusHistory
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    FromStatus = previousStatus,
                    ToStatus = newStatus,
                    ChangedByUserId = actor.Id,
                    Reason = historyReason,
                    ChangedAtUtc = now
                });

            // STEP 29: Create in-app notification.
            NotificationEvents.LifecycleChanged(
                _db,
                session,
                newStatus,
                actor.Id,
                now);

            // STEP 30.3: Queue email for the recipient.
            await NotificationEmailBridge.QueuePendingAsync(
                _db,
                ct);

            // Commit status, history, notification
            // and email queue as one transaction.
            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            string message = action switch
            {
                ActionType.Approve =>
                    "Session approved successfully.",

                ActionType.Reject =>
                    "Session rejected successfully.",

                _ => "Session cancelled successfully."
            };

            return new SessionTransitionResponse(
                session.Id,
                newStatus.ToString(),
                now,
                message);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SessionLifecycleConflictException(
                "Session changed during this request. " +
                "Refresh and try again.",
                ex);
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new SessionLifecycleConflictException(
                "Session is being updated. Please retry.",
                ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode is 5 or 6)
        {
            throw new SessionLifecycleConflictException(
                "Session is being updated. Please retry.",
                ex);
        }
    }

    // Ensure the session is Pending.
    private static void EnsurePending(
        Session session,
        string action)
    {
        if (session.Status != SessionStatus.Pending)
        {
            throw new InvalidSessionTransitionException(
                $"Cannot {action} a session with status " +
                $"{session.Status}.");
        }
    }

    // Ensure the Pending deadline has not passed.
    private static void EnsurePendingNotOverdue(
        Session session,
        DateTime now)
    {
        if (PendingBookingDeadline.IsOverdue(
                session.CreatedAtUtc,
                session.ScheduledStartUtc,
                now))
        {
            throw new InvalidSessionTransitionException(
                "The approval deadline has passed. " +
                "This booking will expire automatically.");
        }
    }

    // Validate cancellation rules.
    private static void ValidateCancellation(
        Session session,
        UserRole actorRole,
        DateTime startUtc,
        DateTime now)
    {
        if (session.Status != SessionStatus.Pending &&
            session.Status != SessionStatus.Approved &&
            session.Status != SessionStatus.NeedsReschedule)
        {
            throw new InvalidSessionTransitionException(
                $"Cannot cancel a session with status " +
                $"{session.Status}.");
        }

        // A pending reschedule can be cancelled.
        if (session.Status == SessionStatus.NeedsReschedule)
        {
            return;
        }

        if (session.Status == SessionStatus.Pending)
        {
            EnsurePendingNotOverdue(session, now);
        }

        if (startUtc <= now)
        {
            throw new InvalidSessionTransitionException(
                "Cannot cancel a session after its start.");
        }

        if (actorRole == UserRole.Student &&
            session.Status == SessionStatus.Approved &&
            startUtc <= now.AddHours(2))
        {
            throw new InvalidSessionTransitionException(
                "Approved sessions can only be cancelled " +
                "more than 2 hours before the start.");
        }
    }

    // Normalize optional cancellation/rejection reason.
    private static string? NormalizeReason(string? reason)
    {
        var value = reason?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length > 500)
        {
            throw new ArgumentException(
                "Reason cannot exceed 500 characters.");
        }

        return value;
    }

    // Validate meeting information by session mode.
    private static string ValidateMeetingInfo(
        SessionMode mode,
        string? meetingInfo)
    {
        var value = meetingInfo?.Trim() ?? "";

        if (value.Length == 0 ||
            value.Length > 1500)
        {
            throw new ArgumentException(
                "Meeting information is required " +
                "and cannot exceed 1500 characters.");
        }

        switch (mode)
        {
            case SessionMode.Zoom:
                if (!Uri.TryCreate(
                        value,
                        UriKind.Absolute,
                        out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps ||
                    !IsZoomHost(uri.Host) ||
                    !string.IsNullOrEmpty(uri.UserInfo))
                {
                    throw new ArgumentException(
                        "Enter a valid HTTPS Zoom meeting link.");
                }
                break;

            case SessionMode.Call:
                var digits = new string(
                    value.Where(char.IsDigit).ToArray());

                var validCharacters = value.All(c =>
                    char.IsDigit(c) ||
                    c == '+' ||
                    c == ' ' ||
                    c == '(' ||
                    c == ')' ||
                    c == '-');

                if (!validCharacters ||
                    digits.Length < 10 ||
                    digits.Length > 15)
                {
                    throw new ArgumentException(
                        "Enter a valid phone number " +
                        "containing 10 to 15 digits.");
                }
                break;

            case SessionMode.Text:
                if (value.Length < 10)
                {
                    throw new ArgumentException(
                        "Chat instructions must contain " +
                        "at least 10 characters.");
                }
                break;

            default:
                throw new ArgumentException(
                    "Unsupported session mode.");
        }

        return value;
    }

    // Only accept legitimate Zoom domains.
    private static bool IsZoomHost(string host)
    {
        return host.Equals(
                   "zoom.us",
                   StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(
                   ".zoom.us",
                   StringComparison.OrdinalIgnoreCase) ||
               host.Equals(
                   "zoom.com",
                   StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(
                   ".zoom.com",
                   StringComparison.OrdinalIgnoreCase);
    }
}
