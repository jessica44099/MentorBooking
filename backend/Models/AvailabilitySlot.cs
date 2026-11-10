using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Models;

public class AvailabilitySlot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MentorId { get; set; }

    public AvailabilityType Type { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string? Note { get; set; }

    // Groups slots created by weekly repetition.
    public Guid? RepeatSeriesId { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}