using System.Net;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Production <see cref="IEmailService"/> implementation that delivers
/// outbound email through AWS Simple Email Service (SES) v2 using the
/// <c>AWSSDK.SimpleEmailV2</c> package.
///
/// <para>
/// Credentials are resolved via the AWS SDK default credential provider
/// chain, which in production uses the ECS task role (no access keys are
/// ever hard-coded or read from configuration) — see design "Email Delivery
/// (AWS SES)" and Requirement 13.6.
/// </para>
///
/// <para>
/// The sender ("From") address and the company admin inbox address are taken
/// from <see cref="EmailSettings"/> (<c>SENDER_EMAIL</c> / <c>ADMIN_EMAIL</c>,
/// or the equivalent Parameter Store values in AWS).
/// </para>
/// </summary>
public sealed class AwsSesEmailService : IEmailService
{
    private readonly IAmazonSimpleEmailServiceV2 _sesClient;
    private readonly EmailSettings _settings;
    private readonly ILogger<AwsSesEmailService> _logger;

    public AwsSesEmailService(
        IAmazonSimpleEmailServiceV2 sesClient,
        IOptions<EmailSettings> settings,
        ILogger<AwsSesEmailService> logger)
    {
        _sesClient = sesClient ?? throw new ArgumentNullException(nameof(sesClient));
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
    /// Builds and dispatches a single SES v2 <see cref="SendEmailRequest"/>
    /// with both plain-text and HTML bodies.
    /// </summary>
    private async Task SendAsync(
        string toEmail,
        string subject,
        string textBody,
        string htmlBody,
        string emailType)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = _settings.SenderEmail,
            Destination = new Destination
            {
                ToAddresses = new List<string> { toEmail }
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Text = new Content { Data = textBody, Charset = "UTF-8" },
                        Html = new Content { Data = htmlBody, Charset = "UTF-8" }
                    }
                }
            }
        };

        try
        {
            var response = await _sesClient.SendEmailAsync(request);
            _logger.LogInformation(
                "Sent {EmailType} email via SES (MessageId: {MessageId})",
                emailType,
                response.MessageId);
        }
        catch (Exception ex)
        {
            // Do not log the recipient address or message body (PII); record
            // only the email type and the failure so the caller can react.
            _logger.LogError(
                ex,
                "Failed to send {EmailType} email via SES: {ErrorMessage}",
                emailType,
                ex.Message);
            throw;
        }
    }
}
