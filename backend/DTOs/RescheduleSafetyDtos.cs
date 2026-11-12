
namespace MentorBooking.Api.DTOs;

// One recorded change to the session start time.
public sealed record RescheduleHistoryEntryResponse(
    Guid Id,
    DateTime OldStartUtc,
    DateTime NewStartUtc,
    int MinutesShifted,
    string Reason,
    DateTime CreatedAtUtc
);

// Paginated history for a session participant.
public sealed record RescheduleHistoryResponse(
    Guid SessionId,
    string CurrentStatus,
    DateTime OriginalStartUtc,
    DateTime CurrentStartUtc,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<RescheduleHistoryEntryResponse> Entries
);

// Mentor checks a proposed time before rescheduling.
public sealed record CheckRescheduleRequest(
    Guid NewSlotId,
    DateTime NewStartUtc
);

// Conflict preview. This does not reserve the slot.
public sealed record RescheduleCheckResponse(
    bool CanReschedule,
    DateTime ProposedStartUtc,
    DateTime ProposedEndUtc,
    IReadOnlyList<string> Conflicts
);
