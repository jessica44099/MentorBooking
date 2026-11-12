
namespace MentorBooking.Api.DTOs;

// Student submits a rating and optional comment.
public sealed record CreateReviewRequest(
    int Rating,
    string? Comment
);

// Review visible to its author.
public sealed record SessionReviewResponse(
    Guid SessionId,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc
);

// Public review without student or session identifiers.
public sealed record PublicReviewResponse(
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc
);

// Paginated reviews and calculated mentor rating.
public sealed record MentorReviewsResponse(
    Guid MentorId,
    int ReviewCount,
    double? AverageRating,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<PublicReviewResponse> Items
);

public sealed class ReviewForbiddenException : Exception
{
    public ReviewForbiddenException(string message)
        : base(message)
    {
    }
}

public sealed class ReviewNotAllowedException : Exception
{
    public ReviewNotAllowedException(string message)
        : base(message)
    {
    }
}

public sealed class ReviewConflictException : Exception
{
    public ReviewConflictException(string message)
        : base(message)
    {
    }

    public ReviewConflictException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
