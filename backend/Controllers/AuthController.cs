
using System.IdentityModel.Tokens.Jwt;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MentorBooking.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IAccountTokenService _tokens;

    public AuthController(
        IAuthService auth,
        IAccountTokenService tokens)
    {
        _auth = auth;
        _tokens = tokens;
    }

    // REGISTER
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _auth.RegisterAsync(
                request, cancellationToken);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
            when (ex.Message == "Email is already registered.")
        {
            return Conflict(new
            {
                error = ex.Message
            });
        }
    }

    // LOGIN
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _auth.LoginAsync(
                request, cancellationToken);

            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new
            {
                error = "Invalid email or password."
            });
        }
    }

    // VERIFY EMAIL
    [AllowAnonymous]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var successful = await _tokens.VerifyEmailAsync(
            request.Token,
            cancellationToken);

        if (!successful)
        {
            return BadRequest(new
            {
                error = "Invalid or expired verification token."
            });
        }

        return Ok(new
        {
            message = "Email verified successfully."
        });
    }

    // RESEND VERIFICATION EMAIL
    [Authorize]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new
            {
                error = "Invalid authentication token."
            });
        }

        try
        {
            await _tokens.SendVerificationAsync(
                userId,
                isResend: true,
                cancellationToken: cancellationToken);

            return Ok(new
            {
                message =
                    "Verification request processed."
            });
        }
        catch (InvalidOperationException ex)
            when (ex.Message.StartsWith("Maximum 3"))
        {
            return StatusCode(429, new
            {
                error = ex.Message
            });
        }
        catch (InvalidOperationException ex)
            when (ex.Message == "Account not found.")
        {
            return NotFound(new
            {
                error = ex.Message
            });
        }
    }

    // FORGOT PASSWORD
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _tokens.RequestPasswordResetAsync(
            request.Email,
            cancellationToken);

        // Identical response for existing and unknown emails.
        return Ok(new
        {
            message =
                "If an account exists for this email, " +
                "password reset instructions will be sent."
        });
    }

    // RESET PASSWORD
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var successful = await _tokens.ResetPasswordAsync(
                request.Token,
                request.Password,
                request.ConfirmPassword,
                cancellationToken);

            if (!successful)
            {
                return BadRequest(new
                {
                    error = "Invalid or expired reset token."
                });
            }

            return Ok(new
            {
                message = "Password reset successfully."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message
            });
        }
    }
}
