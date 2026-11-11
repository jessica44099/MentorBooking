
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/availability")]
[Authorize(Roles = "Mentor")]
public sealed class AvailabilityController : ControllerBase
{
    private readonly IAvailabilityService _availability;

    public AvailabilityController(
        IAvailabilityService availability)
    {
        _availability = availability;
    }

    // Read the mentor ID from the validated JWT.
    private bool TryGetMentorId(out Guid mentorId)
    {
        var value = User.FindFirstValue("sub");

        return Guid.TryParse(value, out mentorId);
    }

    // Convert expected service errors into HTTP responses.
    private async Task<IActionResult> ExecuteAsync(
        Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AvailabilityConflictException ex)
        {
            return Conflict(new
            {
                error = ex.Message,
                clashes = ex.Clashes
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
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // GET /api/availability/mine
    // View availability for a Monday-Sunday week.
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(
        [FromQuery] DateTime weekStartUtc,
        CancellationToken ct)
    {
        if (!TryGetMentorId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(async () =>
        {
            var slots = await _availability.GetWeekAsync(
                mentorId,
                weekStartUtc,
                ct);

            return Ok(slots);
        });
    }

    // POST /api/availability/clash-check
    // Check whether a slot would overlap existing slots.
    [HttpPost("clash-check")]
    public async Task<IActionResult> CheckClashes(
        [FromBody] SaveAvailabilitySlotRequest request,
        [FromQuery] Guid? excludeSlotId,
        CancellationToken ct)
    {
        if (!TryGetMentorId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(async () =>
        {
            var clashes =
                await _availability.CheckClashesAsync(
                    mentorId,
                    request,
                    excludeSlotId,
                    ct);

            return Ok(new
            {
                hasClashes = clashes.Count > 0,
                clashes
            });
        });
    }

    // POST /api/availability
    // Create an Available or Busy slot.
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] SaveAvailabilitySlotRequest request,
        [FromQuery] bool replaceExisting,
        CancellationToken ct)
    {
        if (!TryGetMentorId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(async () =>
        {
            var created = await _availability.CreateAsync(
                mentorId,
                request,
                replaceExisting,
                ct);

            return StatusCode(
                StatusCodes.Status201Created,
                created);
        });
    }

    // PUT /api/availability/{id}
    // Update an unused slot owned by this mentor.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] SaveAvailabilitySlotRequest request,
        [FromQuery] bool replaceExisting,
        CancellationToken ct)
    {
        if (!TryGetMentorId(out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(async () =>
        {
            var updated = await _availability.UpdateAsync(
                mentorId,
                id,
                request,
                replaceExisting,
                ct);

            return Ok(updated);
        });
    }

    // DELETE /api/availability/{id}
    // Delete an unused slot owned by this mentor.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
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

        return await ExecuteAsync(async () =>
        {
            await _availability.DeleteAsync(
                mentorId,
                id,
                ct);

            return NoContent();
        });
    }
}
