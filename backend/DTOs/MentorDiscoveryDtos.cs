
namespace MentorBooking.Api.DTOs;

// Public information students can see.
public sealed record PublicMentorResponse(
    Guid MentorId,
    string Name,
    string Bio,
    IReadOnlyList<string> Subjects
);

// Paginated search result.
public sealed record MentorSearchResponse(
    IReadOnlyList<PublicMentorResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);

// Used when a token belongs to a deleted or
// otherwise invalid Student account.
public sealed class MentorDiscoveryForbiddenException
    : Exception
{
    public MentorDiscoveryForbiddenException(
        string message) : base(message)
    {
    }
}
