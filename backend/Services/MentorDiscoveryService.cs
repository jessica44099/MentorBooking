
using System.Text.Json;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IMentorDiscoveryService
{
    Task<MentorSearchResponse> SearchAsync(
        Guid studentId,
        string? query,
        string? subject,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PublicMentorResponse> GetByIdAsync(
        Guid studentId,
        Guid mentorId,
        CancellationToken ct = default);
}

public sealed class MentorDiscoveryService
    : IMentorDiscoveryService
{
    private readonly AppDbContext _db;

    private static readonly string[] AllowedSubjects =
    {
        "Quant",
        "VARC",
        "DILR"
    };

    public MentorDiscoveryService(AppDbContext db)
    {
        _db = db;
    }

    // Search mentors with optional filters and pagination.
    public async Task<MentorSearchResponse> SearchAsync(
        Guid studentId,
        string? query,
        string? subject,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        await EnsureActiveStudentAsync(studentId, ct);

        if (page < 1)
        {
            throw new ArgumentException(
                "Page must be at least 1.");
        }

        if (pageSize < 1 || pageSize > 50)
        {
            throw new ArgumentException(
                "Page size must be between 1 and 50.");
        }

        var searchTerm = query?.Trim() ?? "";

        if (searchTerm.Length > 100)
        {
            throw new ArgumentException(
                "Search text cannot exceed 100 characters.");
        }

        var subjectFilter = ValidateSubjectFilter(subject);

        // Only retrieve fields needed for public discovery.
        // No email, password hash or authentication fields
        // are included in this database projection.
        var candidates = await _db.Users
            .AsNoTracking()
            .Where(x =>
                x.Role == UserRole.Mentor &&
                !x.IsDeleted &&
                x.IsEmailVerified &&
                x.Bio != null &&
                x.SubjectsJson != null)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Bio,
                x.SubjectsJson
            })
            .ToListAsync(ct);

        // Profile subjects are currently stored as JSON.
        // Parse and validate them before applying filters.
        // For the initial small-scale application,
        // filtering and pagination happen in memory.
        var mentors = candidates
            .Select(x => ToPublicProfile(
                x.Id,
                x.Name,
                x.Bio,
                x.SubjectsJson))
            .OfType<PublicMentorResponse>()
            .Where(x =>
                searchTerm.Length == 0 ||
                x.Name.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase) ||
                x.Bio.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase))
            .Where(x =>
                subjectFilter == null ||
                x.Subjects.Contains(
                    subjectFilter,
                    StringComparer.OrdinalIgnoreCase))
            .OrderBy(
                x => x.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.MentorId)
            .ToList();

        var totalCount = mentors.Count;

        var totalPages = (int)Math.Ceiling(
            (double)totalCount / pageSize);

        // Use long arithmetic to avoid overflow when
        // a very large page number is requested.
        long skip = ((long)page - 1) * pageSize;

        IReadOnlyList<PublicMentorResponse> items =
            skip >= totalCount
                ? Array.Empty<PublicMentorResponse>()
                : mentors
                    .Skip((int)skip)
                    .Take(pageSize)
                    .ToList();

        return new MentorSearchResponse(
            Items: items,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages
        );
    }

    // Get one mentor's public profile.
    public async Task<PublicMentorResponse> GetByIdAsync(
        Guid studentId,
        Guid mentorId,
        CancellationToken ct = default)
    {
        await EnsureActiveStudentAsync(studentId, ct);

        var mentor = await _db.Users
            .AsNoTracking()
            .Where(x =>
                x.Id == mentorId &&
                x.Role == UserRole.Mentor &&
                !x.IsDeleted &&
                x.IsEmailVerified)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Bio,
                x.SubjectsJson
            })
            .FirstOrDefaultAsync(ct);

        if (mentor == null)
        {
            throw new KeyNotFoundException(
                "Mentor not found.");
        }

        var response = ToPublicProfile(
            mentor.Id,
            mentor.Name,
            mentor.Bio,
            mentor.SubjectsJson);

        // Incomplete profiles must not be discoverable.
        if (response == null)
        {
            throw new KeyNotFoundException(
                "Mentor not found.");
        }

        return response;
    }

    // Check the real database account instead of trusting
    // the role claim from an older JWT alone.
    private async Task EnsureActiveStudentAsync(
        Guid studentId,
        CancellationToken ct)
    {
        var valid = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == studentId &&
                     x.Role == UserRole.Student &&
                     !x.IsDeleted,
                ct);

        if (!valid)
        {
            throw new MentorDiscoveryForbiddenException(
                "An active Student account is required.");
        }
    }

    private static string? ValidateSubjectFilter(
        string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var requested = subject.Trim();

        var canonical = AllowedSubjects.FirstOrDefault(
            x => string.Equals(
                x,
                requested,
                StringComparison.OrdinalIgnoreCase));

        if (canonical == null)
        {
            throw new ArgumentException(
                "Subject must be Quant, VARC or DILR.");
        }

        return canonical;
    }

    // Convert saved mentor data into a safe public DTO.
    // Return null for incomplete or invalid profiles.
    private static PublicMentorResponse? ToPublicProfile(
        Guid mentorId,
        string name,
        string? bio,
        string? subjectsJson)
    {
        var cleanBio = bio?.Trim() ?? "";

        if (cleanBio.Length < 20 ||
            cleanBio.Length > 2000 ||
            string.IsNullOrWhiteSpace(subjectsJson))
        {
            return null;
        }

        string[]? storedSubjects;

        try
        {
            storedSubjects =
                JsonSerializer.Deserialize<string[]>(
                    subjectsJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (storedSubjects == null ||
            storedSubjects.Length < 1 ||
            storedSubjects.Length > 3)
        {
            return null;
        }

        var subjects = new List<string>();

        foreach (var raw in storedSubjects)
        {
            var requested = raw?.Trim() ?? "";

            var canonical = AllowedSubjects.FirstOrDefault(
                x => string.Equals(
                    x,
                    requested,
                    StringComparison.OrdinalIgnoreCase));

            // Reject unknown or duplicate subjects.
            if (canonical == null ||
                subjects.Contains(
                    canonical,
                    StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            subjects.Add(canonical);
        }

        return new PublicMentorResponse(
            MentorId: mentorId,
            Name: name,
            Bio: cleanBio,
            Subjects: subjects
        );
    }
}
