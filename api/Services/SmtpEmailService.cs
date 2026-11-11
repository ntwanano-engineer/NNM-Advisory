using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Local-development <see cref="IEmailService"/> implementation that delivers
/// outbound email through a plain SMTP relay using the built-in
/// <see cref="System.Net.Mail.SmtpClient"/>.
///
/// <para>
/// Intended for use with <see href="https://github.com/mailhog/MailHog">Mailhog</see>
/// in the local Docker Compose stack. Mailhog accepts unauthenticated SMTP on
/// the host/port configured via <c>SMTP_HOST</c> / <c>SMTP_PORT</c>
/// (<see cref="EmailSettings.SmtpHost"/> / <see cref="EmailSettings.SmtpPort"/>)
/// and captures every message in its web UI instead of relaying externally.
/// </para>
///
/// <para>
/// The sender ("From") address and the company admin inbox address are taken
/// from <see cref="EmailSettings"/> (<c>SENDER_EMAIL</c> / <c>ADMIN_EMAIL</c>).
/// Email content mirrors <see cref="AwsSesEmailService"/> so that messages are
/// identical regardless of the active provider.
/// </para>
///
/// <para>
/// This implementation is selected at startup when <c>Email:Provider</c>
/// (<c>EMAIL_PROVIDER</c>) is <c>"smtp"</c>.
/// </para>
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IOptions<EmailSettings> settings,
        ILogger<SmtpEmailService> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task SendAcknowledgementEmailAsync(string toEmail, string clientName)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new ArgumentException("Recipient email must be provided.", nameof(toEmail));
        }

        // Requirement 5.4: personalised greeting with the client's full name,
        // confirming that NNM Advisory has received the message.
        var safeName = string.IsNullOrWhiteSpace(clientName) ? "there" : clientName.Trim();

        const string subject = "We've received your message — NNM Advisory";

        var textBody =
            $"Dear {safeName},\r\n\r\n" +
            "Thank you for contacting NNM Advisory. We have received your message " +
            "and a member of our team will be in touch with you shortly.\r\n\r\n" +
            "Kind regards,\r\n" +
            "The NNM Advisory Team";

        var htmlBody =
            "<html><body>" +
            $"<p>Dear {WebUtility.HtmlEncode(safeName)},</p>" +
            "<p>Thank you for contacting <strong>NNM Advisory</strong>. " +
            "We have received your message and a member of our team will be in " +
            "touch with you shortly.</p>" +
            "<p>Kind regards,<br/>The NNM Advisory Team</p>" +
            "</body></html>";

        await SendAsync(
            toEmail: toEmail,
            subject: subject,
            textBody: textBody,
            htmlBody: htmlBody,
            emailType: "acknowledgement");
    }

    /// <inheritdoc />
    public async Task SendAdminNotificationEmailAsync(ContactSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        // Requirement 5.5: the admin notification MUST include the client's
        // full name, email, subject, and message body.
        var subject = $"New contact submission: {submission.Subject}";

        var textBody =
            "A new contact form submission has been received.\r\n\r\n" +
            $"Full name: {submission.FullName}\r\n" +
            $"Email: {submission.Email}\r\n" +
            $"Subject: {submission.Subject}\r\n\r\n" +
            "Message:\r\n" +
            $"{submission.Message}\r\n";

        var htmlBody =
            "<html><body>" +
            "<p>A new contact form submission has been received.</p>" +
            "<ul>" +
            $"<li><strong>Full name:</strong> {WebUtility.HtmlEncode(submission.FullName)}</li>" +
            $"<li><strong>Email:</strong> {WebUtility.HtmlEncode(submission.Email)}</li>" +
            $"<li><strong>Subject:</strong> {WebUtility.HtmlEncode(submission.Subject)}</li>" +
            "</ul>" +
            "<p><strong>Message:</strong></p>" +
            $"<p>{WebUtility.HtmlEncode(submission.Message)}</p>" +
            "</body></html>";

        await SendAsync(
            toEmail: _settings.AdminEmail,
            subject: subject,
            textBody: textBody,
            htmlBody: htmlBody,
            emailType: "admin-notification");
    }

    /// <summary>
    /// Builds and dispatches a single <see cref="MailMessage"/> (with both a
    /// plain-text body and an HTML alternate view) through the configured SMTP
    /// relay. Mailhog requires no authentication and no TLS, so the client is
    /// constructed with the host/port from <see cref="EmailSettings"/> only.
    /// </summary>
    private async Task SendAsync(
        string toEmail,
        string subject,
        string textBody,
        string htmlBody,
        string emailType)
    {
        var host = _settings.SmtpHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "Email:SmtpHost (SMTP_HOST) must be configured when Email:Provider is 'smtp'.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.SenderEmail),
            Subject = subject,
            Body = textBody,
            IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        message.To.Add(new MailAddress(toEmail));

        // Attach the HTML body as an alternate view so capable clients render
        // the rich version while plain-text clients fall back to Body.
        var htmlView = AlternateView.CreateAlternateViewFromString(
            htmlBody, System.Text.Encoding.UTF8, "text/html");
        message.AlternateViews.Add(htmlView);

        // Mailhog (and any local relay) listens on the configured host/port with
        // no authentication and no TLS.
        using var client = new SmtpClient(host, _settings.SmtpPort)
        {
            EnableSsl = false,
            UseDefaultCredentials = false,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        try
        {
            await client.SendMailAsync(message);
            _logger.LogInformation(
                "Sent {EmailType} email via SMTP relay {SmtpHost}:{SmtpPort}",
                emailType,
                host,
                _settings.SmtpPort);
        }
        catch (Exception ex)
        {
            // Do not log the recipient address or message body (PII); record
            // only the email type and the failure so the caller can react.
            _logger.LogError(
                ex,
                "Failed to send {EmailType} email via SMTP relay: {ErrorMessage}",
                emailType,
                ex.Message);
            throw;
        }
    }
}
