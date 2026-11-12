
namespace MentorBooking.Api.DTOs;

// Data supplied when a mentor saves their profile.
public sealed record SaveMentorProfileRequest(
    string? Bio,
    string[]? Subjects
);

// Safe data returned to the frontend.
public sealed record MentorProfileResponse(
    Guid MentorId,
    string Name,
    string? Bio,
    IReadOnlyList<string> Subjects,
    bool IsComplete
);
