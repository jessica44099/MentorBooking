
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Authorize(Roles = "Student")]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;

    public ReviewsController(IReviewService reviews)
    {
        _reviews = reviews;
    }

    private bool TryGetStudentId(out Guid studentId)
    {
        return Guid.TryParse(
            User.FindFirstValue("sub"),
            out studentId);
    }

    // POST /api/sessions/{id}/review
    [HttpPost("api/sessions/{id:guid}/review")]
    public async Task<IActionResult> Create(
        Guid id,
        [FromBody] CreateReviewRequest request,
        CancellationToken ct)
    {
        if (!TryGetStudentId(out var studentId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            var review = await _reviews.CreateAsync(
                studentId,
                id,
                request,
                ct);

            return StatusCode(
                StatusCodes.Status201Created,
                review);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ReviewNotAllowedException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ReviewConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ReviewForbiddenException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // GET /api/sessions/{id}/review
    [HttpGet("api/sessions/{id:guid}/review")]
    public async Task<IActionResult> GetMine(
        Guid id,
        CancellationToken ct)
    {
        if (!TryGetStudentId(out var studentId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            var review = await _reviews.GetMineAsync(
                studentId,
                id,
                ct);

            return Ok(review);
        }
        catch (ReviewForbiddenException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // GET /api/mentors/{mentorId}/reviews
    [HttpGet("api/mentors/{mentorId:guid}/reviews")]
    public async Task<IActionResult> GetMentorReviews(
        Guid mentorId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!TryGetStudentId(out var studentId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            var result =
                await _reviews.GetMentorReviewsAsync(
                    studentId,
                    mentorId,
                    page,
                    pageSize,
                    ct);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (MentorDiscoveryForbiddenException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
