using CsCheck;
using Microsoft.EntityFrameworkCore;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based tests for customer survey score validation.
///
/// <para><b>Property 6: Survey Validation Rejects Incomplete Submissions</b></para>
/// <para>
/// For any customer survey submission in which a required score is missing
/// (null) or falls outside its valid range (service satisfaction ∉ [1, 5],
/// NPS ∉ [0, 10]), or whose open feedback exceeds 1000 characters, the service
/// rejects the submission (<see cref="SurveySubmissionResult.IsSuccess"/> is
/// <c>false</c>, which the controller maps to HTTP 422) and persists nothing —
/// the <c>customer_survey_responses</c> row count is unchanged.
/// </para>
///
/// <para><b>Validates: Requirements 6.4, 7.4</b></para>
///
/// <para>
/// The test wires the real <see cref="SurveyService"/> to the real
/// <see cref="SurveyRepository"/> over an in-memory <see cref="NnmDbContext"/>
/// so the no-persistence guarantee is exercised end to end, not mocked.
/// </para>
/// </summary>
public sealed class SurveyScoreValidationTests
{
    // ------------------------------------------------------------------
    // Generators that deliberately produce invalid customer survey
    // requests. Each sampled request violates at least one rule so the
    // service must reject it.
    // ------------------------------------------------------------------

    /// <summary>
    /// A service-satisfaction value that is null or outside [1, 5].
    /// Covers "missing required score" and both out-of-range directions.
    /// </summary>
    private static readonly Gen<int?> GenInvalidSatisfaction =
        Gen.Frequency(
            (1, Gen.Const((int?)null)),
            (2, Gen.Int[int.MinValue, SurveyService.ServiceSatisfactionMin - 1]
                    .Select(v => (int?)v)),
            (2, Gen.Int[SurveyService.ServiceSatisfactionMax + 1, int.MaxValue]
                    .Select(v => (int?)v)));

    /// <summary>
    /// An NPS value that is null or outside [0, 10].
    /// </summary>
    private static readonly Gen<int?> GenInvalidNps =
        Gen.Frequency(
            (1, Gen.Const((int?)null)),
            (2, Gen.Int[int.MinValue, SurveyService.NpsScoreMin - 1]
                    .Select(v => (int?)v)),
            (2, Gen.Int[SurveyService.NpsScoreMax + 1, int.MaxValue]
                    .Select(v => (int?)v)));

    /// <summary>A score that happens to be valid, used to isolate a single bad field.</summary>
    private static readonly Gen<int?> GenValidSatisfaction =
        Gen.Int[SurveyService.ServiceSatisfactionMin, SurveyService.ServiceSatisfactionMax]
           .Select(v => (int?)v);

    /// <summary>A valid NPS score, used to isolate a single bad field.</summary>
    private static readonly Gen<int?> GenValidNps =
        Gen.Int[SurveyService.NpsScoreMin, SurveyService.NpsScoreMax]
           .Select(v => (int?)v);

    /// <summary>Open feedback that exceeds the 1000-character limit.</summary>
    private static readonly Gen<string> GenOversizedFeedback =
        Gen.Char['a', 'z']
           .Array[SurveyService.OpenFeedbackMaxLength + 1, SurveyService.OpenFeedbackMaxLength + 500]
           .Select(chars => new string(chars));

    /// <summary>
    /// An invalid customer survey request. Three disjoint shapes, each of which
    /// must be rejected:
    ///   1. both scores invalid (null / out of range),
    ///   2. only service satisfaction invalid (NPS valid),
    ///   3. only NPS invalid (satisfaction valid),
    ///   4. both scores valid but open feedback too long.
    /// </summary>
    private static readonly Gen<CustomerSurveyRequest> GenInvalidRequest =
        Gen.OneOf(
            // Both scores bad.
            Gen.Select(GenInvalidSatisfaction, GenInvalidNps,
                (sat, nps) => new CustomerSurveyRequest
                {
                    ServiceSatisfaction = sat,
                    NpsScore = nps,
                }),
            // Only satisfaction bad.
            Gen.Select(GenInvalidSatisfaction, GenValidNps,
                (sat, nps) => new CustomerSurveyRequest
                {
                    ServiceSatisfaction = sat,
                    NpsScore = nps,
                }),
            // Only NPS bad.
            Gen.Select(GenValidSatisfaction, GenInvalidNps,
                (sat, nps) => new CustomerSurveyRequest
                {
                    ServiceSatisfaction = sat,
                    NpsScore = nps,
                }),
            // Scores valid, feedback too long.
            Gen.Select(GenValidSatisfaction, GenValidNps, GenOversizedFeedback,
                (sat, nps, feedback) => new CustomerSurveyRequest
                {
                    ServiceSatisfaction = sat,
                    NpsScore = nps,
                    OpenFeedback = feedback,
                }));

    /// <summary>
    /// Creates a fresh in-memory <see cref="NnmDbContext"/> isolated to a unique
    /// database name so iterations never bleed into one another.
    /// </summary>
    private static NnmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NnmDbContext>()
            .UseInMemoryDatabase($"survey-validation-{Guid.NewGuid()}")
            .Options;

        return new NnmDbContext(options);
    }

    // ------------------------------------------------------------------
    // Property 6 — invalid submissions are rejected and nothing is stored.
    // ------------------------------------------------------------------

    [Fact]
    public void InvalidCustomerSurvey_IsRejected_AndNotPersisted()
    {
        GenInvalidRequest.Sample(request =>
        {
            using var context = CreateContext();
            var repository = new SurveyRepository(context);
            var service = new SurveyService(repository);

            var before = context.CustomerSurveyResponses.AsNoTracking().Count();

            var result = service.SubmitCustomerSurveyAsync(request).GetAwaiter().GetResult();

            var after = context.CustomerSurveyResponses.AsNoTracking().Count();

            // Rejected: maps to HTTP 422 at the controller boundary.
            Assert.False(result.IsSuccess);
            Assert.NotEmpty(result.Errors);

            // No persistence: the survey row count is unchanged.
            Assert.Equal(before, after);
        },
        iter: 100);
    }
}
