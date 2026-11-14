
namespace MentorBooking.Api.Services;

public sealed class SmtpAccountEmailService
    : IEmailService
{
    private readonly IQueuedEmailSender _sender;

    public SmtpAccountEmailService(
        IQueuedEmailSender sender)
    {
        _sender = sender;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.SendAsync(
            to,
            subject,
            htmlBody,
            textBody,
            cancellationToken);

        if (result != EmailDeliveryOutcome.Delivered)
        {
            throw new InvalidOperationException(
                "Account email was not delivered " +
                "to the SMTP provider.");
        }
    }
}
