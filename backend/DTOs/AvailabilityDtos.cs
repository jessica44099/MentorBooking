
namespace MentorBooking.Api.DTOs;

// Data needed to create or edit a calendar slot.
public sealed record SaveAvailabilitySlotRequest(
    string Type,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Note
);

// Safe data returned to the frontend.
public sealed record AvailabilitySlotResponse(
    Guid Id,
    Guid MentorId,
    string Type,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Note
);

// Details of an existing conflicting slot.
public sealed record AvailabilityClashResponse(
    Guid Id,
    string Type,
    DateTime StartUtc,
    DateTime EndUtc
);

// Exception representing a calendar conflict.
// API controllers will translate this into HTTP 409.
public sealed class AvailabilityConflictException : Exception
{
    public IReadOnlyList<AvailabilityClashResponse> Clashes
        { get; }

    public AvailabilityConflictException(
        string message,
        IReadOnlyList<AvailabilityClashResponse>? clashes = null)
        : base(message)
    {
        Clashes = clashes ??
            Array.Empty<AvailabilityClashResponse>();
    }
}
