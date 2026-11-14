
using System.Text;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// 1. CONTROLLERS AND SWAGGER
// =====================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT access token."
        });

    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
});

// =====================================================
// 2. DATABASE CONFIGURATION
// =====================================================

// SQLite database connection.
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Database connection string is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// =====================================================
// 3. REGISTER APPLICATION SERVICES
// =====================================================

// Authentication.
builder.Services.AddScoped<
    IAuthService,
    AuthService>();

// Email services.
builder.Services.AddScoped<
    IEmailService,
    FakeEmailService>();

// Email verification and password reset tokens.
builder.Services.AddScoped<
    IAccountTokenService,
    AccountTokenService>();

// Mentor availability management.
builder.Services.AddScoped<
    IAvailabilityService,
    AvailabilityService>();

// Student session bookings.
builder.Services.AddScoped<
    IBookingService,
    BookingService>();

// Session approval, rejection and cancellation.
builder.Services.AddScoped<
    ISessionLifecycleService,
    SessionLifecycleService>();

// Session start, completion and No-Show.
builder.Services.AddScoped<
    ISessionProgressService,
    SessionProgressService>();

// Mentor profile management.
builder.Services.AddScoped<
    IMentorProfileService,
    MentorProfileService>();

// Mentor discovery and search.
builder.Services.AddScoped<
    IMentorDiscoveryService,
    MentorDiscoveryService>();

// Student reviews and mentor ratings.
builder.Services.AddScoped<
    IReviewService,
    ReviewService>();

// Mentor dashboard statistics.
builder.Services.AddScoped<
    IMentorDashboardStatsService,
    MentorDashboardStatsService>();

// Completed-session corrections and audit history.
builder.Services.AddScoped<
    ISessionRecordCorrectionService,
    SessionRecordCorrectionService>();

// =====================================================
// 4. STEP 27 — SESSION RESCHEDULING
// =====================================================

// Mentor proposes a new session time.
// Student accepts the proposed time.
builder.Services.AddScoped<
    ISessionRescheduleService,
    SessionRescheduleService>();

// =====================================================
// 5. STEP 28 — SHIFT HISTORY AND CONFLICT HANDLING
// =====================================================

// View previous rescheduling changes.
// Preview scheduling conflicts before proposing.
builder.Services.AddScoped<
    IRescheduleSafetyService,
    RescheduleSafetyService>();

// =====================================================
// 6. STEP 29 — STUDENT AND MENTOR NOTIFICATIONS
// =====================================================

// Get notifications for the authenticated user.
// Count unread notifications.
// Mark individual or all notifications as read.
builder.Services.AddScoped<
    INotificationService,
    NotificationService>();

// =====================================================
// 7. BACKGROUND SERVICES
// =====================================================

// Automatically expire overdue Pending bookings.
builder.Services.AddHostedService<
    PendingExpirationWorker>();

// Automatically expire unanswered reschedule proposals.
builder.Services.AddHostedService<
    RescheduleExpirationWorker>();

// =====================================================
// 8. JWT AUTHENTICATION CONFIGURATION
// =====================================================

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey) ||
    Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Configure Jwt:Key with at least 32 UTF-8 bytes.");
}

var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "JWT issuer is missing.");

var audience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "JWT audience is missing.");

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                RequireSignedTokens = true,
                RequireExpirationTime = true,

                ValidIssuer = issuer,
                ValidAudience = audience,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),

                NameClaimType = "name",
                RoleClaimType = "role",

                ClockSkew = TimeSpan.Zero
            };
    });

// Role-based authorization.
builder.Services.AddAuthorization();

// =====================================================
// 9. BUILD APPLICATION
// =====================================================

var app = builder.Build();

// =====================================================
// 10. DEVELOPMENT DATABASE SEEDING
// =====================================================

if (builder.Configuration.GetValue<bool>("seed"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Seeding is development-only.");
    }

    await using var scope =
        app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    await DevelopmentSeeder.RunAsync(db);

    return;
}

// =====================================================
// 11. AUTHENTICATION SMOKE TEST
// =====================================================

if (builder.Configuration.GetValue<bool>("auth-smoke"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Authentication testing is development-only.");
    }

    var password = Environment.GetEnvironmentVariable(
        "MENTORBOOKING_TEST_PASSWORD")
        ?? throw new InvalidOperationException(
            "Set MENTORBOOKING_TEST_PASSWORD.");

    await using var scope =
        app.Services.CreateAsyncScope();

    var auth = scope.ServiceProvider
        .GetRequiredService<IAuthService>();

    var result = await auth.LoginAsync(
        new LoginRequest(
            "mentor1@mentorbooking.test",
            password));

    if (result.Role != "Mentor" ||
        string.IsNullOrWhiteSpace(result.Token) ||
        result.ExpiresAtUtc <= DateTime.UtcNow)
    {
        throw new InvalidOperationException(
            "Authentication smoke test failed.");
    }

    Console.WriteLine(
        "PASS: BCrypt login verification and JWT creation.");

    return;
}

// =====================================================
// 12. ACCOUNT TOKEN SMOKE TEST
// =====================================================

if (builder.Configuration.GetValue<bool>("token-smoke"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Token testing is development-only.");
    }

    await using var scope =
        app.Services.CreateAsyncScope();

    var tokens = scope.ServiceProvider
        .GetRequiredService<IAccountTokenService>();

    await tokens.RequestPasswordResetAsync(
        "mentor1@mentorbooking.test");

    Console.WriteLine(
        "PASS: Password reset token request processed.");

    return;
}

// =====================================================
// 13. SWAGGER AND HTTPS
// =====================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

// =====================================================
// 14. AUTHENTICATION AND AUTHORIZATION
// =====================================================

// Authentication must run before authorization.
app.UseAuthentication();

app.UseAuthorization();

// =====================================================
// 15. MAP API CONTROLLERS
// =====================================================

app.MapControllers();

// =====================================================
// 16. START APPLICATION
// =====================================================

app.Run();
