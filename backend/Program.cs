
using System.Text;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Controllers and Swagger.
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

// SQLite database.
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Database connection string is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Application services.
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, FakeEmailService>();
builder.Services.AddScoped<
    IAccountTokenService, AccountTokenService>();

// JWT authentication.
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

builder.Services.AddAuthorization();

var app = builder.Build();

// Development database seeding.
if (builder.Configuration.GetValue<bool>("seed"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Seeding is development-only.");
    }

    await using var scope = app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    await DevelopmentSeeder.RunAsync(db);

    return;
}

// Authentication smoke test.
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

    await using var scope = app.Services.CreateAsyncScope();

    var auth = scope.ServiceProvider
        .GetRequiredService<IAuthService>();

    var result = await auth.LoginAsync(new LoginRequest(
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

// Email token smoke test.
if (builder.Configuration.GetValue<bool>("token-smoke"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Token testing is development-only.");
    }

    await using var scope = app.Services.CreateAsyncScope();

    var tokens = scope.ServiceProvider
        .GetRequiredService<IAccountTokenService>();

    await tokens.RequestPasswordResetAsync(
        "mentor1@mentorbooking.test");

    Console.WriteLine(
        "PASS: Password reset token request processed.");

    return;
}

// Swagger.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

// Authentication must run before authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
