
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/sessions")]
[Authorize(Roles = "Mentor")]
public sealed class SessionRecordCorrectionController
    : ControllerBase
{
    private readonly ISessionRecordCorrectionService _corrections;

    public SessionRecordCorrectionController(
        ISessionRecordCorrectionService corrections)
    {
        _corrections = corrections;
    }

    private bool TryGetMentorId(out Guid mentorId)
    {
        return Guid.TryParse(
            User.FindFirstValue("sub"),
            out mentorId);
    }

    // PUT /api/sessions/{id}/record
    [HttpPut("{id:guid}/record")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSessionRecordRequest request,
        CancellationToken ct)
    {
        if (!TryGetMentorId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            var result = await _corrections.UpdateAsync(
                mentorId,
                id,
                request,
                ct);

            return Ok(result);
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

    // GET /api/sessions/{id}/record/audit
    [HttpGet("{id:guid}/record/audit")]
    public async Task<IActionResult> GetAudit(
        Guid id,
        CancellationToken ct)
    {
        if (!TryGetMentorId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            var result = await _corrections.GetAuditAsync(
                mentorId,
                id,
                ct);

            return Ok(result);
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
    }
}
