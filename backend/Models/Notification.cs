namespace MentorBooking.Api.Models;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid? SessionId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime? ReadAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}