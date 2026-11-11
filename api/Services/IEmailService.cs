using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Defines the contract for outbound email delivery.
///
/// Two email types are sent on each successful contact form submission:
///   1. An Acknowledgement Email to the client (Requirement 5.3, 5.4).
///   2. An Admin Notification Email to the company inbox (Requirement 5.5).
///
/// Concrete implementations:
///   - <see cref="SmtpEmailService"/>  — local development (Mailhog / any SMTP relay).
///   - <see cref="AwsSesEmailService"/> — production (AWS SES v2).
///
/// The active implementation is selected at startup via the
/// <c>Email:Provider</c> configuration value ("smtp" | "ses").
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a personalised acknowledgement email to the client who submitted
    /// the contact form, confirming that NNM Advisory has received their message.
    ///
    /// <para>
    /// The email body MUST include a personalised greeting that addresses the
    /// client by their full name, and MUST confirm receipt of the message
    /// (Requirement 5.4).
    /// </para>
    /// </summary>
    /// <param name="toEmail">
    /// The recipient email address (the client's address as submitted via the
    /// Contact Form — max 254 characters, RFC 5321).
    /// </param>
    /// <param name="clientName">
    /// The client's full name as submitted via the Contact Form, used to
    /// personalise the greeting (max 100 characters).
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the email has been accepted
    /// by the underlying mail provider, or faults if delivery acceptance fails.
    /// </returns>
    Task SendAcknowledgementEmailAsync(string toEmail, string clientName);

    /// <summary>
    /// Sends a notification email to the company admin inbox informing NNM
    /// Advisory staff that a new contact form submission has been received.
    ///
    /// <para>
    /// The email body MUST contain ALL four of the following fields from the
    /// submission: full name, email address, subject, and message body
    /// (Requirement 5.5).
    /// </para>
    /// </summary>
    /// <param name="submission">
    /// The persisted <see cref="ContactSubmission"/> whose details are
    /// included in the notification email.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> that completes when the email has been accepted
    /// by the underlying mail provider, or faults if delivery acceptance fails.
    /// </returns>
    Task SendAdminNotificationEmailAsync(ContactSubmission submission);
}
