
using System.Security.Claims;
using MentorBooking.Api.Data;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/sessions")]
[Authorize(Roles = "Student,Mentor")]
public sealed class SessionsController : ControllerBase
{
    private readonly IBookingService _booking;
    private readonly AppDbContext _db;

    public SessionsController(
        IBookingService booking,
        AppDbContext db)
    {
        _booking = booking;
        _db = db;
    }

    // Read user identity from the validated JWT.
    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue("sub");

        return Guid.TryParse(claim, out userId);
    }

    // POST /api/sessions
    // Only students can request sessions.
    [HttpPost]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookingRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            var booking = await _booking.CreateAsync(
                studentId,
                request,
                ct);

            return StatusCode(
                StatusCodes.Status201Created,
                booking);
        }
        catch (BookingForbiddenException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = ex.Message });
        }
        catch (BookingConflictException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                error = ex.Message
            });
        }
    }

    // GET /api/sessions/mine
    // Students see their bookings.
    // Mentors see sessions assigned to them.
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        var isMentor = User.IsInRole("Mentor");

        var sessions = await _db.Sessions
            .AsNoTracking()
            .Where(x =>
                isMentor
                    ? x.MentorId == userId
                    : x.StudentId == userId)
            .OrderBy(x => x.ScheduledStartUtc)
            .ToListAsync(ct);

        // Return DTOs, never database entities.
        var response = sessions.Select(x =>
            new BookingResponse(
                x.Id,
                x.MentorId,
                x.StudentId,
                x.SlotId,
                x.Topic,
                x.Description,
                x.Mode.ToString(),
                DateTime.SpecifyKind(
                    x.ScheduledStartUtc,
                    DateTimeKind.Utc),
                DateTime.SpecifyKind(
                    x.ScheduledEndUtc,
                    DateTimeKind.Utc),
                x.Status.ToString(),
                DateTime.SpecifyKind(
                    x.CreatedAtUtc,
                    DateTimeKind.Utc)
            )).ToList();

        return Ok(response);
    }
}
