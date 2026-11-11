namespace MentorBooking.Api.Models;

public class SessionRecordAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public Guid ChangedByMentorId { get; set; }

    public int OldValue { get; set; }

    public int NewValue { get; set; }

    public DateTime ChangedAtUtc { get; set; } =
        DateTime.UtcNow;
}