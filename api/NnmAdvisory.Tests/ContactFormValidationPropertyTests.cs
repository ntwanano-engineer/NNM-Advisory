using CsCheck;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based test for contact form validation of structurally invalid
/// input (Task 5.4).
///
/// <para><b>Property 1: Contact Form Validation Rejects Structurally Invalid Inputs</b></para>
/// <para>
/// For any contact form submission that violates at least one field-level rule
/// — an empty required field, a full name &gt; 100 chars, a subject &gt; 150
/// chars, a message outside the 10–2000 char range, or a malformed email (no
/// <c>@</c>, no domain, no TLD, or embedded whitespace) — the submission is
/// rejected with <see cref="ContactOutcome.ValidationFailed"/> (which the
/// controller maps to HTTP 422) and no <c>contact_submissions</c> row is
/// persisted. The database row count is unchanged.
/// </para>
///
/// <para><b>Validates: Requirements 5.6, 5.7, 10.4</b></para>
///
/// <para>
/// The test exercises the real <see cref="ContactService"/> wired to the real
/// <see cref="ContactRepository"/> over an in-memory <see cref="NnmDbContext"/>,
/// with a no-op <see cref="IEmailService"/> stub. This keeps the validation +
/// persistence path end-to-end real so the row-count invariant is meaningful.
/// </para>
/// </summary>
public sealed class ContactFormValidationPropertyTests
{
    // Minimum property-test iterations mandated by the design specification.
    private const int Iterations = 100;

    /// <summary>
    /// No-op email service. The contact service only dispatches email after a
    /// successful persist; for invalid input it is never invoked, but a stub is
    /// still required to construct the service.
    /// </summary>
    private sealed class NoOpEmailService : IEmailService
    {
        public Task SendAcknowledgementEmailAsync(string toEmail, string clientName) =>
            Task.CompletedTask;

        public Task SendAdminNotificationEmailAsync(ContactSubmission submission) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Creates a fresh in-memory <see cref="NnmDbContext"/> isolated to a unique
    /// database name so iterations never bleed into one another.
    /// </summary>
    private static NnmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NnmDbContext>()
            .UseInMemoryDatabase($"contact-validation-{Guid.NewGuid()}")
            .Options;

        return new NnmDbContext(options);
    }

    // ------------------------------------------------------------------
    // Building blocks for valid field values. An invalid submission is a
    // valid one with exactly one field replaced by an invalid value, so
    // that the rejection is attributable to the injected violation.
    // ------------------------------------------------------------------

    private static readonly Gen<string> GenValidName =
        Gen.Char['A', 'z'].Array[1, 100].Select(c => new string(c));

