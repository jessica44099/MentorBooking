
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
// 2. SQLITE DATABASE CONFIGURATION
// =====================================================

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Database connection string is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// =====================================================
// 3. EMAIL DELIVERY CONFIGURATION — STEP 30.1
// =====================================================

builder.Services.Configure<EmailDeliveryOptions>(
    builder.Configuration.GetSection(
        EmailDeliveryOptions.SectionName));

// =====================================================
// 4. AUTHENTICATION SERVICES
// =====================================================

builder.Services.AddScoped<
    IAuthService,
    AuthService>();

// =====================================================
// 5. EMAIL SERVICES — STEP 30
// =====================================================

// Development:
//   Fake authentication emails.
//   Preview queued session emails.
//
// Other environments:
//   Real SMTP for authentication emails.
//   Real SMTP for queued session emails.

if (builder.Environment.IsDevelopment())
{
    // Development authentication emails.
    builder.Services.AddScoped<
        IEmailService,
        FakeEmailService>();

    // Development queued email previews.
    builder.Services.AddScoped<
        IQueuedEmailSender,
        DevelopmentQueuedEmailSender>();
}
else
{
    // Production/staging authentication emails.
    builder.Services.AddScoped<
        IEmailService,
        SmtpAccountEmailService>();

    // Production/staging queued email delivery.
    builder.Services.AddScoped<
        IQueuedEmailSender,
        SmtpQueuedEmailSender>();
}

// Email verification and password reset tokens.
builder.Services.AddScoped<
    IAccountTokenService,
    AccountTokenService>();

// =====================================================
// 6. MENTOR AVAILABILITY MANAGEMENT
// =====================================================

builder.Services.AddScoped<
    IAvailabilityService,
    AvailabilityService>();

// =====================================================
// 7. STUDENT BOOKING SERVICE
// =====================================================

builder.Services.AddScoped<
    IBookingService,
    BookingService>();

// =====================================================
// 8. SESSION LIFECYCLE SERVICE
// =====================================================

// Approve, reject and cancel sessions.
builder.Services.AddScoped<
    ISessionLifecycleService,
    SessionLifecycleService>();

// =====================================================
// 9. SESSION PROGRESS SERVICE
// =====================================================

// Start, complete and mark No-Show.
builder.Services.AddScoped<
    ISessionProgressService,
    SessionProgressService>();

// =====================================================
// 10. MENTOR PROFILE
// =====================================================

builder.Services.AddScoped<
    IMentorProfileService,
    MentorProfileService>();

// =====================================================
// 11. MENTOR DISCOVERY
// =====================================================

builder.Services.AddScoped<
    IMentorDiscoveryService,
    MentorDiscoveryService>();

// =====================================================
// 12. STUDENT REVIEWS
// =====================================================

builder.Services.AddScoped<
    IReviewService,
    ReviewService>();

// =====================================================
// 13. MENTOR DASHBOARD STATISTICS
// =====================================================

builder.Services.AddScoped<
    IMentorDashboardStatsService,
    MentorDashboardStatsService>();

// =====================================================
// 14. SESSION RECORD CORRECTIONS
// =====================================================

builder.Services.AddScoped<
    ISessionRecordCorrectionService,
    SessionRecordCorrectionService>();

// =====================================================
// 15. STEP 27 — SESSION RESCHEDULING
// =====================================================

// Mentor proposes a new session time.
// Student accepts the proposed time.

builder.Services.AddScoped<
    ISessionRescheduleService,
    SessionRescheduleService>();

// =====================================================
// 16. STEP 28 — RESCHEDULE SAFETY
// =====================================================

// Shift history and calendar conflict previews.

builder.Services.AddScoped<
    IRescheduleSafetyService,
    RescheduleSafetyService>();

// =====================================================
// 17. STEP 29 — IN-APP NOTIFICATIONS
// =====================================================

// Get the logged-in user's notifications.
// Count unread notifications.
// Mark one or all notifications as read.

builder.Services.AddScoped<
    INotificationService,
    NotificationService>();

// =====================================================
// 18. STEP 30.2 — EMAIL NOTIFICATION QUEUE
// =====================================================

// Creates queued email records in SQLite.
// Does not send them immediately.

builder.Services.AddScoped<
    IEmailNotificationQueue,
    EmailNotificationQueue>();

// =====================================================
// 19. BACKGROUND SERVICES
// =====================================================

// Expire overdue Pending bookings.
builder.Services.AddHostedService<
    PendingExpirationWorker>();

// Expire unanswered reschedule proposals.
builder.Services.AddHostedService<
    RescheduleExpirationWorker>();

// =====================================================
// 20. STEP 30.5 — BACKGROUND EMAIL WORKER
// =====================================================

// Register exactly once.
//
// When EmailDelivery:Enabled is false,
// the worker will not process queued emails.
//
// In Development, emails are previewed.
// In other environments, SMTP is used.

builder.Services.AddHostedService<
    QueuedEmailDeliveryWorker>();

// =====================================================
// 21. JWT AUTHENTICATION CONFIGURATION
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
        // Preserve the original JWT claim names.
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

// Enable role-based authorization.
builder.Services.AddAuthorization();

// =====================================================
// 22. BUILD THE APPLICATION
// =====================================================

// Angular development CORS policy.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDevelopment", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


var app = builder.Build();

// =====================================================
// 23. DEVELOPMENT DATABASE SEEDING
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
// 24. AUTHENTICATION SMOKE TEST
// =====================================================

if (builder.Configuration.GetValue<bool>("auth-smoke"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Authentication testing is development-only.");
    }

    var password =
        Environment.GetEnvironmentVariable(
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
// 25. ACCOUNT TOKEN SMOKE TEST
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
// 26. SWAGGER AND HTTPS
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
// 27. AUTHENTICATION AND AUTHORIZATION
// =====================================================

// Authentication MUST execute before authorization.


// Allow the local Angular development server.
if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDevelopment");
}

// Authentication must precede authorization.
app.UseAuthentication();

app.UseAuthorization();

// =====================================================
// 28. MAP API CONTROLLERS
// =====================================================

app.MapControllers();

// =====================================================
// 29. START APPLICATION
// =====================================================

app.Run();
