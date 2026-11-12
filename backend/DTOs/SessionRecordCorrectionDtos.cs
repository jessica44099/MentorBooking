
namespace MentorBooking.Api.DTOs;

// The mentor submits the complete corrected record.
public sealed record UpdateSessionRecordRequest(
    int DoubtsCleared,
    string? Summary,
    string? Resolution,
    string? PrivateMentorNote,
    string? ChangeReason
);

// Returned after a successful correction.
public sealed record UpdatedSessionRecordResponse(
    Guid SessionId,
    int DoubtsCleared,
    string Summary,
    string Resolution,
    string? PrivateMentorNote,
    DateTime CompletedAtUtc,
    DateTime CorrectedAtUtc
);

// Private audit history, visible only to
// the mentor assigned to the session.
public sealed record SessionRecordAuditResponse(
    Guid AuditId,
    Guid SessionId,
    Guid ChangedByMentorId,
    int OldDoubtsCleared,
    int NewDoubtsCleared,
    string? OldSummary,
    string? NewSummary,
    string? OldResolution,
    string? NewResolution,
    string? OldPrivateMentorNote,
    string? NewPrivateMentorNote,
    string? ChangeReason,
    DateTime ChangedAtUtc
);
