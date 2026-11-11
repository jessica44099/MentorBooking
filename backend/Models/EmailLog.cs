using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Models;

public class EmailLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ToAddress { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string TextBody { get; set; } = string.Empty;

    public EmailStatus Status { get; set; } =
        EmailStatus.Queued;

    public int AttemptCount { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime? SentAtUtc { get; set; }
}