
using System.Text.Json;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Data;

public static class DevelopmentSeeder
{
    public static async Task RunAsync(AppDbContext db)
    {
        // This method is only called in Development mode.
        var password = Environment.GetEnvironmentVariable(
            "MENTORBOOKING_SEED_PASSWORD");

        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < 8 ||
            !password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit))
        {
            throw new InvalidOperationException(
                "Set MENTORBOOKING_SEED_PASSWORD to a " +
                "development password of at least 8 characters, " +
                "including a letter and a number.");
        }

        // Apply any existing migrations before inserting data.
        await db.Database.MigrateAsync();

        var demoEmails = new[]
        {
            "mentor1@mentorbooking.test",
            "mentor2@mentorbooking.test",
            "student1@mentorbooking.test",
            "student2@mentorbooking.test",
            "student3@mentorbooking.test"
        };

        // Prevent duplicate test accounts.
        var existingCount = await db.Users.CountAsync(
            user => demoEmails.Contains(user.Email));

        if (existingCount == demoEmails.Length)
        {
            Console.WriteLine(
                "All demo accounts already exist. Seeding skipped.");
            return;
        }

        if (existingCount > 0)
        {
            throw new InvalidOperationException(
                "Some demo accounts already exist. " +
                "Seeding stopped to protect existing data.");
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        // Create users with individually salted BCrypt hashes.
        User CreateUser(
            string name,
            string email,
            UserRole role,
            string? bio = null,
            string[]? subjects = null)
        {
            return new User
            {
                Name = name,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                    password, workFactor: 12),
                Role = role,
                Bio = bio,
                SubjectsJson = subjects == null
                    ? null
                    : JsonSerializer.Serialize(subjects),
                IsEmailVerified = true,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        var mentor1 = CreateUser(
            "Aarav Sharma",
            demoEmails[0],
            UserRole.Mentor,
            "Quantitative Aptitude mentor.",
            new[] { "Quant", "DILR" });

        var mentor2 = CreateUser(
            "Meera Kapoor",
            demoEmails[1],
            UserRole.Mentor,
            "Verbal Ability and reasoning mentor.",
            new[] { "VARC", "DILR" });

        var student1 = CreateUser(
            "Ananya Singh",
            demoEmails[2],
            UserRole.Student);

        var student2 = CreateUser(
            "Rohan Verma",
            demoEmails[3],
            UserRole.Student);

        var student3 = CreateUser(
            "Kabir Gupta",
            demoEmails[4],
            UserRole.Student);

        db.Users.AddRange(
            mentor1, mentor2,
            student1, student2, student3);

        // Use IST for constructing sample calendar dates.
        // All values saved to SQLite are UTC.
        TimeZoneInfo timeZone;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                "Asia/Kolkata");
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(
                "India Standard Time");
        }

        var todayIst = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, timeZone));

        DateTime ToUtc(int dayOffset, int hour)
        {
            var localDate = todayIst.AddDays(dayOffset);
            var localTime = localDate.ToDateTime(
                new TimeOnly(hour, 0));

            return TimeZoneInfo.ConvertTimeToUtc(
                localTime, timeZone);
        }

        // Each example gets its own two-hour Available slot.
        // These slots do not overlap for the same mentor.
        var examples = new[]
        {
            (
                Mentor: mentor1,
                Student: student1,
                Day: -7,
                Hour: 11,
                Status: SessionStatus.Completed,
                Mode: SessionMode.Zoom,
                Topic: "Percentages and Ratios"
            ),
            (
                Mentor: mentor2,
                Student: student2,
                Day: -6,
                Hour: 14,
                Status: SessionStatus.NoShow,
                Mode: SessionMode.Call,
                Topic: "Reading Comprehension"
            ),
            (
                Mentor: mentor1,
                Student: student3,
                Day: -5,
                Hour: 10,
                Status: SessionStatus.Rejected,
                Mode: SessionMode.Text,
                Topic: "Profit and Loss"
            ),
            (
                Mentor: mentor2,
                Student: student1,
                Day: -4,
                Hour: 12,
                Status: SessionStatus.Cancelled,
                Mode: SessionMode.Zoom,
                Topic: "Critical Reasoning"
            ),
            (
                Mentor: mentor1,
                Student: student2,
                Day: -3,
                Hour: 16,
                Status: SessionStatus.Expired,
                Mode: SessionMode.Text,
                Topic: "Algebra Basics"
            ),
            (
                Mentor: mentor1,
                Student: student1,
                Day: 1,
                Hour: 10,
                Status: SessionStatus.Pending,
                Mode: SessionMode.Zoom,
                Topic: "Time and Work"
            ),
            (
                Mentor: mentor2,
                Student: student3,
                Day: 1,
                Hour: 13,
                Status: SessionStatus.Approved,
                Mode: SessionMode.Zoom,
                Topic: "Data Interpretation"
            ),
            (
                Mentor: mentor2,
                Student: student1,
                Day: 0,
                Hour: 15,
                Status: SessionStatus.InProgress,
                Mode: SessionMode.Call,
                Topic: "Number Systems"
            ),
            (
                Mentor: mentor2,
                Student: student2,
                Day: 2,
                Hour: 11,
                Status: SessionStatus.NeedsReschedule,
                Mode: SessionMode.Text,
                Topic: "Logical Reasoning"
            )
        };

        foreach (var example in examples)
        {
            var start = ToUtc(example.Day, example.Hour);
            var end = start.AddHours(1);

            var slot = new AvailabilitySlot
            {
                MentorId = example.Mentor.Id,
                Type = AvailabilityType.Available,
                StartUtc = start.AddMinutes(-30),
                EndUtc = end.AddMinutes(30),
                Note = "Development sample availability"
            };

            db.AvailabilitySlots.Add(slot);

            var session = new Session
            {
                MentorId = example.Mentor.Id,
                StudentId = example.Student.Id,
                SlotId = slot.Id,
                Topic = example.Topic,
                Description =
                    "Development sample session for testing " +
                    "the mentor-student booking workflow.",
                Mode = example.Mode,
                ScheduledStartUtc = start,
                ScheduledEndUtc = end,
                OriginalStartUtc = start,
                Status = example.Status,
                CreatedAtUtc = start.AddDays(-10),
                RejectReason =
                    example.Status == SessionStatus.Rejected
                        ? "Mentor unavailable"
                        : null,
                MeetingInfo =
                    example.Status is
                        SessionStatus.Approved or
                        SessionStatus.InProgress or
                        SessionStatus.Completed or
                        SessionStatus.NoShow
                        ? "Development-only meeting details"
                        : null,
                ActualStartUtc =
                    example.Status is
                        SessionStatus.Completed or
                        SessionStatus.InProgress
                        ? start
                        : null,
                ActualEndUtc =
                    example.Status == SessionStatus.Completed
                        ? end.AddMinutes(5)
                        : null
            };

            db.Sessions.Add(session);

            // Build a sample lifecycle history.
            SessionStatus[] statusPath = example.Status switch
            {
                SessionStatus.Pending =>
                    new[] { SessionStatus.Pending },

                SessionStatus.Approved =>
                    new[]
                    {
                        SessionStatus.Pending,
                        SessionStatus.Approved
                    },

                SessionStatus.InProgress =>
                    new[]
                    {
                        SessionStatus.Pending,
                        SessionStatus.Approved,
                        SessionStatus.InProgress
                    },

                SessionStatus.Completed =>
                    new[]
                    {
                        SessionStatus.Pending,
                        SessionStatus.Approved,
                        SessionStatus.InProgress,
                        SessionStatus.Completed
                    },

                SessionStatus.NoShow =>
                    new[]
                    {
                        SessionStatus.Pending,
                        SessionStatus.Approved,
                        SessionStatus.NoShow
                    },

                SessionStatus.NeedsReschedule =>
                    new[]
                    {
                        SessionStatus.Pending,
                        SessionStatus.Approved,
                        SessionStatus.NeedsReschedule
                    },

                _ => new[]
                {
                    SessionStatus.Pending,
                    example.Status
                }
            };

            SessionStatus? previousStatus = null;

            for (var i = 0; i < statusPath.Length; i++)
            {
                db.SessionStatusHistory.Add(
                    new SessionStatusHistory
                    {
                        SessionId = session.Id,
                        FromStatus = previousStatus,
                        ToStatus = statusPath[i],
                        ChangedByUserId = null,
                        Reason = "Development seed",
                        ChangedAtUtc = session.CreatedAtUtc
                            .AddMinutes(i)
                    });

                previousStatus = statusPath[i];
            }

            // Completed sessions have completion records.
            if (example.Status == SessionStatus.Completed)
            {
                db.SessionRecords.Add(new SessionRecord
                {
                    SessionId = session.Id,
                    DoubtsCleared = 5,
                    Summary =
                        "Practised percentage conversions " +
                        "and ratio problems.",
                    Resolution = ResolutionStatus.FullyResolved,
                    CompletedAtUtc = end.AddMinutes(5)
                });

                db.Reviews.Add(new Review
                {
                    SessionId = session.Id,
                    Rating = 5,
                    Comment =
                        "Clear explanations and useful examples.",
                    CreatedAtUtc = end.AddHours(2)
                });
            }
        }

        // Two additional Busy slots.
        db.AvailabilitySlots.AddRange(
            new AvailabilitySlot
            {
                MentorId = mentor1.Id,
                Type = AvailabilityType.Busy,
                StartUtc = ToUtc(5, 15),
                EndUtc = ToUtc(5, 17),
                Note = "Personal work"
            },
            new AvailabilitySlot
            {
                MentorId = mentor2.Id,
                Type = AvailabilityType.Busy,
                StartUtc = ToUtc(5, 11),
                EndUtc = ToUtc(5, 13),
                Note = "Unavailable"
            });

        // Save all data together.
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Console.WriteLine("Development seeding successful.");
        Console.WriteLine("Users: 5 (2 mentors, 3 students)");
        Console.WriteLine("Availability slots: 11");
        Console.WriteLine("Sessions: 9 (all nine statuses)");
        Console.WriteLine("Completion records: 1");
        Console.WriteLine("Reviews: 1");
    }
}
