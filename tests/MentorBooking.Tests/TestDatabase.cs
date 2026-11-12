
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MentorBooking.Tests;

public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }

    public IConfiguration Configuration { get; }

    public TestDatabase()
    {
        // An isolated SQLite database for each test.
        // No physical database file is created.
        _connection = new SqliteConnection(
            "Data Source=:memory:");

        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(options);

        Db.Database.EnsureCreated();

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TimeZone:Id"] = "Asia/Kolkata"
                })
            .Build();
    }

    // Create a test user.
    public User AddUser(
        UserRole role,
        bool verified = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Test " + role,
            Email = $"{Guid.NewGuid():N}@mentorbooking.test",
            PasswordHash = "test-only-unused-password-hash",
            Role = role,
            IsEmailVerified = verified,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        Db.Users.Add(user);
        Db.SaveChanges();

        return user;
    }

    // Create an available slot.
    public AvailabilitySlot AddSlot(
        User mentor,
        DateTime startUtc,
        int durationMinutes = 120)
    {
        var slot = new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            MentorId = mentor.Id,
            Type = AvailabilityType.Available,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(
                durationMinutes),
            Note = "Automated test availability",
            CreatedAtUtc = DateTime.UtcNow
        };

        Db.AvailabilitySlots.Add(slot);
        Db.SaveChanges();

        return slot;
    }

    // Create a session directly for lifecycle tests.
    public Session AddSession(
        User mentor,
        User student,
        AvailabilitySlot slot,
        DateTime startUtc,
        SessionStatus status,
        int durationMinutes = 60,
        DateTime? createdAtUtc = null)
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            MentorId = mentor.Id,
            StudentId = student.Id,
            SlotId = slot.Id,
            Topic = "Percentages",
            Description =
                "Testing percentage problems and ratios.",
            Mode = SessionMode.Zoom,
            ScheduledStartUtc = startUtc,
            ScheduledEndUtc = startUtc.AddMinutes(
                durationMinutes),
            OriginalStartUtc = startUtc,
            Status = status,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            RowVersion = Guid.NewGuid()
        };

        Db.Sessions.Add(session);
        Db.SaveChanges();

        return session;
    }

    // 10:00 AM IST, seven or more calendar days ahead.
    public static DateTime FutureStartUtc()
    {
        return DateTime.UtcNow.Date
            .AddDays(7)
            .AddHours(4)
            .AddMinutes(30);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
