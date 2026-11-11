using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Default implementation of <see cref="ISurveyService"/>.
///
/// Performs all server-side validation and, on success, delegates persistence
/// to <see cref="ISurveyRepository"/>. The repository stamps the UTC
/// <c>submitted_at</c> timestamp at insert time (Requirements 6.3, 7.3, 11.6),
/// so this service does not set it.
///
/// <para>
/// Lifetime: registered as <c>Scoped</c> so it aligns with the scoped
/// repository / <see cref="Infrastructure.NnmDbContext"/>.
/// </para>
/// </summary>
public sealed class SurveyService : ISurveyService
{
    /// <summary>Inclusive lower bound for the customer service-satisfaction score (Requirement 6.2).</summary>
    public const int ServiceSatisfactionMin = 1;

    /// <summary>Inclusive upper bound for the customer service-satisfaction score (Requirement 6.2).</summary>
    public const int ServiceSatisfactionMax = 5;

    /// <summary>Inclusive lower bound for the NPS score (Requirement 6.2).</summary>
    public const int NpsScoreMin = 0;

    /// <summary>Inclusive upper bound for the NPS score (Requirement 6.2).</summary>
    public const int NpsScoreMax = 10;

    /// <summary>Maximum length of open-ended feedback, in characters (Requirement 6.2).</summary>
    public const int OpenFeedbackMaxLength = 1000;

    private readonly ISurveyRepository _repository;

    public SurveyService(ISurveyRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public async Task<SurveySubmissionResult> SubmitCustomerSurveyAsync(CustomerSurveyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();

        // service_satisfaction: required and in [1, 5]. A null value means the
        // required question was not answered (Requirement 6.4).
        if (request.ServiceSatisfaction is null)
        {
            errors["serviceSatisfaction"] = new[] { "The service satisfaction rating is required." };
        }
        else if (request.ServiceSatisfaction is < ServiceSatisfactionMin or > ServiceSatisfactionMax)
        {
            errors["serviceSatisfaction"] = new[]
            {
                $"The service satisfaction rating must be between {ServiceSatisfactionMin} and {ServiceSatisfactionMax}."
            };
        }

        // nps_score: required and in [0, 10] (Requirements 6.2, 6.4).
        if (request.NpsScore is null)
        {
            errors["npsScore"] = new[] { "The NPS score is required." };
        }
        else if (request.NpsScore is < NpsScoreMin or > NpsScoreMax)
        {
            errors["npsScore"] = new[]
            {
                $"The NPS score must be between {NpsScoreMin} and {NpsScoreMax}."
            };
        }

        // open_feedback: optional, but bounded to 1000 characters when present
        // (Requirement 6.2).
        if (request.OpenFeedback is { Length: > OpenFeedbackMaxLength })
        {
            errors["openFeedback"] = new[]
            {
                $"The open feedback must be {OpenFeedbackMaxLength} characters or fewer."
            };
        }

        if (errors.Count > 0)
        {
            return SurveySubmissionResult.ValidationFailure(errors);
        }

        var response = new CustomerSurveyResponse
        {
            // Values are validated non-null above; the casts are in range of short.
            ServiceSatisfaction = (short)request.ServiceSatisfaction!.Value,
            NpsScore = (short)request.NpsScore!.Value,
            OpenFeedback = NormaliseOptional(request.OpenFeedback),
            // submitted_at is stamped by the repository at insert time
            // (Requirements 6.3, 11.6).
        };

        await _repository.AddCustomerSurveyAsync(response);

        return SurveySubmissionResult.Success();
    }

    /// <inheritdoc/>
    public async Task<SurveySubmissionResult> SubmitEmployeeSurveyAsync(EmployeeSurveyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();

        // All three answer categories are required (Requirements 7.2, 7.4).
        if (request.EngagementScore is null)
        {
            errors["engagementScore"] = new[] { "The employee engagement response is required." };
        }

        if (string.IsNullOrWhiteSpace(request.WorkplaceSatisfaction))
        {
            errors["workplaceSatisfaction"] = new[] { "The workplace satisfaction response is required." };
        }

        if (string.IsNullOrWhiteSpace(request.OpenFeedback))
        {
            errors["openFeedback"] = new[] { "The open-ended feedback response is required." };
        }

        if (errors.Count > 0)
        {
            return SurveySubmissionResult.ValidationFailure(errors);
        }

        // Build the entity from only the non-PII answer fields. Any PII the
        // caller may have supplied is never read here, and the repository writes
        // only these columns, so no PII can be persisted (Requirement 7.6).
        var response = new EmployeeSurveyResponse
        {
            EngagementScore = (short)request.EngagementScore!.Value,
            WorkplaceSatisfaction = request.WorkplaceSatisfaction!.Trim(),
            OpenFeedback = request.OpenFeedback!.Trim(),
            // submitted_at is stamped by the repository at insert time
            // (Requirements 7.3, 11.6).
        };

        await _repository.AddEmployeeSurveyAsync(response);

        return SurveySubmissionResult.Success();
    }

    /// <summary>
    /// Trims optional free text, collapsing an empty or whitespace-only value to
    /// <c>null</c> so blank feedback is stored as SQL NULL rather than "".
    /// </summary>
    private static string? NormaliseOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
