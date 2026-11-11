
using System.Text;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Controllers and Swagger.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database configuration.
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Database connection string is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Authentication service.
builder.Services.AddScoped<IAuthService, AuthService>();

// JWT settings.
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

// Existing development database seeder.
if (builder.Configuration.GetValue<bool>("seed"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Database seeding is allowed only in Development.");
    }

    await using var scope = app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    await DevelopmentSeeder.RunAsync(db);

    return;
}

// Development-only login smoke test.
// No JWT or password is printed.
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
