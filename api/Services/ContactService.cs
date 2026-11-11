using System.Text.RegularExpressions;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Default implementation of <see cref="IContactService"/>.
///
/// Validates every field of a contact submission, persists valid submissions
/// through <see cref="IContactRepository"/> (EF Core <c>SaveChangesAsync</c>,
/// which runs the insert in an implicit transaction — Requirement 5.2), and
/// then dispatches the acknowledgement + admin notification emails
/// (Requirement 5.3).
///
/// <para>
/// Email delivery is best-effort: the submission is durably persisted before
/// any email is attempted, so a provider failure is logged and swallowed rather
/// than surfaced to the client. The submission is already saved, so returning a
/// failure would be misleading and could prompt a duplicate re-submission.
/// </para>
///
/// <para>
/// Registered as a scoped service (shares the request-scoped repository /
/// <see cref="NnmDbContext"/> and email service lifetime).
/// </para>
/// </summary>
public sealed partial class ContactService : IContactService
{
    /// <summary>Full name length limit (Requirement 5.1).</summary>
    public const int MaxFullNameLength = 100;

    /// <summary>Email address length limit — RFC 5321 (Requirement 5.1).</summary>
    public const int MaxEmailLength = 254;

    /// <summary>Subject length limit (Requirement 5.1).</summary>
    public const int MaxSubjectLength = 150;

    /// <summary>Message minimum length (Requirement 5.1).</summary>
    public const int MinMessageLength = 10;

    /// <summary>Message maximum length (Requirement 5.1).</summary>
    public const int MaxMessageLength = 2000;

    private readonly IContactRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ILogger<ContactService> _logger;

    public ContactService(
        IContactRepository repository,
        IEmailService emailService,
        ILogger<ContactService> logger)
    {
        _repository = repository;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// Matches a structurally valid <c>local-part@domain.tld</c> address: a
    /// non-empty local part, a single <c>@</c>, a domain, and a dot-separated
    /// top-level label. No whitespace is permitted anywhere in the address.
    /// This mirrors the client-side rule (Requirement 5.7); fuller RFC 5322
    /// conformance is intentionally not attempted.
    /// </summary>
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailFormat();

    /// <inheritdoc/>
    public async Task<ContactSubmitResult> SubmitAsync(
        ContactSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var errors = Validate(submission);
        if (errors.Count > 0)
        {
            // Nothing is persisted when validation fails (Requirement 5.6, 5.7).
            return ContactSubmitResult.ValidationFailed(errors);
        }

        // Persist first. SaveChangesAsync wraps the insert in a transaction;
        // a failure propagates and is mapped to HTTP 500 upstream — no email
        // is sent for a submission that was never stored (Requirement 5.2).
        var persisted = await _repository.AddAsync(submission, cancellationToken);

        // Emails are best-effort and dispatched only after a successful persist
        // (Requirement 5.3). A send failure must not fail the request.
        await SendNotificationsAsync(persisted);

        return ContactSubmitResult.Accepted();
    }

    /// <summary>
    /// Applies the field-level validation rules and returns a map of invalid
    /// field names (matching the JSON request property names) to their error
    /// messages. An empty map means the submission is valid.
    /// </summary>
    private static Dictionary<string, string[]> Validate(ContactSubmission submission)
    {
        var errors = new Dictionary<string, string[]>();

        // Full Name — required, ≤ 100 chars.
        var fullName = submission.FullName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors["fullName"] = new[] { "The Full Name field is required." };
        }
        else if (fullName.Length > MaxFullNameLength)
        {
            errors["fullName"] = new[]
            {
                $"The Full Name field must not exceed {MaxFullNameLength} characters."
            };
        }

        // Email — required, valid local-part@domain.tld, ≤ 254 chars.
        var email = submission.Email ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = new[] { "The Email field is required." };
        }
        else if (email.Length > MaxEmailLength)
        {
            errors["email"] = new[]
            {
                $"The Email field must not exceed {MaxEmailLength} characters."
            };
        }
        else if (!EmailFormat().IsMatch(email))
        {
            errors["email"] = new[] { "The Email field is not a valid email address." };
        }

        // Subject — required, ≤ 150 chars.
        var subject = submission.Subject ?? string.Empty;
        if (string.IsNullOrWhiteSpace(subject))
        {
            errors["subject"] = new[] { "The Subject field is required." };
        }
        else if (subject.Length > MaxSubjectLength)
        {
            errors["subject"] = new[]
            {
                $"The Subject field must not exceed {MaxSubjectLength} characters."
            };
        }

        // Message — required, 10–2000 chars.
        var message = submission.Message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            errors["message"] = new[] { "The Message field is required." };
        }
        else if (message.Length < MinMessageLength || message.Length > MaxMessageLength)
        {
            errors["message"] = new[]
            {
                $"The Message field must be between {MinMessageLength} and {MaxMessageLength} characters."
            };
        }

        return errors;
    }

    /// <summary>
    /// Sends the acknowledgement and admin notification emails best-effort.
    /// Each send is isolated so one failure does not prevent the other, and no
    /// failure propagates: the submission is already persisted (Requirement 5.3).
    /// </summary>
    private async Task SendNotificationsAsync(ContactSubmission submission)
    {
        try
        {
            await _emailService.SendAcknowledgementEmailAsync(
                submission.Email, submission.FullName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send acknowledgement email for contact submission {SubmissionId}; the submission was persisted.",
                submission.Id);
        }

        try
        {
            await _emailService.SendAdminNotificationEmailAsync(submission);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send admin notification email for contact submission {SubmissionId}; the submission was persisted.",
                submission.Id);
        }
    }
}
