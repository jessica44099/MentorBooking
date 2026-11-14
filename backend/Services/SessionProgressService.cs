
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface ISessionProgressService
{
    Task<SessionTransitionResponse> StartAsync(
        Guid mentorId,
        Guid sessionId,
        CancellationToken ct = default);

    Task<SessionTransitionResponse> CompleteAsync(
        Guid mentorId,
        Guid sessionId,
        CompleteSessionRequest request,
        CancellationToken ct = default);

    Task<SessionTransitionResponse> MarkNoShowAsync(
        Guid mentorId,
        Guid sessionId,
        CancellationToken ct = default);
}

public sealed class SessionProgressService
    : ISessionProgressService
{
    private readonly AppDbContext _db;

    private enum ProgressAction
    {
        Start,
        Complete,
        NoShow
    }

    private sealed record ValidatedCompletion(
        int DoubtsCleared,
        string Summary,
        ResolutionStatus Resolution,
        string? PrivateMentorNote
    );

    public SessionProgressService(AppDbContext db)
    {
        _db = db;
    }

    // =====================================================
    // 1. START A SESSION
    // =====================================================

    public Task<SessionTransitionResponse> StartAsync(
        Guid mentorId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        return TransitionAsync(
            mentorId,
            sessionId,
            ProgressAction.Start,
            null,
            ct);
    }

    // =====================================================
    // 2. COMPLETE A SESSION
    // =====================================================

    public Task<SessionTransitionResponse> CompleteAsync(
        Guid mentorId,
        Guid sessionId,
        CompleteSessionRequest request,
        CancellationToken ct = default)
    {
        return TransitionAsync(
            mentorId,
            sessionId,
            ProgressAction.Complete,
            request,
            ct);
    }

    // =====================================================
    // 3. MARK A SESSION AS NO-SHOW
    // =====================================================

    public Task<SessionTransitionResponse> MarkNoShowAsync(
        Guid mentorId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        return TransitionAsync(
            mentorId,
            sessionId,
            ProgressAction.NoShow,
            null,
            ct);
    }

    // =====================================================
    // 4. COMMON SESSION TRANSITION METHOD
    // =====================================================

    private async Task<SessionTransitionResponse> TransitionAsync(
        Guid mentorId,
        Guid sessionId,
        ProgressAction action,
        CompleteSessionRequest? request,
        CancellationToken ct)
    {
        // Validate completion details before modifying data.
        ValidatedCompletion? completion =
            action == ProgressAction.Complete
                ? ValidateCompletion(request)
                : null;

        try
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            // =================================================
            // 5. VALIDATE MENTOR
            // =====================================================

            var mentor = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == mentorId &&
                         !x.IsDeleted &&
                         x.Role == UserRole.Mentor,
                    ct);

            if (mentor == null)
            {
                throw new SessionLifecycleForbiddenException(
                    "Only active mentors can manage sessions.");
            }

            if (!mentor.IsEmailVerified)
            {
                throw new SessionLifecycleForbiddenException(
                    "Verify your email before managing sessions.");
            }

            // =================================================
            // 6. LOAD SESSION
            // =====================================================

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

            var now = DateTime.UtcNow;

            var scheduledStart = DateTime.SpecifyKind(
                session.ScheduledStartUtc,
                DateTimeKind.Utc);

            var scheduledEnd = DateTime.SpecifyKind(
                session.ScheduledEndUtc,
                DateTimeKind.Utc);

            var previousStatus = session.Status;

            SessionStatus newStatus;
            string historyReason;
            string successMessage;

            // =================================================
            // 7. VALIDATE SESSION ACTION
            // =====================================================

            switch (action)
            {
                case ProgressAction.Start:

                    if (session.Status != SessionStatus.Approved)
                    {
                        throw new InvalidSessionTransitionException(
                            "Only Approved sessions can be started.");
                    }

                    if (session.ActualStartUtc.HasValue)
                    {
                        throw new InvalidSessionTransitionException(
                            "This session has already started.");
                    }

                    if (now < scheduledStart.AddMinutes(-10))
                    {
                        throw new InvalidSessionTransitionException(
                            "Sessions can start at most " +
                            "10 minutes before their scheduled time.");
                    }

                    if (now >= scheduledEnd)
                    {
                        throw new InvalidSessionTransitionException(
                            "The scheduled session time has passed.");
                    }

                    newStatus = SessionStatus.InProgress;
                    historyReason = "Mentor started the session.";
                    successMessage = "Session started successfully.";

                    break;

                case ProgressAction.Complete:

                    if (session.Status != SessionStatus.InProgress)
                    {
                        throw new InvalidSessionTransitionException(
                            "Only InProgress sessions can be completed.");
                    }

                    if (!session.ActualStartUtc.HasValue)
                    {
                        throw new InvalidSessionTransitionException(
                            "The session has no recorded start time.");
                    }

                    var actualStart = DateTime.SpecifyKind(
                        session.ActualStartUtc.Value,
                        DateTimeKind.Utc);

                    if (actualStart > now)
                    {
                        throw new InvalidSessionTransitionException(
                            "Session start time is invalid.");
                    }

                    var recordExists = await _db.SessionRecords
                        .AnyAsync(
                            x => x.SessionId == session.Id,
                            ct);

                    if (recordExists)
                    {
                        throw new SessionLifecycleConflictException(
                            "A completion record already exists.");
                    }

                    newStatus = SessionStatus.Completed;
                    historyReason = "Mentor completed the session.";
                    successMessage = "Session completed successfully.";

                    break;

                case ProgressAction.NoShow:

                    if (session.Status != SessionStatus.Approved)
                    {
                        throw new InvalidSessionTransitionException(
                            "Only Approved sessions can be " +
                            "marked No-Show.");
                    }

                    if (session.ActualStartUtc.HasValue)
                    {
                        throw new InvalidSessionTransitionException(
                            "A started session cannot be " +
                            "marked No-Show.");
                    }

                    if (now < scheduledEnd)
                    {
                        throw new InvalidSessionTransitionException(
                            "Wait until the scheduled session " +
                            "has ended before marking No-Show.");
                    }

                    newStatus = SessionStatus.NoShow;
                    historyReason =
                        "Mentor marked the session No-Show.";
                    successMessage =
                        "Session marked No-Show successfully.";

                    break;

                default:

                    throw new InvalidOperationException(
                        "Unsupported session action.");
            }

            // =================================================
            // 8. CONCURRENCY PROTECTION
            // =====================================================

            var originalVersion = session.RowVersion;
            var nextVersion = Guid.NewGuid();

            var matchingSession = _db.Sessions.Where(
                x => x.Id == sessionId &&
                     x.MentorId == mentorId &&
                     x.Status == previousStatus &&
                     x.RowVersion == originalVersion);

            int updatedRows;

            // =================================================
            // 9. START SESSION
            // =====================================================

            if (action == ProgressAction.Start)
            {
                updatedRows = await matchingSession
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                newStatus)
                            .SetProperty(
                                x => x.RowVersion,
                                nextVersion)
                            .SetProperty(
                                x => x.ActualStartUtc,
                                (DateTime?)now),
                        ct);
            }

            // =================================================
            // 10. COMPLETE SESSION
            // =====================================================

            else if (action == ProgressAction.Complete)
            {
                updatedRows = await matchingSession
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                newStatus)
                            .SetProperty(
                                x => x.RowVersion,
                                nextVersion)
                            .SetProperty(
                                x => x.ActualEndUtc,
                                (DateTime?)now),
                        ct);
            }

            // =================================================
            // 11. MARK SESSION NO-SHOW
            // =====================================================

            else
            {
                updatedRows = await matchingSession
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.Status,
                                newStatus)
                            .SetProperty(
                                x => x.RowVersion,
                                nextVersion),
                        ct);
            }

            if (updatedRows != 1)
            {
                throw new SessionLifecycleConflictException(
                    "The session changed during this request. " +
                    "Refresh and try again.");
            }

            // =================================================
            // 12. SAVE COMPLETED SESSION RECORD
            // =====================================================

            if (action == ProgressAction.Complete)
            {
                _db.SessionRecords.Add(new SessionRecord
                {
                    SessionId = session.Id,
                    DoubtsCleared = completion!.DoubtsCleared,
                    Summary = completion.Summary,
                    Resolution = completion.Resolution,
                    PrivateMentorNote =
                        completion.PrivateMentorNote,
                    CompletedAtUtc = now
                });
            }

            // =================================================
            // 13. RECORD SESSION STATUS HISTORY
            // =====================================================

            _db.SessionStatusHistory.Add(
                new SessionStatusHistory
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    FromStatus = previousStatus,
                    ToStatus = newStatus,
                    ChangedByUserId = mentorId,
                    Reason = historyReason,
                    ChangedAtUtc = now
                });

            // =================================================
            // 14. STEP 29 — CREATE STUDENT NOTIFICATION
            // =====================================================

            // NotificationEvents.ProgressChanged creates
            // a notification only for:
            //
            // Completed -> "Session completed"
            // NoShow    -> "Session marked No-Show"
            //
            // InProgress does not create a notification.

            NotificationEvents.ProgressChanged(
                _db,
                session,
                newStatus,
                now);

            // =================================================
            // 15. SAVE AND COMMIT
            // =====================================================

            // Save the session record, status history,
            // and any generated notification together
            // in the same transaction as the status update.

            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            return new SessionTransitionResponse(
                session.Id,
                newStatus.ToString(),
                now,
                successMessage);
        }

        // =================================================
        // ERROR HANDLING
        // =====================================================

        catch (SqliteException ex)
            when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new SessionLifecycleConflictException(
                "The session is being updated. Please retry.",
                ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SessionLifecycleConflictException(
                "The session changed during this request.",
                ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode is 5 or 6 or 19)
        {
            throw new SessionLifecycleConflictException(
                "The session could not be updated. " +
                "Refresh and try again.",
                ex);
        }
    }

    // =====================================================
    // 16. VALIDATE SESSION COMPLETION DETAILS
    // =====================================================

    private static ValidatedCompletion ValidateCompletion(
        CompleteSessionRequest? request)
    {
        if (request == null)
        {
            throw new ArgumentException(
                "Completion details are required.");
        }

        if (request.DoubtsCleared < 0 ||
            request.DoubtsCleared > 20)
        {
            throw new ArgumentException(
                "Doubts cleared must be between 0 and 20.");
        }

        var summary = request.Summary?.Trim() ?? "";

        if (summary.Length < 1 || summary.Length > 500)
        {
            throw new ArgumentException(
                "Summary must contain 1 to 500 characters.");
        }

        var resolutionText =
            request.Resolution?.Trim() ?? "";

        if (!Enum.TryParse<ResolutionStatus>(
                resolutionText,
                true,
                out var resolution) ||
            !Enum.IsDefined(resolution) ||
            !string.Equals(
                resolutionText,
                resolution.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Resolution must be FullyResolved, " +
                "PartlyResolved or NeedsFollowUp.");
        }

        var privateNote =
            request.PrivateMentorNote?.Trim();

        if (privateNote?.Length > 2000)
        {
            throw new ArgumentException(
                "Private note cannot exceed 2000 characters.");
        }

        if (string.IsNullOrWhiteSpace(privateNote))
        {
            privateNote = null;
        }

        return new ValidatedCompletion(
            request.DoubtsCleared,
            summary,
            resolution,
            privateNote);
    }
}
