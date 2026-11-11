using CsCheck;
using Microsoft.EntityFrameworkCore;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based tests for the survey submission timestamp invariant.
///
/// <para><b>Property 7: Survey Timestamp Invariant</b></para>
/// <para>
/// For any valid survey submission (customer or employee), the persistence
/// layer stamps <c>submitted_at</c> with the UTC instant at which the API
/// receives the request. The stored timestamp is therefore a UTC value (zero
/// offset) that falls within the interval bounded by the instant captured just
/// before the submit call and the instant captured just after it — an interval
/// that is, by construction, well inside the 5-second window of API receipt.
/// </para>
///
/// <para><b>Validates: Requirements 6.3, 7.3, 11.6</b></para>
///
/// <para>
/// The tests exercise the real <see cref="SurveyService"/> on top of the real
/// <see cref="SurveyRepository"/> and an in-memory <see cref="NnmDbContext"/>
/// (the same persistence pattern used by
/// <see cref="ContactSubmissionPersistenceTests"/>), so the timestamp is
/// produced by production code rather than by the test.
/// </para>
/// </summary>
public sealed class SurveyTimestampInvariantTests
{
    /// <summary>The acceptance window: the stored timestamp must be within
    /// 5 seconds of API receipt (Requirements 6.3, 7.3).</summary>
    private static readonly TimeSpan ReceiptWindow = TimeSpan.FromSeconds(5);

    // ------------------------------------------------------------------
    // Generators constrained to the VALID survey input space.
    // ------------------------------------------------------------------

    /// <summary>Optional free text within the 1000-character bound, or null.</summary>
    private static readonly Gen<string?> GenOptionalFeedback =
        Gen.OneOf(
            Gen.Const((string?)null),
            Gen.Char['A', 'z'].Array[0, 1000].Select(c => (string?)new string(c)));

    /// <summary>A fully valid customer survey payload (scores in range,
    /// feedback ≤ 1000 chars).</summary>
    private static readonly Gen<CustomerSurveyRequest> GenValidCustomerRequest =
        Gen.Select(
            Gen.Int[SurveyService.ServiceSatisfactionMin, SurveyService.ServiceSatisfactionMax],
            Gen.Int[SurveyService.NpsScoreMin, SurveyService.NpsScoreMax],
            GenOptionalFeedback,
            (satisfaction, nps, feedback) => new CustomerSurveyRequest
            {
                ServiceSatisfaction = satisfaction,
                NpsScore = nps,
                OpenFeedback = feedback,
            });

    /// <summary>Required non-whitespace text, 1–500 characters.</summary>
    private static readonly Gen<string> GenRequiredText =
        Gen.Char['A', 'z'].Array[1, 500].Select(c => new string(c));

    /// <summary>A fully valid employee survey payload (all required fields
    /// present and non-whitespace).</summary>
    private static readonly Gen<EmployeeSurveyRequest> GenValidEmployeeRequest =
        Gen.Select(
            Gen.Int[0, 10],
            GenRequiredText,
            GenRequiredText,
            (engagement, workplace, feedback) => new EmployeeSurveyRequest
            {
                EngagementScore = engagement,
                WorkplaceSatisfaction = workplace,
                OpenFeedback = feedback,
            });

    /// <summary>
    /// Creates a fresh in-memory <see cref="NnmDbContext"/> isolated to a unique
    /// database name so iterations never bleed into one another.
    /// </summary>
    private static NnmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NnmDbContext>()
            .UseInMemoryDatabase($"survey-timestamp-{Guid.NewGuid()}")
            .Options;

        return new NnmDbContext(options);
    }

    // ------------------------------------------------------------------
    // Property 7 — customer survey timestamp invariant.
    // ------------------------------------------------------------------

    [Fact]
    public void CustomerSurvey_StampsUtcSubmittedAt_WithinReceiptWindow()
    {
        GenValidCustomerRequest.Sample(request =>
        {
            using var context = CreateContext();
            var service = new SurveyService(new SurveyRepository(context));

            // Bracket the submit call to capture the API-receipt window.
            var before = DateTimeOffset.UtcNow;
            var result = service.SubmitCustomerSurveyAsync(request).GetAwaiter().GetResult();
            var after = DateTimeOffset.UtcNow;

            Assert.True(result.IsSuccess);

            var stored = context.CustomerSurveyResponses
                                .AsNoTracking()
                                .Single();

            AssertTimestampWithinReceiptWindow(stored.SubmittedAt, before, after);
        },
        iter: 100);
    }

    // ------------------------------------------------------------------
    // Property 7 — employee survey timestamp invariant.
    // ------------------------------------------------------------------

    [Fact]
    public void EmployeeSurvey_StampsUtcSubmittedAt_WithinReceiptWindow()
    {
        GenValidEmployeeRequest.Sample(request =>
        {
            using var context = CreateContext();
            var service = new SurveyService(new SurveyRepository(context));

            var before = DateTimeOffset.UtcNow;
            var result = service.SubmitEmployeeSurveyAsync(request).GetAwaiter().GetResult();
            var after = DateTimeOffset.UtcNow;

            Assert.True(result.IsSuccess);

            var stored = context.EmployeeSurveyResponses
                                .AsNoTracking()
                                .Single();

            AssertTimestampWithinReceiptWindow(stored.SubmittedAt, before, after);
        },
        iter: 100);
    }

    /// <summary>
    /// Asserts the stored <paramref name="submittedAt"/> is a UTC timestamp
    /// (zero offset), falls inside the [<paramref name="before"/>,
    /// <paramref name="after"/>] bracket around the submit call, and is within
    /// the 5-second window of API receipt (Requirements 6.3, 7.3, 11.6).
    /// </summary>
    private static void AssertTimestampWithinReceiptWindow(
        DateTimeOffset submittedAt,
        DateTimeOffset before,
        DateTimeOffset after)
    {
        // Stored as a UTC instant.
        Assert.Equal(TimeSpan.Zero, submittedAt.Offset);

        // Within the tight bracket captured around the submit call.
        Assert.InRange(submittedAt, before, after);

        // And therefore well within the 5-second window of API receipt.
        Assert.True(
            submittedAt - before <= ReceiptWindow,
            $"submitted_at was {submittedAt - before} after receipt, exceeding the {ReceiptWindow} window.");
    }
}
