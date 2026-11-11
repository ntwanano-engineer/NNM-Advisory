using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Discriminates the possible outcomes of a contact form submission attempt.
/// The controller (Task 5.3) maps each outcome to an HTTP status code:
/// <list type="bullet">
///   <item><see cref="ContactOutcome.Accepted"/> → HTTP 202 Accepted.</item>
///   <item><see cref="ContactOutcome.ValidationFailed"/> → HTTP 422 Unprocessable Entity.</item>
/// </list>
/// Database / infrastructure failures are surfaced as exceptions and mapped to
/// HTTP 500 by the controller / global exception handler.
/// </summary>
public enum ContactOutcome
{
    /// <summary>
    /// The submission passed field-level validation and was persisted.
    /// Acknowledgement + admin notification emails are dispatched best-effort.
    /// </summary>
    Accepted,

    /// <summary>
    /// One or more fields failed validation; <see cref="ContactSubmitResult.Errors"/>
    /// identifies each invalid field by name (Requirement 5.6, 5.7, 10.4). The
    /// submission was NOT persisted.
    /// </summary>
    ValidationFailed
}

/// <summary>
/// The result of a contact form submission attempt.
///
/// <list type="bullet">
///   <item>
///     <see cref="ContactOutcome.Accepted"/> — the submission was persisted and
///     <see cref="Errors"/> is empty.
///   </item>
///   <item>
///     <see cref="ContactOutcome.ValidationFailed"/> — nothing was persisted and
///     <see cref="Errors"/> maps each invalid field name to its error messages,
///     ready to serialise as the <c>{ "errors": { ... } }</c> body (HTTP 422).
///   </item>
/// </list>
/// </summary>
public sealed record ContactSubmitResult(
    ContactOutcome Outcome,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    /// <summary>The submission was accepted and persisted (HTTP 202).</summary>
    public static ContactSubmitResult Accepted() =>
        new(ContactOutcome.Accepted, NoErrors);

    /// <summary>
    /// The submission failed validation (HTTP 422). <paramref name="errors"/>
    /// identifies each invalid field by name.
    /// </summary>
    public static ContactSubmitResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        new(ContactOutcome.ValidationFailed, errors);
}

/// <summary>
/// Business-logic contract for processing contact form submissions.
///
/// Responsibilities (Requirements 5.2, 5.3, 5.6, 5.7):
/// <list type="bullet">
///   <item>
///     Validate every field: full name (required, ≤ 100 chars), email (required,
///     <c>local-part@domain.tld</c>, ≤ 254 chars), subject (required, ≤ 150 chars),
///     message (required, 10–2000 chars). On failure, return a per-field error map
///     the controller maps to HTTP 422 (Requirement 5.6, 5.7).
///   </item>
///   <item>
///     On success, persist the submission via <see cref="Repositories.IContactRepository"/>
///     (EF Core <c>SaveChangesAsync</c> = transaction, Requirement 5.2).
///   </item>
///   <item>
///     After a successful persist, trigger the acknowledgement email to the client
///     (Requirement 5.3) and the admin notification email. Email delivery is
///     best-effort: a send failure is logged but does not fail the request because
///     the submission is already durably stored.
///   </item>
/// </list>
/// </summary>
public interface IContactService
{
    /// <summary>
    /// Validates and, if valid, persists a contact form submission, then
    /// dispatches the acknowledgement and admin notification emails.
    /// </summary>
    /// <param name="submission">
    /// The submission to process. Field values are validated before any
    /// persistence occurs.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A <see cref="ContactSubmitResult"/> describing the outcome: accepted
    /// (persisted) or validation-failed (with per-field errors).
    /// </returns>
    Task<ContactSubmitResult> SubmitAsync(
        ContactSubmission submission,
        CancellationToken cancellationToken = default);
}
