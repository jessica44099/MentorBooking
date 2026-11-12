
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/sessions")]
[Authorize(Roles = "Student,Mentor")]
public sealed class RescheduleSafetyController : ControllerBase
{
    private readonly IRescheduleSafetyService _service;

    public RescheduleSafetyController(
        IRescheduleSafetyService service)
    {
        _service = service;
    }

    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(
            User.FindFirstValue("sub"),
            out userId);
    }

    private async Task<IActionResult> ExecuteAsync<T>(
        Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidSessionTransitionException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (SessionLifecycleForbiddenException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (SessionLifecycleConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // GET /api/sessions/{id}/reschedule/history
    // Either participant can view the session history.
    [HttpGet("{id:guid}/reschedule/history")]
    public async Task<IActionResult> GetHistory(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        var role = User.IsInRole("Mentor")
            ? UserRole.Mentor
            : UserRole.Student;

        return await ExecuteAsync(() =>
            _service.GetHistoryAsync(
                userId, role, id, page, pageSize, ct));
    }

    // POST /api/sessions/{id}/reschedule/check
    // Only the assigned mentor can check a new time.
    [HttpPost("{id:guid}/reschedule/check")]
    [Authorize(Roles = "Mentor")]
    public async Task<IActionResult> Check(
        Guid id,
        [FromBody] CheckRescheduleRequest request,
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
            _service.CheckAsync(
                mentorId, id, request, ct));
    }
}
