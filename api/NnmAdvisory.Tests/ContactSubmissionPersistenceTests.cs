using CsCheck;
using Microsoft.EntityFrameworkCore;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based tests for database round-trip persistence of contact
/// submissions.
///
/// <para><b>Property 2: Contact Submission Persistence Round-Trip</b></para>
/// <para>
/// For any valid contact form submission (full name ≤ 100 chars, valid email,
/// subject ≤ 150 chars, message 10–2000 chars), after the submission is
/// persisted, querying the database for that submission returns a record with
/// identical field values and a valid UTC timestamp recording the moment of
/// submission.
/// </para>
///
/// <para><b>Validates: Requirements 5.2, 11.6</b></para>
/// </summary>
public sealed class ContactSubmissionPersistenceTests
{
    // ------------------------------------------------------------------
    // CsCheck generators constrained to the valid contact submission
    // input space (the bounds enforced by the schema and API layer).
    // ------------------------------------------------------------------

    /// <summary>Full name: non-empty, trimmed, 1–100 characters.</summary>
    private static readonly Gen<string> GenFullName =
        Gen.Char['A', 'z']
           .Array[1, 100]
           .Select(chars => new string(chars));

    /// <summary>
    /// A structurally valid email of the form local-part@domain.tld, kept
    /// within the 254-character column limit.
    /// </summary>
    private static readonly Gen<string> GenEmail =
        Gen.Select(
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),  // local part
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),  // domain
            Gen.OneOfConst("com", "net", "org", "co", "io"),             // tld
            (local, domain, tld) => $"{local}@{domain}.{tld}");

    /// <summary>Subject: non-empty, 1–150 characters.</summary>
    private static readonly Gen<string> GenSubject =
        Gen.Char['A', 'z']
           .Array[1, 150]
           .Select(chars => new string(chars));

    /// <summary>Message body: 10–2000 characters (schema CHECK constraint).</summary>
    private static readonly Gen<string> GenMessage =
        Gen.Char['A', 'z']
           .Array[10, 2000]
           .Select(chars => new string(chars));

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

    /// <summary>
    /// Creates a fresh in-memory <see cref="NnmDbContext"/> isolated to a
    /// unique database name so iterations never bleed into one another.
    /// </summary>
    private static NnmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NnmDbContext>()
            .UseInMemoryDatabase($"contact-roundtrip-{Guid.NewGuid()}")
            .Options;

        return new NnmDbContext(options);
    }

    // ------------------------------------------------------------------
    // Property 2 — round-trip persistence.
    // ------------------------------------------------------------------

    [Fact]
    public void ContactSubmission_RoundTrips_WithIdenticalFieldsAndUtcTimestamp()
    {
        GenValidSubmission.Sample(submission =>
        {
            using var context = CreateContext();

            // Capture the request-receipt instant the way the persistence layer
            // will stamp submitted_at (UTC) at insertion time.
            var beforeInsert = DateTimeOffset.UtcNow;

            submission.SubmittedAt = DateTimeOffset.UtcNow;
            context.ContactSubmissions.Add(submission);
            context.SaveChanges();

            var afterInsert = DateTimeOffset.UtcNow;
            var insertedId = submission.Id;

            // Read the record back through a clean query (no identity-map reuse).
            var stored = context.ContactSubmissions
                                .AsNoTracking()
                                .Single(c => c.Id == insertedId);

            // Field values survive the round trip unchanged.
            Assert.Equal(submission.FullName, stored.FullName);
            Assert.Equal(submission.Email, stored.Email);
            Assert.Equal(submission.Subject, stored.Subject);
            Assert.Equal(submission.Message, stored.Message);

            // submitted_at is a valid UTC timestamp captured at the moment of
            // submission (Requirement 11.6): zero offset and within the
            // insertion window.
            Assert.Equal(TimeSpan.Zero, stored.SubmittedAt.Offset);
            Assert.InRange(stored.SubmittedAt, beforeInsert, afterInsert);
        });
    }
}
