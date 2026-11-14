
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit;

namespace MentorBooking.Api.Services;

// STEP 30.7 — REAL SMTP EMAIL SENDER

public sealed class SmtpQueuedEmailSender
    : IQueuedEmailSender
{
    private readonly EmailDeliveryOptions _options;

    public SmtpQueuedEmailSender(
        IOptions<EmailDeliveryOptions> options)
    {
        _options = options.Value;
    }

    public async Task<EmailDeliveryOutcome> SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct = default)
    {
        // 1. Validate SMTP configuration.

        if (!_options.Enabled)
        {
            throw new InvalidOperationException(
                "Email delivery is disabled.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.SmtpHost) ||
            string.IsNullOrWhiteSpace(
                _options.SmtpUsername) ||
            string.IsNullOrWhiteSpace(
                _options.SmtpPassword) ||
            string.IsNullOrWhiteSpace(
                _options.FromAddress))
        {
            throw new InvalidOperationException(
                "SMTP configuration is incomplete.");
        }

        if (_options.SmtpPort < 1 ||
            _options.SmtpPort > 65535)
        {
            throw new InvalidOperationException(
                "Invalid SMTP port.");
        }

        // TLS must never be optional for
        // authenticated SMTP connections.
        if (!_options.RequireStartTls)
        {
            throw new InvalidOperationException(
                "Encrypted SMTP is required.");
        }

        // 2. Construct the email message.

        var message = new MimeMessage();

        message.From.Add(
            new MailboxAddress(
                _options.FromName,
                _options.FromAddress));

        message.To.Add(
            MailboxAddress.Parse(to));

        message.Subject = subject;

        message.Body = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = textBody
        }.ToMessageBody();

        // 3. Connect securely.
        //
        // Port 465 uses TLS immediately.
        // Other ports require STARTTLS.
        //
        // STARTTLS must succeed; we do not
        // fall back to an unencrypted connection.

        var security =
            _options.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

        using var smtp = new SmtpClient();

        smtp.Timeout = 15000;

        await smtp.ConnectAsync(
            _options.SmtpHost,
            _options.SmtpPort,
            security,
            ct);

        // 4. Authenticate using credentials
        // supplied through configuration.
        await smtp.AuthenticateAsync(
            _options.SmtpUsername,
            _options.SmtpPassword,
            ct);

        // 5. Send the email.
        await smtp.SendAsync(
    FormatOptions.Default,
    message,
    ct);

        await smtp.DisconnectAsync(true, ct);

        // The SMTP server has accepted the
        // email for delivery.
        return EmailDeliveryOutcome.Delivered;
    }
}
