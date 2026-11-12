
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Services;

public interface IReviewService
{
    Task<SessionReviewResponse> CreateAsync(
        Guid studentId,
        Guid sessionId,
        CreateReviewRequest request,
        CancellationToken ct = default);

    Task<SessionReviewResponse> GetMineAsync(
        Guid studentId,
        Guid sessionId,
        CancellationToken ct = default);

    Task<MentorReviewsResponse> GetMentorReviewsAsync(
        Guid studentId,
        Guid mentorId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

public sealed class ReviewService : IReviewService
{
    private readonly AppDbContext _db;
    private readonly IMentorDiscoveryService _discovery;

    public ReviewService(
        AppDbContext db,
        IMentorDiscoveryService discovery)
    {
        _db = db;
        _discovery = discovery;
    }

    // A student submits one review for their own
    // completed session.
    public async Task<SessionReviewResponse> CreateAsync(
        Guid studentId,
        Guid sessionId,
        CreateReviewRequest request,
        CancellationToken ct = default)
    {
        if (request == null)
        {
            throw new ArgumentException(
                "Review details are required.");
        }

        if (request.Rating < 1 || request.Rating > 5)
        {
            throw new ArgumentException(
                "Rating must be between 1 and 5.");
        }

        var comment = request.Comment?.Trim();

        if (comment?.Length > 1000)
        {
            throw new ArgumentException(
                "Review comment cannot exceed 1000 characters.");
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            comment = null;
        }

        await EnsureActiveStudentAsync(
            studentId,
            requireVerified: true,
            ct);

        // Verify that the session belongs to this student.
        var session = await _db.Sessions
            .AsNoTracking()
            .Where(x =>
                x.Id == sessionId &&
                x.StudentId == studentId)
            .Select(x => new
            {
                x.Id,
                x.Status
            })
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            throw new KeyNotFoundException(
                "Session not found.");
        }

        // Reviews are allowed only after completion.
        if (session.Status != SessionStatus.Completed)
        {
            throw new ReviewNotAllowedException(
                "Reviews are allowed only after " +
                "a session is Completed.");
        }

        // Check whether the student already reviewed.
        var alreadyReviewed = await _db.Reviews
            .AsNoTracking()
            .AnyAsync(
                x => x.SessionId == sessionId,
                ct);

        if (alreadyReviewed)
        {
            throw new ReviewConflictException(
                "You have already reviewed this session.");
        }

        var review = new Review
        {
            SessionId = session.Id,
            Rating = request.Rating,
            Comment = comment,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Reviews.Add(review);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteErrorCode == 19)
        {
            // SessionId is the primary key in Reviews.
            // This also prevents duplicate submissions
            // from concurrent requests.
            throw new ReviewConflictException(
                "This session already has a review " +
                "or the review could not be saved.",
                ex);
        }

        return ToSessionResponse(review);
    }

    // Retrieve the review written by the student
    // for their own session.
    public async Task<SessionReviewResponse> GetMineAsync(
        Guid studentId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        await EnsureActiveStudentAsync(
            studentId,
            requireVerified: false,
            ct);

        var ownsSession = await _db.Sessions
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == sessionId &&
                     x.StudentId == studentId,
                ct);

        if (!ownsSession)
        {
            throw new KeyNotFoundException(
                "Session not found.");
        }

        var review = await _db.Reviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.SessionId == sessionId,
                ct);

        if (review == null)
        {
            throw new KeyNotFoundException(
                "No review exists for this session.");
        }

        return ToSessionResponse(review);
    }

    // Return public mentor reviews and calculate
    // the average rating from completed sessions.
    public async Task<MentorReviewsResponse> GetMentorReviewsAsync(
        Guid studentId,
        Guid mentorId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
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

        // Verify the student and public mentor profile.
        // This reuses the discovery validations.
        await _discovery.GetByIdAsync(
            studentId,
            mentorId,
            ct);

        // Only reviews for Completed sessions belonging
        // to this mentor contribute to the rating.
        var reviews =
            from review in _db.Reviews.AsNoTracking()
            join session in _db.Sessions.AsNoTracking()
                on review.SessionId equals session.Id
            where session.MentorId == mentorId &&
                  session.Status == SessionStatus.Completed
            select new
            {
                review.SessionId,
                review.Rating,
                review.Comment,
                review.CreatedAtUtc
            };

        var totalCount = await reviews.CountAsync(ct);

        double? averageRating = null;

        if (totalCount > 0)
        {
            var average = await reviews.AverageAsync(
                x => (double)x.Rating,
                ct);

            averageRating = Math.Round(
                average,
                2,
                MidpointRounding.AwayFromZero);
        }

        var totalPages =
            totalCount == 0
                ? 0
                : (int)Math.Ceiling(
                    (double)totalCount / pageSize);

        // Use long arithmetic to prevent integer overflow
        // with extremely large page numbers.
        long skip = ((long)page - 1) * pageSize;

        List<PublicReviewResponse> items;

        if (skip >= totalCount)
        {
            items = new List<PublicReviewResponse>();
        }
        else
        {
            items = await reviews
                .OrderByDescending(x => x.CreatedAtUtc)
                .ThenBy(x => x.SessionId)
                .Skip((int)skip)
                .Take(pageSize)
                .Select(x => new PublicReviewResponse(
                    x.Rating,
                    x.Comment,
                    x.CreatedAtUtc))
                .ToListAsync(ct);
        }

        // SQLite may return DateTime values with
        // DateTimeKind.Unspecified.
        // Explicitly identify stored timestamps as UTC.
        var utcItems = items
            .Select(x => x with
            {
                CreatedAtUtc = DateTime.SpecifyKind(
                    x.CreatedAtUtc,
                    DateTimeKind.Utc)
            })
            .ToList();

        return new MentorReviewsResponse(
            MentorId: mentorId,
            ReviewCount: totalCount,
            AverageRating: averageRating,
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages,
            Items: utcItems);
    }

    // Verify that the student account exists,
    // is active, and is verified when required.
    private async Task EnsureActiveStudentAsync(
        Guid studentId,
        bool requireVerified,
        CancellationToken ct)
    {
        var student = await _db.Users
            .AsNoTracking()
            .Where(x =>
                x.Id == studentId &&
                x.Role == UserRole.Student &&
                !x.IsDeleted)
            .Select(x => new
            {
                x.IsEmailVerified
            })
            .FirstOrDefaultAsync(ct);

        if (student == null)
        {
            throw new ReviewForbiddenException(
                "An active Student account is required.");
        }

        if (requireVerified && !student.IsEmailVerified)
        {
            throw new ReviewForbiddenException(
                "Verify your email before submitting reviews.");
        }
    }

    // Convert the database Review entity into
    // a safe API response.
    private static SessionReviewResponse ToSessionResponse(
        Review review)
    {
        return new SessionReviewResponse(
            SessionId: review.SessionId,
            Rating: review.Rating,
            Comment: review.Comment,
            CreatedAtUtc: DateTime.SpecifyKind(
                review.CreatedAtUtc,
                DateTimeKind.Utc));
    }
}