    private static readonly Gen<string> GenValidEmail =
        Gen.Select(
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),
            Gen.OneOfConst("com", "net", "org", "co", "io"),
            (local, domain, tld) => $"{local}@{domain}.{tld}");

    private static readonly Gen<string> GenValidSubject =
        Gen.Char['A', 'z'].Array[1, 150].Select(c => new string(c));

    private static readonly Gen<string> GenValidMessage =
        Gen.Char['A', 'z'].Array[10, 2000].Select(c => new string(c));

    // ------------------------------------------------------------------
    // Invalid field generators — each produces a value that must trigger a
    // validation error for its field.
    // ------------------------------------------------------------------

    /// <summary>Empty / whitespace-only — fails the "required" rule.</summary>
    private static readonly Gen<string> GenBlank =
        Gen.OneOfConst("", " ", "   ", "\t", "\n", "  \t ");

    /// <summary>Full name exceeding the 100-char limit.</summary>
    private static readonly Gen<string> GenNameTooLong =
        Gen.Int[101, 400].Select(n => new string('a', n));

    /// <summary>Subject exceeding the 150-char limit.</summary>
    private static readonly Gen<string> GenSubjectTooLong =
        Gen.Int[151, 500].Select(n => new string('s', n));

    /// <summary>Message shorter than 10 or longer than 2000 characters.</summary>
    private static readonly Gen<string> GenMessageOutOfBounds =
        Gen.OneOf(
            Gen.Int[1, 9].Select(n => new string('m', n)),
            Gen.Int[2001, 2500].Select(n => new string('m', n)));

    /// <summary>
    /// Structurally malformed emails: no <c>@</c>, missing domain or TLD,
    /// empty local part, or embedded whitespace.
    /// </summary>
    private static readonly Gen<string> GenMalformedEmail =
        Gen.OneOf(
            // No @ at all.
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),
            // No TLD (no dot after the domain).
            Gen.Select(
                Gen.Char['a', 'z'].Array[1, 15].Select(c => new string(c)),
                Gen.Char['a', 'z'].Array[1, 15].Select(c => new string(c)),
                (local, domain) => $"{local}@{domain}"),
            // Empty local part.
            Gen.Select(
                Gen.Char['a', 'z'].Array[1, 15].Select(c => new string(c)),
                Gen.OneOfConst("com", "net", "org"),
                (domain, tld) => $"@{domain}.{tld}"),
            // Embedded whitespace in an otherwise well-formed address.
            Gen.Select(
                Gen.Char['a', 'z'].Array[1, 10].Select(c => new string(c)),
                Gen.Char['a', 'z'].Array[1, 10].Select(c => new string(c)),
                (local, domain) => $"{local} space@{domain}.com"));

    /// <summary>A fully valid base submission; exactly one field is later corrupted.</summary>
    private static readonly Gen<ContactSubmission> GenValidBase =
        Gen.Select(GenValidName, GenValidEmail, GenValidSubject, GenValidMessage,
            (name, email, subject, message) => new ContactSubmission
            {
                FullName = name,
                Email = email,
                Subject = subject,
                Message = message,
            });

    /// <summary>
    /// A single injected field violation: the field to corrupt (0–6) paired with
    /// the invalid value to assign to it. Combining the seven invalid-value
    /// generators through <see cref="Gen.OneOf"/> ensures every invalid class —
    /// blank required fields, over-length name/subject, out-of-range message, and
    /// malformed email — is exercised across iterations.
    /// </summary>
    private static readonly Gen<(int Field, string Value)> GenViolation =
        Gen.OneOf(
            GenBlank.Select(v => (0, v)),             // full name required
            GenNameTooLong.Select(v => (1, v)),       // full name > 100
            GenBlank.Select(v => (2, v)),             // email required
            GenMalformedEmail.Select(v => (3, v)),    // malformed email
            GenBlank.Select(v => (4, v)),             // subject required
            GenSubjectTooLong.Select(v => (5, v)),    // subject > 150
            GenMessageOutOfBounds.Select(v => (6, v)) // message out of 10–2000
        );

    /// <summary>
    /// A submission that is valid in every field except exactly one, which is
    /// replaced by a structurally invalid value.
    /// </summary>
    private static readonly Gen<ContactSubmission> GenInvalidSubmission =
        Gen.Select(GenValidBase, GenViolation, (submission, violation) =>
        {
            switch (violation.Field)
            {
                case 0: submission.FullName = violation.Value; break;
                case 1: submission.FullName = violation.Value; break;
                case 2: submission.Email = violation.Value; break;
                case 3: submission.Email = violation.Value; break;
                case 4: submission.Subject = violation.Value; break;
                case 5: submission.Subject = violation.Value; break;
                default: submission.Message = violation.Value; break;
            }

            return submission;
        });

    // ------------------------------------------------------------------
    // Property 1 — structurally invalid input is rejected (HTTP 422) and
    // nothing is persisted.
    // ------------------------------------------------------------------

    [Fact]
    public void InvalidSubmission_IsRejected_AndPersistsNoRow()
    {
        GenInvalidSubmission.Sample(submission =>
        {
            using var context = CreateContext();
            var repository = new ContactRepository(context);
            var service = new ContactService(
                repository, new NoOpEmailService(), NullLogger<ContactService>.Instance);

            var beforeCount = context.ContactSubmissions.Count();

            var result = service.SubmitAsync(submission).GetAwaiter().GetResult();

            var afterCount = context.ContactSubmissions
                                    .AsNoTracking()
                                    .Count();

            // Rejected with the validation outcome the controller maps to HTTP 422.
            return result.Outcome == ContactOutcome.ValidationFailed
                   && result.Errors.Count > 0
                   // Row count is unchanged: no submission was persisted.
                   && afterCount == beforeCount
                   && afterCount == 0;
        }, iter: Iterations);
    }
}
