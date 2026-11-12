
using System.Text.Json;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IMentorProfileService
{
    Task<MentorProfileResponse> GetMineAsync(
        Guid mentorId,
        CancellationToken ct = default);

    Task<MentorProfileResponse> UpdateMineAsync(
        Guid mentorId,
        SaveMentorProfileRequest request,
        CancellationToken ct = default);

    Task<bool> IsCompleteAsync(
        Guid mentorId,
        CancellationToken ct = default);
}

public sealed class MentorProfileService
    : IMentorProfileService
{
    private readonly AppDbContext _db;

    private static readonly string[] AllowedSubjects =
    {
        "Quant",
        "VARC",
        "DILR"
    };

    public MentorProfileService(AppDbContext db)
    {
        _db = db;
    }

    // GET: Read the current mentor's saved profile.
    public async Task<MentorProfileResponse> GetMineAsync(
        Guid mentorId,
        CancellationToken ct = default)
    {
        var mentor = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == mentorId &&
                     x.Role == UserRole.Mentor &&
                     !x.IsDeleted,
                ct);

        if (mentor == null)
        {
            throw new KeyNotFoundException(
                "Mentor profile not found.");
        }

        return ToResponse(mentor);
    }

    // PUT: Validate and save the profile in SQLite.
    public async Task<MentorProfileResponse> UpdateMineAsync(
        Guid mentorId,
        SaveMentorProfileRequest request,
        CancellationToken ct = default)
    {
        if (request == null)
        {
            throw new ArgumentException(
                "Profile details are required.");
        }

        var bio = request.Bio?.Trim() ?? "";

        if (bio.Length < 20 || bio.Length > 2000)
        {
            throw new ArgumentException(
                "Bio must contain 20 to 2000 characters.");
        }

        var subjects = ValidateSubjects(request.Subjects);

        var mentor = await _db.Users
            .FirstOrDefaultAsync(
                x => x.Id == mentorId &&
                     x.Role == UserRole.Mentor &&
                     !x.IsDeleted,
                ct);

        if (mentor == null)
        {
            throw new KeyNotFoundException(
                "Mentor profile not found.");
        }

        // Store the normalized subjects as JSON.
        mentor.Bio = bio;
        mentor.SubjectsJson =
            JsonSerializer.Serialize(subjects);

        await _db.SaveChangesAsync(ct);

        // Return values calculated from saved fields.
        return ToResponse(mentor);
    }

    // Check actual saved profile data.
    // Other services can use this for profile gating.
    public async Task<bool> IsCompleteAsync(
        Guid mentorId,
        CancellationToken ct = default)
    {
        var profile = await _db.Users
            .AsNoTracking()
            .Where(x => x.Id == mentorId &&
                        x.Role == UserRole.Mentor &&
                        !x.IsDeleted)
            .Select(x => new
            {
                x.Bio,
                x.SubjectsJson
            })
            .FirstOrDefaultAsync(ct);

        if (profile == null)
        {
            return false;
        }

        return CheckComplete(
            profile.Bio,
            ParseStoredSubjects(profile.SubjectsJson));
    }

    // Allow exactly one to three recognized subjects.
    private static string[] ValidateSubjects(
        string[]? subjects)
    {
        if (subjects == null ||
            subjects.Length < 1 ||
            subjects.Length > 3)
        {
            throw new ArgumentException(
                "Select at least one and at most " +
                "three subjects.");
        }

        var result = new List<string>();

        foreach (var item in subjects)
        {
            var requested = item?.Trim() ?? "";

            var canonical = AllowedSubjects.FirstOrDefault(
                allowed => string.Equals(
                    allowed,
                    requested,
                    StringComparison.OrdinalIgnoreCase));

            if (canonical == null)
            {
                throw new ArgumentException(
                    "Subjects must be Quant, VARC or DILR.");
            }

            if (result.Contains(
                canonical,
                StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Duplicate subjects are not allowed.");
            }

            result.Add(canonical);
        }

        return result.ToArray();
    }

    // Existing users may have empty or older profile data.
    // Invalid stored JSON must not crash profile reading.
    private static string[] ParseStoredSubjects(
        string? subjectsJson)
    {
        if (string.IsNullOrWhiteSpace(subjectsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            var subjects = JsonSerializer
                .Deserialize<string[]>(subjectsJson);

            if (subjects == null)
            {
                return Array.Empty<string>();
            }

            return ValidateSubjects(subjects);
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
        catch (ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool CheckComplete(
        string? bio,
        IReadOnlyList<string> subjects)
    {
        var bioLength = bio?.Trim().Length ?? 0;

        return bioLength >= 20 &&
               bioLength <= 2000 &&
               subjects.Count >= 1 &&
               subjects.Count <= 3;
    }

    private static MentorProfileResponse ToResponse(
        User mentor)
    {
        var subjects =
            ParseStoredSubjects(mentor.SubjectsJson);

        return new MentorProfileResponse(
            MentorId: mentor.Id,
            Name: mentor.Name,
            Bio: mentor.Bio,
            Subjects: subjects,
            IsComplete: CheckComplete(
                mentor.Bio,
                subjects)
        );
    }
}
