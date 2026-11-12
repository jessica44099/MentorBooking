
using System.Security.Claims;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/mentors/me/profile")]
[Authorize(Roles = "Mentor")]
public sealed class MentorProfileController
    : ControllerBase
{
    private readonly IMentorProfileService _profiles;

    public MentorProfileController(
        IMentorProfileService profiles)
    {
        _profiles = profiles;
    }

    private bool TryGetMentorId(out Guid mentorId)
    {
        var userId = User.FindFirstValue("sub");

        return Guid.TryParse(userId, out mentorId);
    }

    // GET /api/mentors/me/profile
    [HttpGet]
    public async Task<IActionResult> GetMine(
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
            var profile = await _profiles.GetMineAsync(
                mentorId,
                ct);

            return Ok(profile);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                error = "Mentor profile not found."
            });
        }
    }

    // PUT /api/mentors/me/profile
    [HttpPut]
    public async Task<IActionResult> UpdateMine(
        [FromBody] SaveMentorProfileRequest request,
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
            var profile = await _profiles.UpdateMineAsync(
                mentorId,
                request,
                ct);

            return Ok(profile);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                error = "Mentor profile not found."
            });
        }
    }
}
