
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface ISessionRecordCorrectionService
{
    Task<UpdatedSessionRecordResponse> UpdateAsync(
        Guid mentorId,
        Guid sessionId,
        UpdateSessionRecordRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<SessionRecordAuditResponse>> GetAuditAsync(
        Guid mentorId,
        Guid sessionId,
        CancellationToken ct = default);
}

public sealed class SessionRecordCorrectionService
    : ISessionRecordCorrectionService
{
    private readonly AppDbContext _db;

    private sealed record ValidatedCorrection(
        int DoubtsCleared,
        string Summary,
        ResolutionStatus Resolution,
        string? PrivateMentorNote,
        string ChangeReason
    );

    public SessionRecordCorrectionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UpdatedSessionRecordResponse> UpdateAsync(
        Guid mentorId,
        Guid sessionId,
        UpdateSessionRecordRequest request,
        CancellationToken ct = default)
    {
        var correction = Validate(request);

        try
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            await EnsureOwnedCompletedSessionAsync(
                mentorId,
                sessionId,
                ct);

            // Load the existing record without tracking.
            var previous = await _db.SessionRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.SessionId == sessionId,
                    ct);

            if (previous == null)
            {
                throw new KeyNotFoundException(
                    "Completion record not found.");
            }

            var hasChanges =
                previous.DoubtsCleared != correction.DoubtsCleared ||
                previous.Summary != correction.Summary ||
                previous.Resolution != correction.Resolution ||
                previous.PrivateMentorNote !=
                    correction.PrivateMentorNote;

            if (!hasChanges)
            {
                throw new ArgumentException(
                    "No changes were supplied.");
            }

            // Compare the original values during the UPDATE.
            // This stops an overlapping correction from
            // silently overwriting a changed database row.
            var matchingRecord = _db.SessionRecords
                .Where(x =>
                    x.SessionId == sessionId &&
                    x.DoubtsCleared == previous.DoubtsCleared &&
                    x.Summary == previous.Summary &&
                    x.Resolution == previous.Resolution &&
                    x.PrivateMentorNote ==
                        previous.PrivateMentorNote);

            var updatedRows = await matchingRecord
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.DoubtsCleared,
                            correction.DoubtsCleared)
                        .SetProperty(
                            x => x.Summary,
                            correction.Summary)
                        .SetProperty(
                            x => x.Resolution,
                            correction.Resolution)
                        .SetProperty(
                            x => x.PrivateMentorNote,
                            correction.PrivateMentorNote),
                    ct);

            if (updatedRows != 1)
            {
                throw new SessionLifecycleConflictException(
                    "This completion record changed " +
                    "during the request. Refresh and retry.");
            }

            var now = DateTime.UtcNow;

            // Save the old and new values in one audit entry.
            _db.SessionRecordAudits.Add(
                new SessionRecordAudit
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    ChangedByMentorId = mentorId,

                    OldValue = previous.DoubtsCleared,
                    NewValue = correction.DoubtsCleared,

                    OldSummary = previous.Summary,
                    NewSummary = correction.Summary,

                    OldResolution =
                        previous.Resolution.ToString(),
                    NewResolution =
                        correction.Resolution.ToString(),

                    OldPrivateMentorNote =
                        previous.PrivateMentorNote,
                    NewPrivateMentorNote =
                        correction.PrivateMentorNote,

                    ChangeReason = correction.ChangeReason,
                    ChangedAtUtc = now
                });

            // The record update and audit insertion
            // succeed together or roll back together.
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new UpdatedSessionRecordResponse(
                SessionId: sessionId,
                DoubtsCleared: correction.DoubtsCleared,
                Summary: correction.Summary,
                Resolution: correction.Resolution.ToString(),
                PrivateMentorNote:
                    correction.PrivateMentorNote,
                CompletedAtUtc: DateTime.SpecifyKind(
                    previous.CompletedAtUtc,
                    DateTimeKind.Utc),
                CorrectedAtUtc: now
            );
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new SessionLifecycleConflictException(
                "The record is being updated. Retry shortly.",
                ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SessionLifecycleConflictException(
                "The record changed during this request.",
                ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode is 5 or 6 or 19)
        {
            throw new SessionLifecycleConflictException(
                "The record or audit could not be saved.",
                ex);
        }
    }

    // Read full audit history for the assigned mentor only.
    public async Task<IReadOnlyList<SessionRecordAuditResponse>>
        GetAuditAsync(
            Guid mentorId,
            Guid sessionId,
            CancellationToken ct = default)
    {
        await EnsureOwnedCompletedSessionAsync(
            mentorId,
            sessionId,
            ct);

        var recordExists = await _db.SessionRecords
            .AsNoTracking()
            .AnyAsync(
                x => x.SessionId == sessionId,
                ct);

        if (!recordExists)
        {
            throw new KeyNotFoundException(
                "Completion record not found.");
        }

        var audits = await _db.SessionRecordAudits
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.ChangedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        return audits.Select(x =>
            new SessionRecordAuditResponse(
                AuditId: x.Id,
                SessionId: x.SessionId,
                ChangedByMentorId: x.ChangedByMentorId,
                OldDoubtsCleared: x.OldValue,
                NewDoubtsCleared: x.NewValue,
                OldSummary: x.OldSummary,
                NewSummary: x.NewSummary,
                OldResolution: x.OldResolution,
                NewResolution: x.NewResolution,
                OldPrivateMentorNote:
                    x.OldPrivateMentorNote,
                NewPrivateMentorNote:
                    x.NewPrivateMentorNote,
                ChangeReason: x.ChangeReason,
                ChangedAtUtc: DateTime.SpecifyKind(
                    x.ChangedAtUtc,
                    DateTimeKind.Utc)
            )).ToList();
    }

    private async Task EnsureOwnedCompletedSessionAsync(
        Guid mentorId,
        Guid sessionId,
        CancellationToken ct)
    {
        var mentor = await _db.Users
            .AsNoTracking()
            .Where(x =>
                x.Id == mentorId &&
                x.Role == UserRole.Mentor &&
                !x.IsDeleted)
            .Select(x => new { x.IsEmailVerified })
            .FirstOrDefaultAsync(ct);

        if (mentor == null)
        {
            throw new SessionLifecycleForbiddenException(
                "An active Mentor account is required.");
        }

        if (!mentor.IsEmailVerified)
        {
            throw new SessionLifecycleForbiddenException(
                "Verify your email before accessing records.");
        }

        var session = await _db.Sessions
            .AsNoTracking()
            .Where(x =>
                x.Id == sessionId &&
                x.MentorId == mentorId)
            .Select(x => new { x.Status })
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            // Don't reveal another mentor's session.
            throw new KeyNotFoundException(
                "Session not found.");
        }

        if (session.Status != SessionStatus.Completed)
        {
            throw new InvalidSessionTransitionException(
                "Only Completed session records can " +
                "be corrected or audited.");
        }
    }

    private static ValidatedCorrection Validate(
        UpdateSessionRecordRequest request)
    {
        if (request == null)
        {
            throw new ArgumentException(
                "Correction details are required.");
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
                "Private mentor note cannot exceed " +
                "2000 characters.");
        }

        if (string.IsNullOrWhiteSpace(privateNote))
        {
            privateNote = null;
        }

        var reason = request.ChangeReason?.Trim() ?? "";

        if (reason.Length < 10 || reason.Length > 500)
        {
            throw new ArgumentException(
                "Correction reason must contain " +
                "10 to 500 characters.");
        }

        return new ValidatedCorrection(
            DoubtsCleared: request.DoubtsCleared,
            Summary: summary,
            Resolution: resolution,
            PrivateMentorNote: privateNote,
            ChangeReason: reason
        );
    }
}

