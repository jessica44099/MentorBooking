
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
                    IsolationLevel.Serializable, ct);

            var actor = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == actorId && !x.IsDeleted,
                    ct);

            if (actor == null)
            {
                throw new SessionLifecycleForbiddenException(
                    "Account not found or inactive.");
            }

            var session = await _db.Sessions
                .FirstOrDefaultAsync(
                    x => x.Id == sessionId,
                    ct);

            if (session == null)
            {
                throw new KeyNotFoundException(
                    "Session not found.");
            }

            // Only the assigned mentor can approve or reject.
            // The assigned student or mentor can cancel.
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
                if (actor.Role == UserRole.Student)
                {
                    if (session.StudentId != actor.Id)
                    {
                        throw new KeyNotFoundException(
                            "Session not found.");
                    }
                }
                else if (actor.Role == UserRole.Mentor)
                {
                    if (session.MentorId != actor.Id)
                    {
                        throw new KeyNotFoundException(
                            "Session not found.");
                    }
                }
                else
                {
                    throw new SessionLifecycleForbiddenException(
                        "Invalid account role.");
                }
            }

            var now = DateTime.UtcNow;
            var startUtc = DateTime.SpecifyKind(
                session.ScheduledStartUtc,
                DateTimeKind.Utc);

            var previousStatus = session.Status;

            switch (action)
            {
                case ActionType.Approve:
                    if (!actor.IsEmailVerified)
                    {
                        throw new SessionLifecycleForbiddenException(
                            "Verify your email before approving sessions.");
                    }

                    EnsurePending(session, "approve");

                    if (startUtc <= now)
                    {
                        throw new InvalidSessionTransitionException(
                            "Cannot approve a session that has started.");
                    }

                    // Required for Zoom, Text and Call.
                    session.MeetingInfo = ValidateMeetingInfo(
                        session.Mode,
                        meetingInfo);

                    session.Status = SessionStatus.Approved;
                    session.RejectReason = null;
                    break;

                case ActionType.Reject:
                    EnsurePending(session, "reject");

                    if (startUtc <= now)
                    {
                        throw new InvalidSessionTransitionException(
                            "Cannot reject a session after its start.");
                    }

                    session.Status = SessionStatus.Rejected;
                    session.RejectReason = reason;
                    break;

                case ActionType.Cancel:
                    ValidateCancellation(
                        session,
                        actor.Role,
                        startUtc,
                        now);

                    session.Status = SessionStatus.Cancelled;
                    break;
            }

            // Application-managed optimistic concurrency.
            // EF will compare the original RowVersion on update.
            session.RowVersion = Guid.NewGuid();

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
                    ToStatus = session.Status,
                    ChangedByUserId = actor.Id,
                    Reason = historyReason,
                    ChangedAtUtc = now
                });

            // Session update and history must succeed together.
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
                session.Status.ToString(),
                now,
                message);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SessionLifecycleConflictException(
                "Session was changed by another request. " +
                "Please refresh and try again.",
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

        // NeedsReschedule sessions may be cancelled
        // without a timing penalty.
        if (session.Status == SessionStatus.NeedsReschedule)
        {
            return;
        }

        if (startUtc <= now)
        {
            throw new InvalidSessionTransitionException(
                "Cannot cancel a session after its start.");
        }

        // Students must cancel approved sessions
        // more than two hours before the start.
        if (actorRole == UserRole.Student &&
            session.Status == SessionStatus.Approved &&
            startUtc <= now.AddHours(2))
        {
            throw new InvalidSessionTransitionException(
                "Approved sessions can only be cancelled " +
                "more than 2 hours before the start.");
        }
    }

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

    private static string ValidateMeetingInfo(
        SessionMode mode,
        string? meetingInfo)
    {
        var value = meetingInfo?.Trim() ?? "";

        if (value.Length == 0 || value.Length > 1500)
        {
            throw new ArgumentException(
                "Meeting information is required " +
                "and cannot exceed 1500 characters.");
        }

        switch (mode)
        {
            case SessionMode.Zoom:
                if (!Uri.TryCreate(
                        value, UriKind.Absolute, out var uri) ||
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
