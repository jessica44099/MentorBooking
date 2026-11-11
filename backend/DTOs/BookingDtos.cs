
namespace MentorBooking.Api.DTOs;

// Data a student sends to request a session.
public sealed record CreateBookingRequest(
    Guid MentorId,
    Guid SlotId,
    string Topic,
    string Description,
    DateTime ScheduledStartUtc,
    int DurationMinutes,
    string Mode
);

// Safe booking data returned to the frontend.
public sealed record BookingResponse(
    Guid Id,
    Guid MentorId,
    Guid StudentId,
    Guid SlotId,
    string Topic,
    string Description,
    string Mode,
    DateTime ScheduledStartUtc,
    DateTime ScheduledEndUtc,
    string Status,
    DateTime CreatedAtUtc
);

// Maps to HTTP 409 in the future controller.
public sealed class BookingConflictException : Exception
{
    public BookingConflictException(string message)
        : base(message)
    {
    }

    public BookingConflictException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

// Maps to HTTP 403 in the future controller.
public sealed class BookingForbiddenException : Exception
{
    public BookingForbiddenException(string message)
        : base(message)
    {
    }
}
