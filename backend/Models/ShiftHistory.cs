namespace MentorBooking.Api.Models;

public class ShiftHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public Guid? CausedBySessionId { get; set; }

    public DateTime OldStartUtc { get; set; }

    public DateTime NewStartUtc { get; set; }

    public int MinutesShifted { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}