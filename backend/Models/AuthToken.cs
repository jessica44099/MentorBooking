using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Models;

public class AuthToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public AuthTokenType Type { get; set; }

    // Store a secure token hash, not the raw token.
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}