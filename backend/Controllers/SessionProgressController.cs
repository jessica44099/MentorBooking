
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
public sealed class SessionProgressController : ControllerBase
{
    private readonly ISessionProgressService _progress;
    private readonly AppDbContext _db;

    public SessionProgressController(
        ISessionProgressService progress,
        AppDbContext db)
    {
        _progress = progress;
        _db = db;
    }

    // Read identity only from the validated JWT.
    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue("sub");

        return Guid.TryParse(value, out userId);
    }

    // Translate known service errors into HTTP responses.
    private async Task<IActionResult> ExecuteAsync(
        Func<Task<SessionTransitionResponse>> action)
    {
        try
        {
            var result = await action();

            return Ok(result);
        }
        catch (InvalidSessionTransitionException ex)
        {
            return BadRequest(new
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
        catch (SessionLifecycleForbiddenException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
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
        catch (SessionLifecycleConflictException ex)
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
    }

    // POST /api/sessions/{id}/start
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> Start(
        Guid id,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(() =>
            _progress.StartAsync(
                mentorId,
                id,
                ct));
    }

    // POST /api/sessions/{id}/complete
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> Complete(
        Guid id,
        [FromBody] CompleteSessionRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(() =>
            _progress.CompleteAsync(
                mentorId,
                id,
                request,
                ct));
    }

    // POST /api/sessions/{id}/no-show
    [HttpPost("{id:guid}/no-show")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> MarkNoShow(
        Guid id,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(() =>
            _progress.MarkNoShowAsync(
                mentorId,
                id,
                ct));
    }

    // GET /api/sessions/{id}/record
    // Read a completion record without exposing
    // private mentor notes to students.
    [HttpGet("{id:guid}/record")]
    public async Task<IActionResult> GetRecord(
        Guid id,
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

        // Reject deleted or inactive accounts.
        var activeUser = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == userId && !x.IsDeleted,
                ct);

        if (!activeUser)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "Account is inactive."
                });
        }

        // Return 404 for sessions belonging to other users.
        var session = await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id &&
                     (
                         isMentor
                             ? x.MentorId == userId
                             : x.StudentId == userId
                     ),
                ct);

        if (session == null)
        {
            return NotFound(new
            {
                error = "Session not found."
            });
        }

        var record = await _db.SessionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.SessionId == id,
                ct);

        if (record == null)
        {
            return NotFound(new
            {
                error = "No completion record exists."
            });
        }

        var completedAtUtc = DateTime.SpecifyKind(
            record.CompletedAtUtc,
            DateTimeKind.Utc);

        // Mentors can read their own private notes.
        if (isMentor)
        {
            return Ok(new
            {
                sessionId = record.SessionId,
                doubtsCleared = record.DoubtsCleared,
                summary = record.Summary,
                resolution = record.Resolution.ToString(),
                privateMentorNote =
                    record.PrivateMentorNote,
                completedAtUtc
            });
        }

        // Students receive only shared information.
        // Do not include privateMentorNote at all.
        return Ok(new
        {
            sessionId = record.SessionId,
            doubtsCleared = record.DoubtsCleared,
            summary = record.Summary,
            resolution = record.Resolution.ToString(),
            completedAtUtc
        });
    }
}
