
using System.Data;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IAvailabilityService
{
    Task<IReadOnlyList<AvailabilitySlotResponse>> GetWeekAsync(
        Guid mentorId,
        DateTime weekStartUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<AvailabilityClashResponse>> CheckClashesAsync(
        Guid mentorId,
        SaveAvailabilitySlotRequest request,
        Guid? excludeSlotId = null,
        CancellationToken ct = default);

    Task<AvailabilitySlotResponse> CreateAsync(
        Guid mentorId,
        SaveAvailabilitySlotRequest request,
        bool replaceExisting = false,
        CancellationToken ct = default);

    Task<AvailabilitySlotResponse> UpdateAsync(
        Guid mentorId,
        Guid slotId,
        SaveAvailabilitySlotRequest request,
        bool replaceExisting = false,
        CancellationToken ct = default);

    Task DeleteAsync(
        Guid mentorId,
        Guid slotId,
        CancellationToken ct = default);
}

public sealed class AvailabilityService : IAvailabilityService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public AvailabilityService(
        AppDbContext db,
        IConfiguration configuration)
    {
        _db = db;

        var id = configuration["TimeZone:Id"]
            ?? "Asia/Kolkata";

        try
        {
            _timeZone =
                TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
            when (id == "Asia/Kolkata")
        {
            _timeZone =
                TimeZoneInfo.FindSystemTimeZoneById(
                    "India Standard Time");
        }
    }

    // Return the mentor's slots for a Monday-Sunday week.
    public async Task<IReadOnlyList<AvailabilitySlotResponse>>
        GetWeekAsync(
            Guid mentorId,
            DateTime weekStartUtc,
            CancellationToken ct = default)
    {
        await EnsureMentorAsync(mentorId, ct);

        RequireUtc(weekStartUtc);

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(
            weekStartUtc, _timeZone);

        if (localStart.DayOfWeek != DayOfWeek.Monday ||
            localStart.TimeOfDay != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Week must start Monday at 12:00 AM IST.");
        }

        var weekEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            localStart.AddDays(7), _timeZone);

        var slots = await _db.AvailabilitySlots
            .AsNoTracking()
            .Where(x =>
                x.MentorId == mentorId &&
                x.StartUtc < weekEndUtc &&
                x.EndUtc > weekStartUtc)
            .OrderBy(x => x.StartUtc)
            .ToListAsync(ct);

        return slots.Select(ToResponse).ToList();
    }

    // Check overlaps without modifying the database.
    // This is useful when the calendar popup is open.
    public async Task<IReadOnlyList<AvailabilityClashResponse>>
        CheckClashesAsync(
            Guid mentorId,
            SaveAvailabilitySlotRequest request,
            Guid? excludeSlotId = null,
            CancellationToken ct = default)
    {
        ValidateInput(request);
        await EnsureMentorAsync(mentorId, ct);

        var clashes = await FindClashesAsync(
            mentorId,
            request.StartUtc,
            request.EndUtc,
            excludeSlotId,
            ct);

        return clashes.Select(ToClashResponse).ToList();
    }

    public Task<AvailabilitySlotResponse> CreateAsync(
        Guid mentorId,
        SaveAvailabilitySlotRequest request,
        bool replaceExisting = false,
        CancellationToken ct = default)
    {
        return SaveAsync(
            mentorId, null, request, replaceExisting, ct);
    }

    public Task<AvailabilitySlotResponse> UpdateAsync(
        Guid mentorId,
        Guid slotId,
        SaveAvailabilitySlotRequest request,
        bool replaceExisting = false,
        CancellationToken ct = default)
    {
        return SaveAsync(
            mentorId, slotId, request, replaceExisting, ct);
    }

    private async Task<AvailabilitySlotResponse> SaveAsync(
        Guid mentorId,
        Guid? slotId,
        SaveAvailabilitySlotRequest request,
        bool replaceExisting,
        CancellationToken ct)
    {
        var type = ValidateInput(request);

        try
        {
            // The clash check and write happen atomically.
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, ct);

            await EnsureMentorAsync(mentorId, ct);

            AvailabilitySlot? slot = null;

            if (slotId.HasValue)
            {
                slot = await _db.AvailabilitySlots
                    .FirstOrDefaultAsync(
                        x => x.Id == slotId.Value &&
                             x.MentorId == mentorId,
                        ct);

                if (slot == null)
                {
                    throw new KeyNotFoundException(
                        "Availability slot not found.");
                }

                await EnsureSlotUnusedAsync(slot.Id, ct);
            }

            var clashes = await FindClashesAsync(
                mentorId,
                request.StartUtc,
                request.EndUtc,
                slotId,
                ct);

            if (clashes.Count > 0)
            {
                var clashResponses = clashes
                    .Select(ToClashResponse)
                    .ToList();

                if (!replaceExisting)
                {
                    var first = clashes[0];

                    throw new AvailabilityConflictException(
                        FormatClash(first),
                        clashResponses);
                }

                // Explicit replacement is only permitted
                // for slots with no associated sessions.
                foreach (var clash in clashes)
                {
                    await EnsureSlotUnusedAsync(clash.Id, ct);
                }

                _db.AvailabilitySlots.RemoveRange(clashes);
            }

            if (slot == null)
            {
                slot = new AvailabilitySlot
                {
                    MentorId = mentorId
                };

                _db.AvailabilitySlots.Add(slot);
            }

            slot.Type = type;
            slot.StartUtc = request.StartUtc;
            slot.EndUtc = request.EndUtc;
            slot.Note = string.IsNullOrWhiteSpace(request.Note)
                ? null
                : request.Note.Trim();

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return ToResponse(slot);
        }
        catch (SqliteException ex)
            when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new AvailabilityConflictException(
                "Calendar is being updated. Please retry.");
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode is 5 or 6)
        {
            throw new AvailabilityConflictException(
                "Calendar is being updated. Please retry.");
        }
    }

    // Delete only an unused slot owned by this mentor.
    public async Task DeleteAsync(
        Guid mentorId,
        Guid slotId,
        CancellationToken ct = default)
    {
        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, ct);

        await EnsureMentorAsync(mentorId, ct);

        var slot = await _db.AvailabilitySlots
            .FirstOrDefaultAsync(
                x => x.Id == slotId &&
                     x.MentorId == mentorId,
                ct);

        if (slot == null)
        {
            throw new KeyNotFoundException(
                "Availability slot not found.");
        }

        await EnsureSlotUnusedAsync(slot.Id, ct);

        _db.AvailabilitySlots.Remove(slot);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // A referenced slot is protected even if the
    // associated booking has already been completed.
    private async Task EnsureSlotUnusedAsync(
        Guid slotId,
        CancellationToken ct)
    {
        var used = await _db.Sessions.AnyAsync(
            x => x.SlotId == slotId, ct);

        if (used)
        {
            throw new AvailabilityConflictException(
                "This slot is linked to a session and " +
                "cannot be edited, deleted or replaced.");
        }
    }

    private async Task EnsureMentorAsync(
        Guid mentorId,
        CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == mentorId && !x.IsDeleted,
                ct);

        if (user == null)
        {
            throw new KeyNotFoundException(
                "Mentor not found.");
        }

        if (user.Role != UserRole.Mentor)
        {
            throw new UnauthorizedAccessException(
                "Only mentors can manage availability.");
        }
    }

    // A.start < B.end AND A.end > B.start
    private Task<List<AvailabilitySlot>> FindClashesAsync(
        Guid mentorId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeSlotId,
        CancellationToken ct)
    {
        return _db.AvailabilitySlots
            .AsNoTracking()
            .Where(x =>
                x.MentorId == mentorId &&
                x.StartUtc < endUtc &&
                x.EndUtc > startUtc &&
                (!excludeSlotId.HasValue ||
                 x.Id != excludeSlotId.Value))
            .OrderBy(x => x.StartUtc)
            .ToListAsync(ct);
    }

    private AvailabilityType ValidateInput(
        SaveAvailabilitySlotRequest request)
    {
        if (!Enum.TryParse<AvailabilityType>(
                request.Type, true, out var type) ||
            !Enum.IsDefined(type))
        {
            throw new ArgumentException(
                "Type must be Available or Busy.");
        }

        RequireUtc(request.StartUtc);
        RequireUtc(request.EndUtc);

        if (request.StartUtc <= DateTime.UtcNow)
        {
            throw new ArgumentException(
                "Cannot create availability in the past.");
        }

        var duration =
            request.EndUtc - request.StartUtc;

        if (duration < TimeSpan.FromMinutes(30) ||
            duration > TimeSpan.FromHours(8))
        {
            throw new ArgumentException(
                "Slot duration must be 30 minutes to 8 hours.");
        }

        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(
            request.StartUtc, _timeZone);

        var endLocal = TimeZoneInfo.ConvertTimeFromUtc(
            request.EndUtc, _timeZone);

        if (startLocal.Date != endLocal.Date)
        {
            throw new ArgumentException(
                "Slots cannot cross midnight. Split the slot.");
        }

        if (!IsOnGrid(startLocal) || !IsOnGrid(endLocal))
        {
            throw new ArgumentException(
                "Start and end must use a 15-minute grid.");
        }

        if (request.Note?.Trim().Length > 500)
        {
            throw new ArgumentException(
                "Note cannot exceed 500 characters.");
        }

        return type;
    }

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Dates must be UTC, using a Z suffix.");
        }
    }

    private static bool IsOnGrid(DateTime value)
    {
        return value.Minute % 15 == 0 &&
               value.Ticks % TimeSpan.TicksPerMinute == 0;
    }

    private string FormatClash(AvailabilitySlot slot)
    {
        var start = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(
                slot.StartUtc, DateTimeKind.Utc), _timeZone);

        var end = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(
                slot.EndUtc, DateTimeKind.Utc), _timeZone);

        return $"Clashes with {slot.Type} " +
               $"{start:h:mm tt}–{end:h:mm tt} IST.";
    }

    private static AvailabilitySlotResponse ToResponse(
        AvailabilitySlot slot)
    {
        return new AvailabilitySlotResponse(
            slot.Id,
            slot.MentorId,
            slot.Type.ToString(),
            DateTime.SpecifyKind(
                slot.StartUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(
                slot.EndUtc, DateTimeKind.Utc),
            slot.Note);
    }

    private static AvailabilityClashResponse ToClashResponse(
        AvailabilitySlot slot)
    {
        return new AvailabilityClashResponse(
            slot.Id,
            slot.Type.ToString(),
            DateTime.SpecifyKind(
                slot.StartUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(
                slot.EndUtc, DateTimeKind.Utc));
    }
}
