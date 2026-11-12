
using System.Security.Claims;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/mentors/me/stats")]
[Authorize(Roles = "Mentor")]
public sealed class MentorDashboardStatsController
    : ControllerBase
{
    private readonly IMentorDashboardStatsService _stats;

    public MentorDashboardStatsController(
        IMentorDashboardStatsService stats)
    {
        _stats = stats;
    }

    // GET /api/mentors/me/stats
    [HttpGet]
    public async Task<IActionResult> GetMine(
        CancellationToken ct)
    {
        var claim = User.FindFirstValue("sub");

        if (!Guid.TryParse(claim, out var mentorId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            var result = await _stats.GetMineAsync(
                mentorId,
                ct);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                error = "Active mentor account not found."
            });
        }
    }
}
