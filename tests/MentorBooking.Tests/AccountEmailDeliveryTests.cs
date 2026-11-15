using System.Text.RegularExpressions;
using MentorBooking.Api.Controllers;
using MentorBooking.Api.DTOs;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MentorBooking.Tests;

public sealed class AccountEmailDeliveryTests
{
    private static IConfiguration Settings() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "http://localhost:4200",
                ["Jwt:Key"] = "mentorbooking-test-only-key-32-bytes-long!",
                ["Jwt:Issuer"] = "MentorBooking.Api",
                ["Jwt:Audience"] = "MentorBooking.Client"
            })
            .Build();

    private static AccountTokenService Tokens(TestDatabase test, IEmailService email) =>
        new(test.Db, email, Settings(), NullLogger<AccountTokenService>.Instance);

    private sealed class RecordingEmail : IEmailService
    {
        public readonly List<(string To, string Subject, string Html, string Text)> Sent = new();
        public bool Fail { get; set; }

        public Task SendAsync(
            string to, string subject, string htmlBody, string textBody,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail)
                throw new IOException("Injected SMTP failure for test.");
            Sent.Add((to, subject, htmlBody, textBody));
            return Task.CompletedTask;
        }
    }

    private static string RawToken(string body)
    {
        var match = Regex.Match(body, @"token=([0-9a-fA-F]{64})");
        Assert.True(match.Success, "Expected a token in the Angular email link.");
        return match.Groups[1].Value;
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Mentor")]
    public async Task Registration_emails_real_registered_address_and_link_verifies(string role)
    {
        using var test = new TestDatabase();
        var mail = new RecordingEmail();
        var tokens = Tokens(test, mail);
        var auth = new AuthService(test.Db, Settings(), tokens);
        var address = Guid.NewGuid().ToString("N") + "@example.com";

        var registered = await auth.RegisterAsync(
            new RegisterRequest("Test Account", address, "Password123!", "Password123!", role));

        Assert.Equal(role, registered.Role);
        Assert.False(registered.IsEmailVerified);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(address, sent.To);
        Assert.Contains("http://localhost:4200/verify-email?token=", sent.Text);
        var raw = RawToken(sent.Text);
        Assert.True(await tokens.VerifyEmailAsync(raw));
        Assert.False(await tokens.VerifyEmailAsync(raw));
        Assert.True((await test.Db.Users.SingleAsync(x => x.Id == registered.UserId)).IsEmailVerified);
        Assert.DoesNotContain(raw, (await test.Db.AuthTokens.SingleAsync()).TokenHash);
    }

    [Fact]
    public async Task Forgot_password_uses_angular_link_and_token_is_single_use()
    {
        using var test = new TestDatabase();
        var user = test.AddUser(UserRole.Student);
        var mail = new RecordingEmail();
        var tokens = Tokens(test, mail);

        await tokens.RequestPasswordResetAsync(user.Email);
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(user.Email, sent.To);
        Assert.Contains("http://localhost:4200/reset-password?token=", sent.Text);
        var token = RawToken(sent.Text);

        Assert.True(await tokens.ResetPasswordAsync(token, "NewPassword123!", "NewPassword123!"));
        Assert.False(await tokens.ResetPasswordAsync(token, "AnotherPassword123!", "AnotherPassword123!"));
        Assert.True(BCrypt.Net.BCrypt.Verify("NewPassword123!", user.PasswordHash));
    }

    [Fact]
    public async Task Registration_failure_is_visible_and_account_can_resend_later()
    {
        using var test = new TestDatabase();
        var mail = new RecordingEmail { Fail = true };
        var tokens = Tokens(test, mail);
        var auth = new AuthService(test.Db, Settings(), tokens);
        var controller = new AuthController(auth, tokens);
        var address = Guid.NewGuid().ToString("N") + "@example.com";

        var response = await controller.Register(
            new RegisterRequest("Test Account", address, "Password123!", "Password123!", "Student"),
            CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(response);
        Assert.Equal(503, error.StatusCode);
        Assert.Contains("Account created", error.Value!.ToString());
        var registered = await test.Db.Users.SingleAsync(x => x.Email == address);
        Assert.False(registered.IsEmailVerified);
        Assert.Single(await test.Db.AuthTokens.ToListAsync());

        mail.Fail = false;
        await tokens.SendVerificationAsync(registered.Id, isResend: true);
        Assert.Single(mail.Sent);
    }

    [Fact]
    public async Task Forgot_password_returns_identical_response_for_unknown_and_delivery_failure()
    {
        using var test = new TestDatabase();
        var user = test.AddUser(UserRole.Student);
        var mail = new RecordingEmail { Fail = true };
        var tokens = Tokens(test, mail);
        var auth = new AuthService(test.Db, Settings(), tokens);
        var controller = new AuthController(auth, tokens);

        var known = Assert.IsType<OkObjectResult>(await controller.ForgotPassword(
            new ForgotPasswordRequest(user.Email), CancellationToken.None));
        var unknown = Assert.IsType<OkObjectResult>(await controller.ForgotPassword(
            new ForgotPasswordRequest("unknown@example.com"), CancellationToken.None));

        Assert.Equal(known.Value!.ToString(), unknown.Value!.ToString());
        Assert.Empty(mail.Sent);
    }
}
