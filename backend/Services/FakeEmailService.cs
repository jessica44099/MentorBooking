using System.Text.RegularExpressions;
using MentorBooking.Api.Data;
using MentorBooking.Api.Models;
using MentorBooking.Api.Models.Enums;

namespace MentorBooking.Api.Services;

public interface IEmailService
{
    Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default);
}

public sealed class FakeEmailService : IEmailService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FakeEmailService> _logger;

    public FakeEmailService(
        AppDbContext db,
        IWebHostEnvironment environment,
        ILogger<FakeEmailService> logger)
    {
        _db = db;
        _environment = environment;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Fake email is only available in Development.");
        }

        // Never store raw verification or reset tokens
        // inside the persistent email log.
        string RedactTokens(string content)
        {
            return Regex.Replace(
                content,
                @"(?i)(token=)[0-9a-f]{64}",
                "$1[REDACTED]");
        }

        var log = new EmailLog
        {
            ToAddress = to,
            Subject = subject,
            HtmlBody = RedactTokens(htmlBody),
            TextBody = RedactTokens(textBody),
            Status = EmailStatus.Sent,
            AttemptCount = 1,
            CreatedAtUtc = DateTime.UtcNow,
            SentAtUtc = DateTime.UtcNow
        };

        try
        {
            _db.EmailLogs.Add(log);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to save the development email log.");

            _db.Entry(log).State =
                Microsoft.EntityFrameworkCore.EntityState.Detached;
        }

        // Development only: show the complete link.
        Console.WriteLine();
        Console.WriteLine("========== FAKE EMAIL ==========");
        Console.WriteLine($"To: {to}");
        Console.WriteLine($"Subject: {subject}");
        Console.WriteLine();
        Console.WriteLine(textBody);
        Console.WriteLine("================================");
        Console.WriteLine();
    }
}