
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/mentors")]
[Authorize(Roles = "Student")]
public sealed class MentorDiscoveryController
    : ControllerBase
{
    private readonly IMentorDiscoveryService _discovery;

    public MentorDiscoveryController(
        IMentorDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    private bool TryGetStudentId(out Guid studentId)
    {
        var value = User.FindFirstValue("sub");

        return Guid.TryParse(value, out studentId);
    }

    // GET /api/mentors
    // Optional: q, subject, page, pageSize
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? subject,
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
            var result = await _discovery.SearchAsync(
                studentId,
                q,
                subject,
                page,
                pageSize,
                ct);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (MentorDiscoveryForbiddenException)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "Student account is inactive."
                });
        }
    }

    // GET /api/mentors/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
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
            var mentor = await _discovery.GetByIdAsync(
                studentId,
                id,
                ct);

            return Ok(mentor);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                error = "Mentor not found."
            });
        }
        catch (MentorDiscoveryForbiddenException)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    error = "Student account is inactive."
                });
        }
    }
}
