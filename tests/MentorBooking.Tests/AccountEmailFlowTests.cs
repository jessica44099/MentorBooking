using System.Text.RegularExpressions;
using MentorBooking.Api.Models.Enums;
using MentorBooking.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MentorBooking.Tests;

public sealed class AccountEmailFlowTests
{
    private sealed class RecordingEmailService : IEmailService
    {
        public List<string> Bodies { get; } = new();
        public bool FailDelivery { get; set; }

        public Task SendAsync(
            string to,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken cancellationToken = default)
        {
            if (FailDelivery)
            {
                throw new InvalidOperationException("Simulated SMTP rejection.");
            }

            Bodies.Add(textBody);
            return Task.CompletedTask;
        }
    }

    private static AccountTokenService CreateTokens(
        TestDatabase test,
        IEmailService sender)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "http://localhost:4200"
            })
            .Build();

        return new AccountTokenService(
            test.Db,
            sender,
            configuration,
            NullLogger<AccountTokenService>.Instance);
    }

    private static string ExtractToken(string text)
    {
        var match = Regex.Match(text, @"token=([A-F0-9]{64})");
        Assert.True(match.Success, "Email must contain a working token link.");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Verification_link_is_emailed_and_token_is_single_use()
    {
        using var test = new TestDatabase();
        var user = test.AddUser(UserRole.Student, verified: false);
        var sender = new RecordingEmailService();
        var tokens = CreateTokens(test, sender);

        await tokens.SendVerificationAsync(user.Id);

        Assert.Single(sender.Bodies);
        Assert.Contains("http://localhost:4200/verify-email?token=", sender.Bodies[0]);

        var rawToken = ExtractToken(sender.Bodies[0]);
        var record = Assert.Single(test.Db.AuthTokens);
        Assert.NotEqual(rawToken, record.TokenHash);

        Assert.True(await tokens.VerifyEmailAsync(rawToken));
        Assert.True((await test.Db.Users.FindAsync(user.Id))!.IsEmailVerified);
        Assert.False(await tokens.VerifyEmailAsync(rawToken));
    }

    [Fact]
    public async Task Password_reset_link_changes_password_without_logging_raw_token()
    {
        using var test = new TestDatabase();
        var user = test.AddUser(UserRole.Mentor);
        var sender = new RecordingEmailService();
        var tokens = CreateTokens(test, sender);

        await tokens.RequestPasswordResetAsync(user.Email);
        Assert.Single(sender.Bodies);
        Assert.Contains("http://localhost:4200/reset-password?token=", sender.Bodies[0]);

        var rawToken = ExtractToken(sender.Bodies[0]);
        var saved = Assert.Single(test.Db.AuthTokens);
        Assert.NotEqual(rawToken, saved.TokenHash);

        Assert.True(await tokens.ResetPasswordAsync(
            rawToken, "SecurePassword123", "SecurePassword123"));
        Assert.True(BCrypt.Net.BCrypt.Verify(
            "SecurePassword123", (await test.Db.Users.FindAsync(user.Id))!.PasswordHash));
        Assert.False(await tokens.ResetPasswordAsync(
            rawToken, "NewPassword456", "NewPassword456"));
    }

    [Fact]
    public async Task Unknown_reset_email_does_not_reveal_account_existence()
    {
        using var test = new TestDatabase();
        var sender = new RecordingEmailService();
        var tokens = CreateTokens(test, sender);

        await tokens.RequestPasswordResetAsync("unknown@example.com");

        Assert.Empty(sender.Bodies);
        Assert.Empty(test.Db.AuthTokens);
    }

    [Fact]
    public async Task Smtp_rejection_is_reported_instead_of_claiming_success()
    {
        using var test = new TestDatabase();
        var user = test.AddUser(UserRole.Student, verified: false);
        var sender = new RecordingEmailService { FailDelivery = true };
        var tokens = CreateTokens(test, sender);

        await Assert.ThrowsAsync<AccountEmailDeliveryException>(
            () => tokens.SendVerificationAsync(user.Id));

        Assert.False((await test.Db.Users.FindAsync(user.Id))!.IsEmailVerified);
    }
}
