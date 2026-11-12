
namespace MentorBooking.Api.DTOs;

// Mentor requests a new session time.
public sealed record ProposeRescheduleRequest(
    Guid NewSlotId,
    DateTime NewStartUtc,
    string? Reason
);

// Response after a proposal or acceptance.
public sealed record RescheduleResponse(
    Guid SessionId,
    Guid SlotId,
    DateTime OriginalStartUtc,
    DateTime ScheduledStartUtc,
    DateTime ScheduledEndUtc,
    string Status,
    string Message
);
