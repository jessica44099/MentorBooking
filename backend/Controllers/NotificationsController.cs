
using System.Security.Claims;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = "Student,Mentor")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(
        INotificationService notifications)
    {
        _notifications = notifications;
    }

    // Obtain the user ID from the authenticated JWT.
    // Never accept a user ID supplied by the client.
    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(
            User.FindFirstValue("sub"),
            out userId);
    }

    // Convert expected service errors into HTTP responses.
    private async Task<IActionResult> ExecuteAsync(
        Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
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

    // GET /api/notifications/mine
    // Get the logged-in user's notifications.
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool unreadOnly = false,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(async () =>
        {
            var result = await _notifications.GetMineAsync(
                userId,
                page,
                pageSize,
                unreadOnly,
                ct);

            return Ok(result);
        });
    }

    // GET /api/notifications/unread-count
    // Count unread notifications for the logged-in user.
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(async () =>
        {
            var result =
                await _notifications.GetUnreadCountAsync(
                    userId,
                    ct);

            return Ok(result);
        });
    }

    // PATCH /api/notifications/{id}/read
    // Mark one of the user's notifications as read.
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(
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

        return await ExecuteAsync(async () =>
        {
            await _notifications.MarkReadAsync(
                userId,
                id,
                ct);

            return NoContent();
        });
    }

    // PATCH /api/notifications/read-all
    // Mark all notifications belonging to the user as read.
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        return await ExecuteAsync(async () =>
        {
            var result =
                await _notifications.MarkAllReadAsync(
                    userId,
                    ct);

            return Ok(result);
        });
    }
}
