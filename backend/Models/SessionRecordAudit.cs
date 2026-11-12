
namespace MentorBooking.Api.Models;

public class SessionRecordAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public Guid ChangedByMentorId { get; set; }

    // Existing columns: preserve old audit data.
    public int OldValue { get; set; }
    public int NewValue { get; set; }

    // Full before-and-after snapshots.
    // Nullable so existing historical audit rows
    // can remain in the database.
    public string? OldSummary { get; set; }
    public string? NewSummary { get; set; }

    public string? OldResolution { get; set; }
    public string? NewResolution { get; set; }

    public string? OldPrivateMentorNote { get; set; }
    public string? NewPrivateMentorNote { get; set; }

    public string? ChangeReason { get; set; }

    public DateTime ChangedAtUtc { get; set; } =
        DateTime.UtcNow;
}
