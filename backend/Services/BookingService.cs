
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IBookingService
{
    Task<BookingResponse> CreateAsync(
        Guid studentId,
        CreateBookingRequest request,
        CancellationToken ct = default);
}

public sealed class BookingService : IBookingService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public BookingService(
        AppDbContext db,
        IConfiguration configuration)
    {
        _db = db;

        var timeZoneId = configuration["TimeZone:Id"]
            ?? "Asia/Kolkata";

        try
        {
            _timeZone =
                TimeZoneInfo.FindSystemTimeZoneById(
                    timeZoneId);
        }
        catch (TimeZoneNotFoundException)
            when (timeZoneId == "Asia/Kolkata")
        {
            _timeZone =
                TimeZoneInfo.FindSystemTimeZoneById(
                    "India Standard Time");
        }
    }

    public async Task<BookingResponse> CreateAsync(
        Guid studentId,
        CreateBookingRequest request,
        CancellationToken ct = default)
    {
        var mode = ValidateRequest(request);

        var startUtc = request.ScheduledStartUtc;
        var endUtc = startUtc.AddMinutes(
            request.DurationMinutes);

        var topic = request.Topic.Trim();
        var description = request.Description.Trim();

        try
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, ct);

            // Verify the student.
            var student = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == studentId &&
                         !x.IsDeleted,
                    ct);

            if (student == null ||
                student.Role != UserRole.Student)
            {
                throw new BookingForbiddenException(
                    "Only students can book sessions.");
            }

            if (!student.IsEmailVerified)
            {
                throw new BookingForbiddenException(
                    "Verify your email before booking.");
            }

            // Verify the mentor.
            var mentor = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.MentorId &&
                         !x.IsDeleted &&
                         x.Role == UserRole.Mentor,
                    ct);

            if (mentor == null)
            {
                throw new KeyNotFoundException(
                    "Mentor not found.");
            }

            // Verify the selected availability slot.
            var slot = await _db.AvailabilitySlots
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.SlotId &&
                         x.MentorId == mentor.Id &&
                         x.Type == AvailabilityType.Available,
                    ct);

            if (slot == null)
            {
                throw new KeyNotFoundException(
                    "Available slot not found for this mentor.");
            }

            if (startUtc < slot.StartUtc ||
                endUtc > slot.EndUtc)
            {
                throw new BookingConflictException(
                    "The session must fit completely " +
                    "inside the selected Available slot.");
            }

            // Check for overlapping Busy slots.
            var busyConflict =
                await _db.AvailabilitySlots.AnyAsync(
                    x => x.MentorId == mentor.Id &&
                         x.Type == AvailabilityType.Busy &&
                         x.StartUtc < endUtc &&
                         x.EndUtc > startUtc,
                    ct);

            if (busyConflict)
            {
                throw new BookingConflictException(
                    "The mentor is busy at this time.");
            }

            // Capture one UTC time for the overlap checks.
            var now = DateTime.UtcNow;

            var approvalCutoff = now.AddHours(
                -PendingBookingDeadline.ApprovalHours);

            // A Pending booking is active only when:
            // - It is less than 48 hours old.
            // - Its scheduled start has not arrived.
            //
            // Approved and InProgress bookings always
            // hold their scheduled time.
            //
            // The explicit comparisons are translated
            // into SQL by EF Core.

            // Check mentor overlap.
            var mentorConflict = await _db.Sessions
                .AnyAsync(
                    x => x.MentorId == mentor.Id &&
                         (
                             x.Status ==
                                 SessionStatus.Approved ||

                             x.Status ==
                                 SessionStatus.InProgress ||

                             (
                                 x.Status ==
                                     SessionStatus.Pending &&
                                 x.CreatedAtUtc >
                                     approvalCutoff &&
                                 x.ScheduledStartUtc > now
                             )
                         ) &&
                         x.ScheduledStartUtc < endUtc &&
                         x.ScheduledEndUtc > startUtc,
                    ct);

            if (mentorConflict)
            {
                throw new BookingConflictException(
                    "That time was just taken, " +
                    "please pick another.");
            }

            // Apply the same deadline rules to the
            // student's other bookings.
            var studentConflict = await _db.Sessions
                .AnyAsync(
                    x => x.StudentId == student.Id &&
                         (
                             x.Status ==
                                 SessionStatus.Approved ||

                             x.Status ==
                                 SessionStatus.InProgress ||

                             (
                                 x.Status ==
                                     SessionStatus.Pending &&
                                 x.CreatedAtUtc >
                                     approvalCutoff &&
                                 x.ScheduledStartUtc > now
                             )
                         ) &&
                         x.ScheduledStartUtc < endUtc &&
                         x.ScheduledEndUtc > startUtc,
                    ct);

            if (studentConflict)
            {
                throw new BookingConflictException(
                    "You already have another session " +
                    "scheduled during this time.");
            }

            // Create the Pending booking.
            var createdAtUtc = DateTime.UtcNow;

            var session = new Session
            {
                Id = Guid.NewGuid(),
                MentorId = mentor.Id,
                StudentId = student.Id,
                SlotId = slot.Id,

                Topic = topic,
                Description = description,
                Mode = mode,

                ScheduledStartUtc = startUtc,
                ScheduledEndUtc = endUtc,
                OriginalStartUtc = startUtc,

                Status = SessionStatus.Pending,
                MeetingInfo = null,

                RowVersion = Guid.NewGuid(),
                CreatedAtUtc = createdAtUtc
            };

            _db.Sessions.Add(session);

            // Record initial status.
            _db.SessionStatusHistory.Add(
                new SessionStatusHistory
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    FromStatus = null,
                    ToStatus = SessionStatus.Pending,
                    ChangedByUserId = student.Id,
                    Reason = "Student requested a session.",
                    ChangedAtUtc = createdAtUtc
                });

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return ToResponse(session);
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new BookingConflictException(
                "Calendar is being updated. " +
                "Please refresh and retry.",
                ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new BookingConflictException(
                "The booking changed while processing. " +
                "Please try again.",
                ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode is 5 or 6 or 19)
        {
            throw new BookingConflictException(
                "The selected time is no longer available. " +
                "Please refresh and try again.",
                ex);
        }
    }

    private SessionMode ValidateRequest(
        CreateBookingRequest request)
    {
        if (request.MentorId == Guid.Empty ||
            request.SlotId == Guid.Empty)
        {
            throw new ArgumentException(
                "Mentor and availability slot are required.");
        }

        var topic = request.Topic?.Trim() ?? "";

        if (topic.Length < 2 || topic.Length > 200)
        {
            throw new ArgumentException(
                "Topic must contain 2 to 200 characters.");
        }

        var description =
            request.Description?.Trim() ?? "";

        if (description.Length < 20 ||
            description.Length > 2000)
        {
            throw new ArgumentException(
                "Description must contain " +
                "20 to 2000 characters.");
        }

        var modeText = request.Mode?.Trim() ?? "";

        if (!Enum.TryParse<SessionMode>(
                modeText, true, out var mode) ||
            !Enum.IsDefined(mode) ||
            !string.Equals(
                modeText,
                mode.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Mode must be Zoom, Text or Call.");
        }

        if (request.DurationMinutes != 30 &&
            request.DurationMinutes != 60)
        {
            throw new ArgumentException(
                "Session duration must be 30 or 60 minutes.");
        }

        if (request.ScheduledStartUtc.Kind !=
            DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Start time must be UTC with a Z suffix.");
        }

        if (request.ScheduledStartUtc <= DateTime.UtcNow)
        {
            throw new ArgumentException(
                "Cannot book a session in the past.");
        }

        var endUtc = request.ScheduledStartUtc
            .AddMinutes(request.DurationMinutes);

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(
            request.ScheduledStartUtc,
            _timeZone);

        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(
            endUtc,
            _timeZone);

        if (localStart.Date != localEnd.Date)
        {
            throw new ArgumentException(
                "Sessions cannot cross midnight in IST.");
        }

        if (localStart.Minute % 30 != 0 ||
            localStart.Ticks %
                TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException(
                "Session start must follow the " +
                "30-minute calendar grid.");
        }

        return mode;
    }

    private static BookingResponse ToResponse(
        Session session)
    {
        return new BookingResponse(
            session.Id,
            session.MentorId,
            session.StudentId,
            session.SlotId,
            session.Topic,
            session.Description,
            session.Mode.ToString(),

            DateTime.SpecifyKind(
                session.ScheduledStartUtc,
                DateTimeKind.Utc),

            DateTime.SpecifyKind(
                session.ScheduledEndUtc,
                DateTimeKind.Utc),

            session.Status.ToString(),

            DateTime.SpecifyKind(
                session.CreatedAtUtc,
                DateTimeKind.Utc)
        );
    }
}
