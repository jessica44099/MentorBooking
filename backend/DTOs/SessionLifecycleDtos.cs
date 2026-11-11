
namespace MentorBooking.Api.DTOs;

// Mentor must provide meeting information before approval.
public sealed record ApproveSessionRequest(
    string MeetingInfo
);

// Mentor can provide an optional rejection reason.
public sealed record RejectSessionRequest(
    string? Reason
);

// Mentor or student can provide a cancellation reason.
public sealed record CancelSessionRequest(
    string? Reason
);

// Safe response after a status change.
public sealed record SessionTransitionResponse(
    Guid SessionId,
    string Status,
    DateTime ChangedAtUtc,
    string Message
);

// Illegal status transitions map to HTTP 400.
public sealed class InvalidSessionTransitionException : Exception
{
    public InvalidSessionTransitionException(string message)
        : base(message)
    {
    }
}

// Concurrent updates map to HTTP 409.
public sealed class SessionLifecycleConflictException : Exception
{
    public SessionLifecycleConflictException(string message)
        : base(message)
    {
    }

    public SessionLifecycleConflictException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

// Permission failures map to HTTP 403.
public sealed class SessionLifecycleForbiddenException : Exception
{
    public SessionLifecycleForbiddenException(string message)
        : base(message)
    {
    }
}
