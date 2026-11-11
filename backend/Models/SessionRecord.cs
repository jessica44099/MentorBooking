using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Models;

public class SessionRecord
{
    public Guid SessionId { get; set; }

    public int DoubtsCleared { get; set; }

    public string Summary { get; set; } = string.Empty;

    public ResolutionStatus Resolution { get; set; }

    public string? PrivateMentorNote { get; set; }

    public DateTime CompletedAtUtc { get; set; }
}