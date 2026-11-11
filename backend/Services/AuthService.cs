
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace MentorBooking.Api.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public AuthService(
        AppDbContext db,
        IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<AuthResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? "";
        var email = NormalizeEmail(request.Email);

        if (name.Length < 2 || name.Length > 150)
        {
            throw new ArgumentException(
                "Name must contain 2 to 150 characters.");
        }

        ValidateEmail(email);

        var role = request.Role?.Trim().ToLowerInvariant() switch
        {
            "mentor" => UserRole.Mentor,
            "student" => UserRole.Student,
            _ => throw new ArgumentException(
                "Choose either Mentor or Student.")
        };

        ValidatePassword(request.Password);

        if (request.Password != request.ConfirmPassword)
        {
            throw new ArgumentException(
                "Passwords do not match.");
        }

        var emailExists = await _db.Users.AnyAsync(
            user => user.Email.ToLower() == email,
            cancellationToken);

        if (emailExists)
        {
            throw new InvalidOperationException(
                "Email is already registered.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                request.Password,
                workFactor: 12),
            Role = role,
            IsEmailVerified = false,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "Email is already registered.", ex);
        }

        // Registration creates an unverified account.
        // Verification tokens and email delivery come later.
        return GenerateAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrEmpty(request.Password))
        {
            throw new UnauthorizedAccessException(
                "Invalid email or password.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Email.ToLower() == email && !u.IsDeleted,
            cancellationToken);

        if (user == null)
        {
            throw new UnauthorizedAccessException(
                "Invalid email or password.");
        }

        bool passwordValid;

        try
        {
            passwordValid = BCrypt.Net.BCrypt.Verify(
                request.Password,
                user.PasswordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            passwordValid = false;
        }

        if (!passwordValid)
        {
            throw new UnauthorizedAccessException(
                "Invalid email or password.");
        }

        // Unverified users may log in.
        // Booking permissions will be checked separately.
        return GenerateAuthResponse(user);
    }

    private AuthResponse GenerateAuthResponse(User user)
    {
        var key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT signing key is missing.");

        var issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT issuer is missing.");

        var audience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT audience is missing.");

        var expiresMinutes = _configuration.GetValue<int>(
            "Jwt:ExpiresMinutes", 60);

        if (expiresMinutes <= 0)
        {
            throw new InvalidOperationException(
                "JWT expiration must be positive.");
        }

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(expiresMinutes);

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Email,
                user.Email),

            new Claim("name", user.Name),

            new Claim("role", user.Role.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString())
        };

        var securityKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(key));

        var credentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler()
            .WriteToken(token);

        return new AuthResponse(
            Token: tokenString,
            ExpiresAtUtc: expires,
            UserId: user.Id,
            Name: user.Name,
            Email: user.Email,
            Role: user.Role.ToString(),
            IsEmailVerified: user.IsEmailVerified
        );
    }

    private static string NormalizeEmail(string? email)
    {
        return email?.Trim().ToLowerInvariant() ?? "";
    }

    private static void ValidateEmail(string email)
    {
        if (email.Length == 0 || email.Length > 320 ||
            !MailAddress.TryCreate(email, out var address) ||
            !string.Equals(
                address.Address,
                email,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Enter a valid email address.");
        }
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) ||
            password.Length < 8 ||
            !password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit) ||
            Encoding.UTF8.GetByteCount(password) > 72)
        {
            throw new ArgumentException(
                "Password must have at least 8 characters, " +
                "including one letter and one number, " +
                "and be at most 72 UTF-8 bytes.");
        }
    }
}
