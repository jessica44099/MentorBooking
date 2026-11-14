
namespace MentorBooking.Api.Services;

public sealed class EmailDeliveryOptions
{
    public const string SectionName = "EmailDelivery";

    // Keep real email delivery disabled by default.
    public bool Enabled { get; set; } = false;

    // Sender information.
    public string FromName { get; set; } =
        "MentorBooking";

    public string FromAddress { get; set; } =
        string.Empty;

    // SMTP server settings.
    public string SmtpHost { get; set; } =
        string.Empty;

    public int SmtpPort { get; set; } = 587;

    // Require an encrypted SMTP connection.
    public bool RequireStartTls { get; set; } = true;

    // Authentication credentials.
    // These will come from .NET User Secrets
    // or environment variables, not GitHub.
    public string SmtpUsername { get; set; } =
        string.Empty;

    public string SmtpPassword { get; set; } =
        string.Empty;
}
