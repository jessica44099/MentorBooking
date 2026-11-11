
namespace MentorBooking.Api.DTOs;

// Registration request.
public sealed record RegisterRequest(
    string Name,
    string Email,
    string Password,
    string ConfirmPassword,
    string Role
);

// Login request.
public sealed record LoginRequest(
    string Email,
    string Password
);

// Login and registration response.
public sealed record AuthResponse(
    string Token,
    DateTime ExpiresAtUtc,
    Guid UserId,
    string Name,
    string Email,
    string Role,
    bool IsEmailVerified
);

// Verify email request.
public sealed record VerifyEmailRequest(
    string Token
);

// Forgot password request.
public sealed record ForgotPasswordRequest(
    string Email
);

// Reset password request.
public sealed record ResetPasswordRequest(
    string Token,
    string Password,
    string ConfirmPassword
);
