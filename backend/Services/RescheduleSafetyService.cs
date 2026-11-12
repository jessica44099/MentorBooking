
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IRescheduleSafetyService
{
    Task<RescheduleHistoryResponse> GetHistoryAsync(
        Guid userId,
        UserRole role,
        Guid sessionId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<RescheduleCheckResponse> CheckAsync(
        Guid mentorId,
        Guid sessionId,
        CheckRescheduleRequest request,
        CancellationToken ct = default);
}

public sealed class RescheduleSafetyService
    : IRescheduleSafetyService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public RescheduleSafetyService(
        AppDbContext db,
        IConfiguration configuration)
    {
        _db = db;

        var timeZoneId =
            configuration["TimeZone:Id"] ?? "Asia/Kolkata";

        try
        {
            _timeZone = TimeZoneInfo
                .FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
            when (timeZoneId == "Asia/Kolkata")
        {
            _timeZone = TimeZoneInfo
                .FindSystemTimeZoneById(
                    "India Standard Time");
        }
    }

    // Students and mentors can read history
    // only for sessions they actually belong to.
    public async Task<RescheduleHistoryResponse>
        GetHistoryAsync(
            Guid userId,
            UserRole role,
            Guid sessionId,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default)
    {
        if (page < 1 ||
            pageSize < 1 ||
            pageSize > 50 ||
            ((long)page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentException(
                "Page must be positive and pageSize " +
                "must be between 1 and 50.");
        }

        var activeUser = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == userId &&
                     x.Role == role &&
                     !x.IsDeleted,
                ct);

        if (!activeUser)
        {
            throw new SessionLifecycleForbiddenException(
                "An active account is required.");
        }

        var session = await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == sessionId &&
                     (
                         role == UserRole.Mentor
                             ? x.MentorId == userId
                             : x.StudentId == userId
                     ),
                ct);

        if (session == null)
        {
            // Don't expose sessions belonging to others.
            throw new KeyNotFoundException(
                "Session not found.");
        }

        var query = _db.ShiftHistories
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId);

        var totalCount = await query.CountAsync(ct);

        var skip = (int)(((long)page - 1) * pageSize);

        var history = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        var entries = history.Select(x =>
            new RescheduleHistoryEntryResponse(
                Id: x.Id,
                OldStartUtc: AsUtc(x.OldStartUtc),
                NewStartUtc: AsUtc(x.NewStartUtc),
                MinutesShifted: x.MinutesShifted,
                Reason: x.Reason,
                CreatedAtUtc: AsUtc(x.CreatedAtUtc)
            )).ToList();

        return new RescheduleHistoryResponse(
            SessionId: session.Id,
            CurrentStatus: session.Status.ToString(),
            OriginalStartUtc: AsUtc(
                session.OriginalStartUtc),
            CurrentStartUtc: AsUtc(
                session.ScheduledStartUtc),
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            Entries: entries
        );
    }

    // Preview conflicts without changing any records.
    public async Task<RescheduleCheckResponse> CheckAsync(
        Guid mentorId,
        Guid sessionId,
        CheckRescheduleRequest request,
        CancellationToken ct = default)
    {
        if (request == null ||
            request.NewSlotId == Guid.Empty ||
            request.NewStartUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "A valid slot ID and UTC start are required.");
        }

        var mentor = await _db.Users
            .AsNoTracking()
            .Where(x =>
                x.Id == mentorId &&
                x.Role == UserRole.Mentor &&
                !x.IsDeleted)
            .Select(x => new
            {
                x.IsEmailVerified
            })
            .FirstOrDefaultAsync(ct);

        if (mentor == null)
        {
            throw new SessionLifecycleForbiddenException(
                "An active mentor account is required.");
        }

        if (!mentor.IsEmailVerified)
        {
            throw new SessionLifecycleForbiddenException(
                "Verify your email before rescheduling.");
        }

        var session = await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == sessionId &&
                     x.MentorId == mentorId,
                ct);

        if (session == null)
        {
            throw new KeyNotFoundException(
                "Session not found.");
        }

        if (session.Status != SessionStatus.Approved ||
            session.ActualStartUtc.HasValue)
        {
            throw new InvalidSessionTransitionException(
                "Only Approved, unstarted sessions " +
                "can be rescheduled.");
        }

        var now = DateTime.UtcNow;

        if (session.ScheduledStartUtc <= now)
        {
            throw new InvalidSessionTransitionException(
                "The original session start has passed.");
        }

        var newStart = request.NewStartUtc;

        if (newStart <= now.AddHours(2) ||
            newStart > now.AddDays(365))
        {
            throw new ArgumentException(
                "New start must be more than 2 hours " +
                "away and within the next 365 days.");
        }

        var duration =
            session.ScheduledEndUtc -
            session.ScheduledStartUtc;

        if (duration != TimeSpan.FromMinutes(30) &&
            duration != TimeSpan.FromMinutes(60))
        {
            throw new InvalidSessionTransitionException(
                "Session duration must be 30 or 60 minutes.");
        }

        var newEnd = newStart.Add(duration);

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(
            newStart, _timeZone);

        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(
            newEnd, _timeZone);

        if (localStart.Date != localEnd.Date ||
            localStart.Minute % 30 != 0 ||
            localStart.Ticks %
                TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException(
                "Session cannot cross local midnight, " +
                "and starts must follow the 30-minute grid.");
        }

        var conflicts = new List<string>();

        if (newStart == AsUtc(session.ScheduledStartUtc))
        {
            conflicts.Add(
                "Choose a different start time.");
        }

        var slot = await _db.AvailabilitySlots
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == request.NewSlotId &&
                     x.MentorId == mentorId &&
                     x.Type == AvailabilityType.Available,
                ct);

        if (slot == null ||
            newStart < slot.StartUtc ||
            newEnd > slot.EndUtc)
        {
            conflicts.Add(
                "The proposed time must fit within " +
                "an Available slot belonging to the mentor.");
        }

        var busy = await _db.AvailabilitySlots
            .AsNoTracking()
            .AnyAsync(
                x => x.MentorId == mentorId &&
                     x.Type == AvailabilityType.Busy &&
                     x.StartUtc < newEnd &&
                     x.EndUtc > newStart,
                ct);

        if (busy)
        {
            conflicts.Add(
                "The mentor has a Busy calendar block " +
                "at the proposed time.");
        }

        var approvalCutoff = now.AddHours(
            -PendingBookingDeadline.ApprovalHours);

        var mentorBooked = await _db.Sessions
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != sessionId &&
                     x.MentorId == mentorId &&
                     x.ScheduledStartUtc < newEnd &&
                     x.ScheduledEndUtc > newStart &&
                     (
                         x.Status == SessionStatus.Approved ||
                         x.Status == SessionStatus.InProgress ||
                         x.Status == SessionStatus.NeedsReschedule ||
                         (
                             x.Status == SessionStatus.Pending &&
                             x.CreatedAtUtc > approvalCutoff &&
                             x.ScheduledStartUtc > now
                         )
                     ),
                ct);

        if (mentorBooked)
        {
            conflicts.Add(
                "The mentor has another active booking " +
                "at the proposed time.");
        }

        var studentBooked = await _db.Sessions
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != sessionId &&
                     x.StudentId == session.StudentId &&
                     x.ScheduledStartUtc < newEnd &&
                     x.ScheduledEndUtc > newStart &&
                     (
                         x.Status == SessionStatus.Approved ||
                         x.Status == SessionStatus.InProgress ||
                         x.Status == SessionStatus.NeedsReschedule ||
                         (
                             x.Status == SessionStatus.Pending &&
                             x.CreatedAtUtc > approvalCutoff &&
                             x.ScheduledStartUtc > now
                         )
                     ),
                ct);

        if (studentBooked)
        {
            conflicts.Add(
                "The student has another active booking " +
                "at the proposed time.");
        }

        return new RescheduleCheckResponse(
            CanReschedule: conflicts.Count == 0,
            ProposedStartUtc: newStart,
            ProposedEndUtc: newEnd,
            Conflicts: conflicts
        );
    }

    private static DateTime AsUtc(DateTime dateTime)
    {
        return DateTime.SpecifyKind(
            dateTime, DateTimeKind.Utc);
    }
}
