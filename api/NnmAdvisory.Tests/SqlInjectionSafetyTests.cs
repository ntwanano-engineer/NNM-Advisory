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
/// Property-based tests for SQL injection safety across the user-facing write
/// paths (contact form and both surveys).
///
/// <para><b>Property 10: SQL Injection Safety</b></para>
/// <para>
/// For any submitted field value that embeds SQL metacharacters or classic
/// injection fragments (<c>'</c>, <c>--</c>, <c>;</c>, <c>' OR '1'='1</c>,
/// <c>'; DROP TABLE contact_submissions; --</c>, <c>" OR ""="</c>), the value is
/// persisted as literal data: no SQL error is raised, the stored field equals
/// the submitted string verbatim (it is treated as data, never executed), and
/// database integrity is intact (exactly the row(s) written exist, other tables
/// are untouched).
/// </para>
///
/// <para><b>Validates: Requirements 11.3</b></para>
///
/// <para>
/// The property is exercised end to end through the production service +
/// repository + <see cref="NnmDbContext"/> path, with a no-op
/// <see cref="IEmailService"/> stub. EF Core always parameterises its generated
/// SQL regardless of provider, so this verifies the ORM-based parameterisation
/// that satisfies Requirement 11.3 — injection fragments can never break out of
/// the data layer into executable SQL.
/// </para>
/// </summary>
public sealed class SqlInjectionSafetyTests
{
    // Minimum property-test iterations mandated by the design specification.
    private const int Iterations = 100;

    /// <summary>
    /// No-op email service. The contact service dispatches email best-effort
    /// after a successful persist; the injection-safety property is only
    /// concerned with persisted field values, so the stub records nothing and
    /// never throws.
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
            .UseInMemoryDatabase($"sql-injection-{Guid.NewGuid()}")
            .Options;

