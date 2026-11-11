
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/sessions")]
[Authorize]
public sealed class SessionLifecycleController : ControllerBase
{
    private readonly ISessionLifecycleService _lifecycle;

    public SessionLifecycleController(
        ISessionLifecycleService lifecycle)
    {
        _lifecycle = lifecycle;
    }

    // Read the user ID from the validated JWT.
    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue("sub");

        return Guid.TryParse(value, out userId);
    }

    // Convert known service errors into HTTP responses.
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
                new { error = ex.Message });
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

    // POST /api/sessions/{id}/approve
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody] ApproveSessionRequest request,
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
            _lifecycle.ApproveAsync(
                mentorId,
                id,
                request,
                ct));
    }

    // POST /api/sessions/{id}/reject
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> Reject(
        Guid id,
        [FromBody] RejectSessionRequest request,
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
            _lifecycle.RejectAsync(
                mentorId,
                id,
                request,
                ct));
    }

    // POST /api/sessions/{id}/cancel
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Student,Mentor")]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] CancelSessionRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var actorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(() =>
            _lifecycle.CancelAsync(
                actorId,
                id,
                request,
                ct));
    }
}
