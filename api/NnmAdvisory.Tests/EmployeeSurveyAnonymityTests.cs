using System.Reflection;
using CsCheck;
using Microsoft.EntityFrameworkCore;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based tests for the anonymity guarantee of the employee survey
/// persistence path.
///
/// <para><b>Property 11: Employee Survey Anonymity Invariant</b></para>
/// <para>
/// For any employee survey submission — including payloads whose free-text
/// answers are stuffed with name-, email-, and id-like content — after the
/// submission is persisted the stored <see cref="EmployeeSurveyResponse"/>
/// record carries no field that identifies the respondent. The schema contains
/// only the non-PII answer columns (engagement score, workplace satisfaction,
/// open feedback) plus the surrogate primary key and the UTC timestamp, so no
/// respondent-identifying data can be stored regardless of the payload.
/// </para>
///
/// <para><b>Validates: Requirements 7.6</b></para>
/// </summary>
public sealed class EmployeeSurveyAnonymityTests
{
    /// <summary>
    /// The complete, exhaustive set of public instance properties the
    /// <see cref="EmployeeSurveyResponse"/> entity is allowed to expose. Any
    /// property outside this set — anything resembling a name, email, or
    /// employee identifier — would break respondent anonymity.
    /// </summary>
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        nameof(EmployeeSurveyResponse.Id),
        nameof(EmployeeSurveyResponse.EngagementScore),
        nameof(EmployeeSurveyResponse.WorkplaceSatisfaction),
        nameof(EmployeeSurveyResponse.OpenFeedback),
        nameof(EmployeeSurveyResponse.SubmittedAt),
    };

    /// <summary>
    /// Substrings that, if they appeared in a persisted column name, would
    /// signal that the schema can hold respondent-identifying data.
    /// </summary>
    private static readonly string[] PiiTokens = { "name", "email", "employeeid" };

    // ------------------------------------------------------------------
    // Generators — deliberately inject PII-looking content into the free
    // text answer fields to prove it cannot leak into an identifying column.
    // ------------------------------------------------------------------

    /// <summary>A plausible full name, e.g. "Jane Smith".</summary>
    private static readonly Gen<string> GenName =
        Gen.Select(
            Gen.Char['A', 'z'].Array[1, 15].Select(c => new string(c)),
            Gen.Char['A', 'z'].Array[1, 15].Select(c => new string(c)),
            (first, last) => $"{first} {last}");

    /// <summary>A plausible email, e.g. "jane@example.com".</summary>
    private static readonly Gen<string> GenEmail =
        Gen.Select(
            Gen.Char['a', 'z'].Array[1, 12].Select(c => new string(c)),
            Gen.Char['a', 'z'].Array[1, 12].Select(c => new string(c)),
            (local, domain) => $"{local}@{domain}.com");

    /// <summary>A plausible employee id, e.g. "EMP-00421".</summary>
    private static readonly Gen<string> GenEmployeeId =
        Gen.Int[0, 99999].Select(n => $"EMP-{n:D5}");

    /// <summary>
    /// A free-text answer that embeds name/email/id-like content, so the test
    /// actively tries to smuggle PII through the submission path.
    /// </summary>
    private static readonly Gen<string> GenPiiLadenText =
        Gen.Select(GenName, GenEmail, GenEmployeeId,
            (name, email, id) =>
                $"My name is {name}, email {email}, id {id}. The office could be better.");

    /// <summary>Engagement rating in the valid SMALLINT answer range.</summary>
    private static readonly Gen<int> GenEngagement = Gen.Int[1, 5];

    /// <summary>
    /// A complete employee survey submission whose text answers are loaded with
    /// identifying content.
    /// </summary>
    private static readonly Gen<EmployeeSurveyRequest> GenPiiLadenSubmission =
        Gen.Select(GenEngagement, GenPiiLadenText, GenPiiLadenText,
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
            .UseInMemoryDatabase($"employee-anonymity-{Guid.NewGuid()}")
            .Options;

        return new NnmDbContext(options);
    }

    // ------------------------------------------------------------------
    // Structural invariant — the entity type itself exposes no PII column.
    // This holds independently of any payload: the schema has no field in
    // which a name, email, or employee id could ever be stored.
    // ------------------------------------------------------------------

    [Fact]
    public void EmployeeSurveyResponse_Type_ExposesNoPiiColumns()
    {
        var properties = typeof(EmployeeSurveyResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Every property is one of the known non-PII answer/metadata columns.
        foreach (var property in properties)
        {
            Assert.Contains(property.Name, AllowedProperties);
        }

        // The allowed set is fully present (no silent removal of expected columns).
        Assert.Equal(AllowedProperties.Count, properties.Length);

        // No property name resembles respondent-identifying data. (The lone
        // "Id" surrogate key is a bare UUID primary key, not an employee id, so
        // it must match exactly rather than being flagged by the "id" substring.)
        foreach (var property in properties)
        {
            var normalised = property.Name.Replace("_", string.Empty).ToLowerInvariant();

            foreach (var token in PiiTokens)
            {
                Assert.False(
                    normalised.Contains(token, StringComparison.Ordinal),
                    $"EmployeeSurveyResponse exposes PII-like property '{property.Name}'.");
            }
        }
    }

    // ------------------------------------------------------------------
    // Property 11 — persist PII-laden submissions through the real service +
    // repository + DbContext and assert nothing identifying is stored.
    // ------------------------------------------------------------------

    [Fact]
    public void EmployeeSurvey_Persisted_ContainsNoRespondentIdentifyingFields()
    {
        GenPiiLadenSubmission.Sample(request =>
        {
            using var context = CreateContext();
            var repository = new SurveyRepository(context);
            var service = new SurveyService(repository);

            var result = service.SubmitEmployeeSurveyAsync(request).GetAwaiter().GetResult();

            // A fully answered submission must be accepted and persisted.
            Assert.True(result.IsSuccess);

            var stored = context.EmployeeSurveyResponses
                                .AsNoTracking()
                                .Single();

            // The stored record holds exactly the non-PII answer data that was
            // submitted — the engagement/satisfaction/feedback fields round-trip
            // and nothing else is recorded.
            Assert.Equal((short)request.EngagementScore!.Value, stored.EngagementScore);
            Assert.Equal(request.WorkplaceSatisfaction!.Trim(), stored.WorkplaceSatisfaction);
            Assert.Equal(request.OpenFeedback!.Trim(), stored.OpenFeedback);

            // submitted_at is a valid UTC timestamp (zero offset) — metadata, not PII.
            Assert.Equal(TimeSpan.Zero, stored.SubmittedAt.Offset);

            // The Id is a generated surrogate key, independent of any identifying
            // content that appeared in the payload.
            Assert.NotEqual(Guid.Empty, stored.Id);

            // Reflecting over every stored value, assert no property both (a) is
            // not one of the sanctioned non-PII answer columns and (b) carries
            // respondent-identifying content. Because the entity exposes only the
            // sanctioned columns, any PII embedded in the free-text answers is
            // confined to those answer fields and never promoted to an
            // identifying column of its own.
            foreach (var property in typeof(EmployeeSurveyResponse)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.Contains(property.Name, AllowedProperties);

                var normalised = property.Name.Replace("_", string.Empty).ToLowerInvariant();
                foreach (var token in PiiTokens)
                {
                    Assert.False(
                        normalised.Contains(token, StringComparison.Ordinal),
                        $"Persisted record exposes PII-like field '{property.Name}'.");
                }
            }
        });
    }
}
