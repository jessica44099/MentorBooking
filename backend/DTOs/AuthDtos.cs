
namespace MentorBooking.Api.DTOs;

// Data required for registration.
public sealed record RegisterRequest(
    string Name,
    string Email,
    string Password,
    string ConfirmPassword,
    string Role
);

// Data required for login.
public sealed record LoginRequest(
    string Email,
    string Password
);

// Safe login response.
// Never return the password or password hash.
public sealed record AuthResponse(
    string Token,
    DateTime ExpiresAtUtc,
    Guid UserId,
    string Name,
    string Email,
    string Role,
    bool IsEmailVerified
);
