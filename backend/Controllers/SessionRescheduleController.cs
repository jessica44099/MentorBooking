
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/sessions")]
[Authorize(Roles = "Student,Mentor")]
public sealed class SessionRescheduleController
    : ControllerBase
{
    private readonly ISessionRescheduleService _reschedule;

    public SessionRescheduleController(
        ISessionRescheduleService reschedule)
    {
        _reschedule = reschedule;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue("sub");

        return Guid.TryParse(claim, out userId);
    }

    // Map expected service errors to HTTP responses.
    private async Task<IActionResult> ExecuteAsync(
        Func<Task<RescheduleResponse>> action)
    {
        try
        {
            var result = await action();

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidSessionTransitionException ex)
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

    // Only the mentor can propose a new time.
    // POST /api/sessions/{id}/reschedule
    [HttpPost("{id:guid}/reschedule")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> Propose(
        Guid id,
        [FromBody] ProposeRescheduleRequest request,
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
            _reschedule.ProposeAsync(
                mentorId,
                id,
                request,
                ct));
    }

    // Only the assigned student can accept.
    // POST /api/sessions/{id}/reschedule/accept
    [HttpPost("{id:guid}/reschedule/accept")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Accept(
        Guid id,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var studentId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(() =>
            _reschedule.AcceptAsync(
                studentId,
                id,
                ct));
    }
}