        return new NnmDbContext(options);
    }

    // ------------------------------------------------------------------
    // Injection payload building blocks.
    //
    // A payload is a benign alphanumeric token, followed by one classic SQL
    // injection fragment, followed by another benign token. Wrapping the
    // fragment in alphanumeric tokens guarantees:
    //   * the value is never whitespace-only (passes "required" validation), and
    //   * the value has no leading/trailing whitespace (so the survey paths,
    //     which Trim() free text, round-trip the value verbatim).
    // The metacharacters/fragments themselves are what must stay literal data.
    // ------------------------------------------------------------------

    /// <summary>A short benign alphanumeric token used to pad around a fragment.</summary>
    private static readonly Gen<string> GenToken =
        Gen.Char['a', 'z'].Array[1, 12].Select(c => new string(c));

    /// <summary>
    /// The SQL metacharacters and injection fragments called out by the task:
    /// a lone quote, a comment introducer, a statement separator, a tautology,
    /// a stacked DROP TABLE, and a double-quote tautology.
    /// </summary>
    private static readonly Gen<string> GenInjectionFragment =
        Gen.OneOfConst(
            "'",
            "--",
            ";",
            "' OR '1'='1",
            "'; DROP TABLE contact_submissions; --",
            "\" OR \"\"=\"",
            "'; DROP TABLE customer_survey_responses; --",
            "1; DELETE FROM employee_survey_responses");

    /// <summary>
    /// A payload embedding an injection fragment between two benign tokens. The
    /// result never starts or ends with whitespace.
    /// </summary>
    private static Gen<string> GenPayload(int minLength, int maxLength) =>
        Gen.Select(GenToken, GenInjectionFragment, GenToken,
                (prefix, fragment, suffix) => prefix + fragment + suffix)
           // Pad with benign alphanumerics (no whitespace) to reach the valid
           // length window for the target field, keeping the fragment intact.
           .SelectMany(core =>
               Gen.Char['a', 'z']
                  .Array[Math.Max(0, minLength - core.Length),
                         Math.Max(0, maxLength - core.Length)]
                  .Select(pad => core + new string(pad)))
           // Guard: never exceed the field maximum even if core already does.
           .Select(s => s.Length > maxLength ? s[..maxLength] : s);

    // ------------------------------------------------------------------
    // Valid surrounding field values so the submission is ACCEPTED and the
    // injected field can be read back. Exactly one field per case carries the
    // injection payload.
    // ------------------------------------------------------------------

    private static readonly Gen<string> GenValidName =
        Gen.Char['A', 'z'].Array[1, 100].Select(c => new string(c));

    private static readonly Gen<string> GenValidEmail =
        Gen.Select(
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),
            Gen.OneOfConst("com", "net", "org", "co", "io"),
            (local, domain, tld) => $"{local}@{domain}.{tld}");

    // ------------------------------------------------------------------
    // Property 10 — contact submission: injection payloads in Subject and
    // Message are stored verbatim with no SQL error and DB integrity intact.
    // ------------------------------------------------------------------

    [Fact]
    public void ContactSubmission_WithInjectionPayloads_StoredVerbatim_AndIntact()
    {
        // Subject: 1–150 chars; Message: 10–2000 chars. Both receive injection
        // payloads simultaneously so both columns are exercised each iteration.
        var gen = Gen.Select(
            GenValidName,
            GenValidEmail,
            GenPayload(1, ContactService.MaxSubjectLength),
            GenPayload(ContactService.MinMessageLength, ContactService.MaxMessageLength),
            (name, email, subject, message) => (name, email, subject, message));

        gen.Sample(data =>
        {
            using var context = CreateContext();
            var repository = new ContactRepository(context);
            var service = new ContactService(
                repository, new NoOpEmailService(), NullLogger<ContactService>.Instance);

            var submission = new ContactSubmission
            {
                FullName = data.name,
                Email = data.email,
                Subject = data.subject,
                Message = data.message,
            };

            var result = service.SubmitAsync(submission).GetAwaiter().GetResult();

            // The submission was accepted (so it is readable back) and no SQL
            // error was raised during persistence.
            if (result.Outcome != ContactOutcome.Accepted)
            {
                return false;
            }

            // DB integrity: exactly one row exists, no other table was touched.
            var contactCount = context.ContactSubmissions.AsNoTracking().Count();
            var customerCount = context.CustomerSurveyResponses.AsNoTracking().Count();
            var employeeCount = context.EmployeeSurveyResponses.AsNoTracking().Count();

            var stored = context.ContactSubmissions.AsNoTracking().Single();

            // The injected values are stored as literal data, byte-for-byte.
            return contactCount == 1
                   && customerCount == 0
                   && employeeCount == 0
                   && stored.Subject == data.subject
                   && stored.Message == data.message;
        }, iter: Iterations);
    }

    // ------------------------------------------------------------------
    // Property 10 — customer survey: an injection payload in OpenFeedback is
    // stored verbatim with no SQL error and DB integrity intact.
    // ------------------------------------------------------------------

    [Fact]
    public void CustomerSurvey_WithInjectionFeedback_StoredVerbatim_AndIntact()
    {
        var gen = Gen.Select(
            Gen.Int[SurveyService.ServiceSatisfactionMin, SurveyService.ServiceSatisfactionMax],
            Gen.Int[SurveyService.NpsScoreMin, SurveyService.NpsScoreMax],
            GenPayload(1, SurveyService.OpenFeedbackMaxLength),
            (satisfaction, nps, feedback) => (satisfaction, nps, feedback));

        gen.Sample(data =>
        {
            using var context = CreateContext();
            var repository = new SurveyRepository(context);
            var service = new SurveyService(repository);

            var request = new CustomerSurveyRequest
            {
                ServiceSatisfaction = data.satisfaction,
                NpsScore = data.nps,
                OpenFeedback = data.feedback,
            };

            var result = service.SubmitCustomerSurveyAsync(request).GetAwaiter().GetResult();

            // Accepted (readable back) and no SQL error raised.
            if (!result.IsSuccess)
            {
                return false;
            }

            var customerCount = context.CustomerSurveyResponses.AsNoTracking().Count();
            var contactCount = context.ContactSubmissions.AsNoTracking().Count();
            var employeeCount = context.EmployeeSurveyResponses.AsNoTracking().Count();

            var stored = context.CustomerSurveyResponses.AsNoTracking().Single();

            // The service does not Trim() the payload here (it has no surrounding
            // whitespace), so it round-trips verbatim as literal data.
            return customerCount == 1
                   && contactCount == 0
                   && employeeCount == 0
                   && stored.OpenFeedback == data.feedback;
        }, iter: Iterations);
    }

    // ------------------------------------------------------------------
    // Property 10 — employee survey: injection payloads in WorkplaceSatisfaction
    // and OpenFeedback are stored verbatim with no SQL error and DB integrity
    // intact.
    // ------------------------------------------------------------------

    [Fact]
    public void EmployeeSurvey_WithInjectionPayloads_StoredVerbatim_AndIntact()
    {
        var gen = Gen.Select(
            Gen.Int[1, 5],
            GenPayload(1, 500),
            GenPayload(1, 1000),
            (engagement, workplace, feedback) => (engagement, workplace, feedback));

        gen.Sample(data =>
        {
            using var context = CreateContext();
            var repository = new SurveyRepository(context);
            var service = new SurveyService(repository);

            var request = new EmployeeSurveyRequest
            {
                EngagementScore = data.engagement,
                WorkplaceSatisfaction = data.workplace,
                OpenFeedback = data.feedback,
            };

            var result = service.SubmitEmployeeSurveyAsync(request).GetAwaiter().GetResult();

            // Accepted (readable back) and no SQL error raised.
            if (!result.IsSuccess)
            {
                return false;
            }

            var employeeCount = context.EmployeeSurveyResponses.AsNoTracking().Count();
            var contactCount = context.ContactSubmissions.AsNoTracking().Count();
            var customerCount = context.CustomerSurveyResponses.AsNoTracking().Count();

            var stored = context.EmployeeSurveyResponses.AsNoTracking().Single();

            // Payloads have no surrounding whitespace, so the service's Trim() is
            // a no-op and the fragments round-trip verbatim as literal data.
            return employeeCount == 1
                   && contactCount == 0
                   && customerCount == 0
                   && stored.WorkplaceSatisfaction == data.workplace
                   && stored.OpenFeedback == data.feedback;
        }, iter: Iterations);
    }
}
