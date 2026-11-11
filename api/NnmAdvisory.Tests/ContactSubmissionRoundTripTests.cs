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
/// Property-based test for contact submission round-trip persistence through
/// the production service path (Task 5.5).
///
/// <para><b>Property 2: Contact Submission Persistence Round-Trip</b></para>
/// <para>
/// For any valid contact form submission (full name 1–100 chars, a structurally
/// valid <c>local-part@domain.tld</c> email ≤ 254 chars, subject 1–150 chars,
/// message 10–2000 chars) submitted through <see cref="ContactService.SubmitAsync"/>,
/// the submission is accepted and persisted. Querying the stored
/// <see cref="ContactSubmission"/> back returns a record whose
/// <c>FullName</c>, <c>Email</c>, <c>Subject</c>, and <c>Message</c> equal the
/// submitted values, and whose <c>SubmittedAt</c> is a UTC timestamp
/// (zero offset) captured within the submit window.
/// </para>
///
/// <para><b>Validates: Requirements 5.2, 11.6</b></para>
///
/// <para>
/// This test complements the DB-layer round-trip test (Task 3.3) by exercising
/// the SAME property through the production path: the real
/// <see cref="ContactService"/> performs validation, the real
/// <see cref="ContactRepository"/> persists over an in-memory
/// <see cref="NnmDbContext"/>, and a no-op <see cref="IEmailService"/> stub
/// stands in for outbound email. The submitted_at stamp is applied by the
/// repository at insert time (Requirement 11.6), so asserting it here verifies
/// the production stamping behaviour rather than a test-set value.
/// </para>
/// </summary>
public sealed class ContactSubmissionRoundTripTests
{
    // Minimum property-test iterations mandated by the design specification.
    private const int Iterations = 100;

    /// <summary>
    /// No-op email service. The contact service dispatches email best-effort
    /// after a successful persist; the round-trip property is only concerned
    /// with persistence, so the stub records nothing and never throws.
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
            .UseInMemoryDatabase($"contact-service-roundtrip-{Guid.NewGuid()}")
            .Options;

        return new NnmDbContext(options);
    }

    // ------------------------------------------------------------------
    // CsCheck generators constrained to the valid contact submission input
    // space (the bounds enforced by the schema and the service layer).
    // ------------------------------------------------------------------

    /// <summary>Full name: non-empty, 1–100 characters.</summary>
    private static readonly Gen<string> GenFullName =
        Gen.Char['A', 'z'].Array[1, 100].Select(c => new string(c));

    /// <summary>
    /// A structurally valid <c>local-part@domain.tld</c> email kept within the
    /// 254-character column limit.
    /// </summary>
    private static readonly Gen<string> GenEmail =
        Gen.Select(
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),  // local part
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),  // domain
            Gen.OneOfConst("com", "net", "org", "co", "io"),             // tld
            (local, domain, tld) => $"{local}@{domain}.{tld}");

    /// <summary>Subject: non-empty, 1–150 characters.</summary>
    private static readonly Gen<string> GenSubject =
        Gen.Char['A', 'z'].Array[1, 150].Select(c => new string(c));

    /// <summary>Message body: 10–2000 characters (schema CHECK constraint).</summary>
    private static readonly Gen<string> GenMessage =
        Gen.Char['A', 'z'].Array[10, 2000].Select(c => new string(c));

    /// <summary>A fully valid contact submission payload.</summary>
    private static readonly Gen<ContactSubmission> GenValidSubmission =
        Gen.Select(GenFullName, GenEmail, GenSubject, GenMessage,
            (fullName, email, subject, message) => new ContactSubmission
            {
                FullName = fullName,
                Email = email,
                Subject = subject,
                Message = message,
            });

    // ------------------------------------------------------------------
    // Property 2 — service-layer round-trip persistence.
    // ------------------------------------------------------------------

    [Fact]
    public void ValidSubmission_RoundTripsThroughService_WithIdenticalFieldsAndUtcTimestamp()
    {
        GenValidSubmission.Sample(generated =>
        {
            using var context = CreateContext();
            var repository = new ContactRepository(context);
            var service = new ContactService(
                repository, new NoOpEmailService(), NullLogger<ContactService>.Instance);

            // Snapshot the submitted field values before handing the entity to
            // the service: the repository mutates SubmittedAt on the same
            // instance, so comparisons must use the captured originals.
            var expectedFullName = generated.FullName;
            var expectedEmail = generated.Email;
            var expectedSubject = generated.Subject;
            var expectedMessage = generated.Message;

            // Bound the window in which the repository stamps submitted_at.
            var beforeSubmit = DateTimeOffset.UtcNow;

            var result = service.SubmitAsync(generated).GetAwaiter().GetResult();

            var afterSubmit = DateTimeOffset.UtcNow;

            // The production path accepted and persisted the submission.
            if (result.Outcome != ContactOutcome.Accepted)
            {
                return false;
            }

            // Read the record back through a clean query (no identity-map reuse)
            // so the assertion reflects persisted state.
            var stored = context.ContactSubmissions
                                .AsNoTracking()
                                .Single();

            // Field values survive the round trip unchanged.
            var fieldsMatch =
                stored.FullName == expectedFullName &&
                stored.Email == expectedEmail &&
                stored.Subject == expectedSubject &&
                stored.Message == expectedMessage;

            // submitted_at is a UTC timestamp (zero offset) captured within the
            // submit window (Requirement 11.6).
            var timestampIsUtc = stored.SubmittedAt.Offset == TimeSpan.Zero;
            var timestampInWindow =
                stored.SubmittedAt >= beforeSubmit &&
                stored.SubmittedAt <= afterSubmit;

            return fieldsMatch && timestampIsUtc && timestampInWindow;
        }, iter: Iterations);
    }
}
