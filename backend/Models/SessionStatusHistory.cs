using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Models;

public class SessionStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public SessionStatus? FromStatus { get; set; }

    public SessionStatus ToStatus { get; set; }

    public Guid? ChangedByUserId { get; set; }

    public string? Reason { get; set; }

    public DateTime ChangedAtUtc { get; set; } =
        DateTime.UtcNow;
}