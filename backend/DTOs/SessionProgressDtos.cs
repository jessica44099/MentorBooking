
namespace MentorBooking.Api.DTOs;

// Required when a mentor completes a session.
public sealed record CompleteSessionRequest(
    int DoubtsCleared,
    string Summary,
    string Resolution,
    string? PrivateMentorNote
);
