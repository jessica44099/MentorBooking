
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MentorBooking.Tests;

public sealed class ReviewServiceTests
{
    private static ReviewService CreateService(
        TestDatabase test)
    {
        var discovery = new MentorDiscoveryService(test.Db);
        return new ReviewService(test.Db, discovery);
    }

    private static Session CreateSession(
        TestDatabase test,
        User mentor,
        User student,
        SessionStatus status)
    {
        var start = DateTime.UtcNow.Date
            .AddDays(-2)
            .AddHours(4)
            .AddMinutes(30);

        var slot = test.AddSlot(mentor, start);

        return test.AddSession(
            mentor,
            student,
            slot,
            start,
            status);
    }

    [Fact]
    public async Task Student_can_review_completed_session()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var session = CreateSession(
            test, mentor, student,
            SessionStatus.Completed);

        var service = CreateService(test);

        var review = await service.CreateAsync(
            student.Id,
            session.Id,
            new CreateReviewRequest(
                5, "Very clear explanations."));

        Assert.Equal(5, review.Rating);

        Assert.True(
            await test.Db.Reviews.AnyAsync(
                x => x.SessionId == session.Id &&
                     x.Rating == 5));
    }

    [Fact]
    public async Task Duplicate_review_is_rejected()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var session = CreateSession(
            test, mentor, student,
            SessionStatus.Completed);

        var service = CreateService(test);

        await service.CreateAsync(
            student.Id,
            session.Id,
            new CreateReviewRequest(5, "Helpful session."));

        await Assert.ThrowsAsync<ReviewConflictException>(
            () => service.CreateAsync(
                student.Id,
                session.Id,
                new CreateReviewRequest(
                    1, "Second review attempt.")));

        Assert.Equal(
            1,
            await test.Db.Reviews.CountAsync());
    }

    [Fact]
    public async Task Student_cannot_review_another_students_session()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var owner = test.AddUser(UserRole.Student);
        var stranger = test.AddUser(UserRole.Student);

        var session = CreateSession(
            test, mentor, owner,
            SessionStatus.Completed);

        var service = CreateService(test);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.CreateAsync(
                stranger.Id,
                session.Id,
                new CreateReviewRequest(
                    5, "Unauthorized review.")));
    }

    [Fact]
    public async Task Pending_session_cannot_be_reviewed()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var session = CreateSession(
            test, mentor, student,
            SessionStatus.Pending);

        var service = CreateService(test);

        await Assert.ThrowsAsync<ReviewNotAllowedException>(
            () => service.CreateAsync(
                student.Id,
                session.Id,
                new CreateReviewRequest(
                    4, "Too early.")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Invalid_ratings_are_rejected(
        int rating)
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(UserRole.Student);

        var session = CreateSession(
            test, mentor, student,
            SessionStatus.Completed);

        var service = CreateService(test);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(
                student.Id,
                session.Id,
                new CreateReviewRequest(
                    rating, "Rating test.")));
    }

    [Fact]
    public async Task Unverified_student_cannot_submit_review()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);
        var student = test.AddUser(
            UserRole.Student, verified: false);

        var session = CreateSession(
            test, mentor, student,
            SessionStatus.Completed);

        var service = CreateService(test);

        await Assert.ThrowsAsync<ReviewForbiddenException>(
            () => service.CreateAsync(
                student.Id,
                session.Id,
                new CreateReviewRequest(
                    5, "Review attempt.")));
    }

    [Fact]
    public async Task Mentor_average_uses_saved_reviews()
    {
        using var test = new TestDatabase();

        var mentor = test.AddUser(UserRole.Mentor);

        // The mentor must have a complete public profile.
        mentor.Bio =
            "I help students master quantitative aptitude " +
            "and improve their problem-solving skills.";

        mentor.SubjectsJson = "[\"Quant\"]";

        test.Db.SaveChanges();

        var student = test.AddUser(UserRole.Student);

        var first = CreateSession(
            test, mentor, student,
            SessionStatus.Completed);

        var second = CreateSession(
            test, mentor, student,
            SessionStatus.Completed);

        test.Db.Reviews.AddRange(
            new Review
            {
                SessionId = first.Id,
                Rating = 5,
                Comment = "Excellent.",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
            },
            new Review
            {
                SessionId = second.Id,
                Rating = 4,
                Comment = "Helpful.",
                CreatedAtUtc = DateTime.UtcNow
            });

        await test.Db.SaveChangesAsync();

        var service = CreateService(test);

        var result = await service.GetMentorReviewsAsync(
            student.Id,
            mentor.Id,
            page: 1,
            pageSize: 10);

        Assert.Equal(2, result.ReviewCount);
        Assert.NotNull(result.AverageRating);
        Assert.Equal(4.5, result.AverageRating.Value);
        Assert.Equal(2, result.Items.Count);
    }
}
