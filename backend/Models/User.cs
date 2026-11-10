using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool IsEmailVerified { get; set; } = false;

    // Mentor profile fields.
    public string? Bio { get; set; }

    public string? SubjectsJson { get; set; }

    // Soft deletion keeps completed-session history.
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}