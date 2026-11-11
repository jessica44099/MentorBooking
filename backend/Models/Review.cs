namespace MentorBooking.Api.Models;

public class Review
{
    public Guid SessionId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}