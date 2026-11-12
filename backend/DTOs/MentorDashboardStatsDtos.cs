
namespace MentorBooking.Api.DTOs;

// Statistics returned to the authenticated mentor.
// Never includes private student information.
public sealed record MentorDashboardStatsResponse(
    Guid MentorId,
    int CompletedSessions,
    int StudentsHelped,
    int DoubtsCleared,
    double HoursMentored,
    double? AverageRating,
    int ReviewCount,
    int PendingRequests,
    int UpcomingApprovedSessions
);
