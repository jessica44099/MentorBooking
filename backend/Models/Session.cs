using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Models;

public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MentorId { get; set; }

    public Guid StudentId { get; set; }

    public Guid SlotId { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SessionMode Mode { get; set; }

    public string? MeetingInfo { get; set; }

    public DateTime ScheduledStartUtc { get; set; }

    public DateTime ScheduledEndUtc { get; set; }

    // Original time before any automatic shift.
    public DateTime OriginalStartUtc { get; set; }

    public DateTime? ActualStartUtc { get; set; }

    public DateTime? ActualEndUtc { get; set; }

    public SessionStatus Status { get; set; } =
        SessionStatus.Pending;

    public string? RejectReason { get; set; }

    // Application-managed concurrency token.
    public Guid RowVersion { get; set; } =
        Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}