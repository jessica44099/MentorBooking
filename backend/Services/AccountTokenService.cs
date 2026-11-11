
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IAccountTokenService
{
    Task SendVerificationAsync(
        Guid userId,
        bool isResend = false,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<bool> ResetPasswordAsync(
        string token,
        string password,
        string confirmPassword,
        CancellationToken cancellationToken = default);
}

public sealed class AccountTokenService : IAccountTokenService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountTokenService> _logger;

    public AccountTokenService(
        AppDbContext db,
        IEmailService email,
        IConfiguration configuration,
        ILogger<AccountTokenService> logger)
    {
        _db = db;
        _email = email;
        _configuration = configuration;
        _logger = logger;
    }

    // Generate 32 cryptographically random bytes.
    private static string GenerateToken()
    {
        return Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));
    }

    // Only the SHA-256 hash is saved to AuthTokens.
    private static string HashToken(string token)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    token.ToUpperInvariant())));
    }

    private static bool HasValidTokenFormat(string? token)
    {
        return token != null &&
            Regex.IsMatch(token, @"\A[0-9a-fA-F]{64}\z");
    }

    private string BuildLink(string route, string token)
    {
        var baseUrl = _configuration["Frontend:BaseUrl"]
            ?? "http://localhost:4200";

        return baseUrl.TrimEnd('/') +
            "/" + route + "?token=" +
            Uri.EscapeDataString(token);
    }

    private async Task SendEmailSafelyAsync(
        string to,
        string subject,
        string heading,
        string link,
        CancellationToken ct)
    {
        var safeHeading = WebUtility.HtmlEncode(heading);
        var safeLink = WebUtility.HtmlEncode(link);

        var html =
            "<html><body>" +
            $"<h2>{safeHeading}</h2>" +
            $"<p><a href=\"{safeLink}\">Continue</a></p>" +
            "</body></html>";

        var text = heading + Environment.NewLine + link;

        try
        {
            await _email.SendAsync(
                to, subject, html, text, ct);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException)
        {
            // Email failure must not undo the user's action.
            _logger.LogError(
                ex, "Could not deliver account email.");
        }
    }

    // EMAIL VERIFICATION
    public async Task SendVerificationAsync(
        Guid userId,
        bool isResend = false,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        var user = await _db.Users.FirstOrDefaultAsync(
            x => x.Id == userId && !x.IsDeleted,
            cancellationToken);

        if (user == null)
        {
            throw new InvalidOperationException(
                "Account not found.");
        }

        if (user.IsEmailVerified)
        {
            return;
        }

        if (isResend)
        {
            var recentCount = await _db.AuthTokens.CountAsync(
                x => x.UserId == userId &&
                     x.Type ==
                         AuthTokenType.EmailVerificationResend &&
                     x.CreatedAtUtc >= now.AddHours(-1),
                cancellationToken);

            if (recentCount >= 3)
            {
                throw new InvalidOperationException(
                    "Maximum 3 verification resends per hour.");
            }
        }

        // Invalidate older verification tokens.
        await _db.AuthTokens
            .Where(x =>
                x.UserId == userId &&
                (x.Type == AuthTokenType.EmailVerification ||
                 x.Type ==
                     AuthTokenType.EmailVerificationResend) &&
                x.UsedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.UsedAtUtc, now),
                cancellationToken);

        var rawToken = GenerateToken();

        _db.AuthTokens.Add(new AuthToken
        {
            UserId = userId,
            Type = isResend
                ? AuthTokenType.EmailVerificationResend
                : AuthTokenType.EmailVerification,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = now.AddHours(24),
            CreatedAtUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var link = BuildLink("verify-email", rawToken);

        await SendEmailSafelyAsync(
            user.Email,
            "Verify your MentorBooking email",
            "Verify your email address. Link valid for 24 hours.",
            link,
            cancellationToken);
    }

    public async Task<bool> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!HasValidTokenFormat(token))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var hash = HashToken(token);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        var existing = await _db.AuthTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TokenHash == hash &&
                     (x.Type ==
                         AuthTokenType.EmailVerification ||
                      x.Type ==
                         AuthTokenType.EmailVerificationResend),
                cancellationToken);

        if (existing == null)
        {
            return false;
        }

        var user = await _db.Users.FirstOrDefaultAsync(
            x => x.Id == existing.UserId && !x.IsDeleted,
            cancellationToken);

        if (user == null)
        {
            return false;
        }

        // Conditional update enforces single use.
        var changed = await _db.AuthTokens
            .Where(x => x.Id == existing.Id &&
                        x.UsedAtUtc == null &&
                        x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.UsedAtUtc, now),
                cancellationToken);

        if (changed != 1)
        {
            return false;
        }

        user.IsEmailVerified = true;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    // FORGOT PASSWORD
    public async Task RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail =
            (email ?? "").Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(
            x => x.Email == normalizedEmail && !x.IsDeleted,
            cancellationToken);

        // Do nothing for unknown emails.
        // The future API will return the same response.
        if (user == null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        // Invalidate earlier unused reset links.
        await _db.AuthTokens
            .Where(x => x.UserId == user.Id &&
                        x.Type == AuthTokenType.PasswordReset &&
                        x.UsedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.UsedAtUtc, now),
                cancellationToken);

        var rawToken = GenerateToken();

        _db.AuthTokens.Add(new AuthToken
        {
            UserId = user.Id,
            Type = AuthTokenType.PasswordReset,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = now.AddMinutes(30),
            CreatedAtUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var link = BuildLink("reset-password", rawToken);

        await SendEmailSafelyAsync(
            user.Email,
            "Reset your MentorBooking password",
            "Reset your password. Link valid for 30 minutes.",
            link,
            cancellationToken);
    }

    public async Task<bool> ResetPasswordAsync(
        string token,
        string password,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (password != confirmPassword)
        {
            throw new ArgumentException(
                "Passwords do not match.");
        }

        if (string.IsNullOrEmpty(password) ||
            password.Length < 8 ||
            !password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit) ||
            Encoding.UTF8.GetByteCount(password) > 72)
        {
            throw new ArgumentException(
                "Password must contain at least 8 characters, " +
                "one letter and one number, and be at most " +
                "72 UTF-8 bytes.");
        }

        if (!HasValidTokenFormat(token))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var hash = HashToken(token);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        var existing = await _db.AuthTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TokenHash == hash &&
                     x.Type == AuthTokenType.PasswordReset,
                cancellationToken);

        if (existing == null)
        {
            return false;
        }

        var user = await _db.Users.FirstOrDefaultAsync(
            x => x.Id == existing.UserId && !x.IsDeleted,
            cancellationToken);

        if (user == null)
        {
            return false;
        }

        var changed = await _db.AuthTokens
            .Where(x => x.Id == existing.Id &&
                        x.UsedAtUtc == null &&
                        x.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.UsedAtUtc, now),
                cancellationToken);

        if (changed != 1)
        {
            return false;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(
            password, workFactor: 12);

        await _db.SaveChangesAsync(cancellationToken);

        // Invalidate all other unused reset tokens.
        await _db.AuthTokens
            .Where(x =>
                x.UserId == user.Id &&
                x.Type == AuthTokenType.PasswordReset &&
                x.UsedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.UsedAtUtc, now),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
