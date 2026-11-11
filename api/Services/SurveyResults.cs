namespace NnmAdvisory.Api.Services;

/// <summary>
/// Inbound request model for a customer survey submission.
///
/// This is the raw payload the controller (Task 6.3) binds from the request
/// body and hands to <see cref="ISurveyService.SubmitCustomerSurveyAsync"/>.
/// Scores are nullable so the service can distinguish "not answered" (null)
/// from a supplied-but-out-of-range value and surface the correct per-field
/// error (Requirements 6.4, 10.4).
/// </summary>
public sealed class CustomerSurveyRequest
{
    /// <summary>Service satisfaction rating; valid range is [1, 5] (Requirement 6.2).</summary>
    public int? ServiceSatisfaction { get; init; }

    /// <summary>Net Promoter Score; valid range is [0, 10] (Requirement 6.2).</summary>
    public int? NpsScore { get; init; }

    /// <summary>Optional open-ended feedback; at most 1000 characters (Requirement 6.2).</summary>
    public string? OpenFeedback { get; init; }
}

/// <summary>
/// Inbound request model for an employee survey submission.
///
/// Deliberately exposes only the three non-PII answer categories defined by
/// Requirement 7.2 (engagement, workplace satisfaction, open feedback). Any PII
/// a caller supplies arrives as unmapped JSON and is discarded during model
/// binding; the service additionally guarantees only these fields are persisted
/// (Requirement 7.6).
/// </summary>
public sealed class EmployeeSurveyRequest
{
    /// <summary>Employee engagement rating. Required (Requirement 7.2, 7.4).</summary>
    public int? EngagementScore { get; init; }

    /// <summary>Workplace satisfaction answer. Required (Requirement 7.2, 7.4).</summary>
    public string? WorkplaceSatisfaction { get; init; }

    /// <summary>Open-ended feedback. Required (Requirement 7.2, 7.4).</summary>
    public string? OpenFeedback { get; init; }
}

/// <summary>
/// Outcome of a survey submission attempt.
///
/// Carries either a success flag or a per-field error map that the controller
/// (Task 6.3) maps directly to an HTTP 422 response body of the shape
/// <c>{ "errors": { "fieldName": ["message"] } }</c> defined in the design
/// document (Requirements 6.4, 7.4, 10.4). Keeping the error map keyed by field
/// name lets the controller stay a thin translation layer.
/// </summary>
public sealed class SurveySubmissionResult
{
    private SurveySubmissionResult(
        bool isSuccess,
        IReadOnlyDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    /// <summary>True when the submission validated and was persisted.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Per-field validation errors, keyed by the field name. Empty when
    /// <see cref="IsSuccess"/> is true.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>Builds a success result with no errors.</summary>
    public static SurveySubmissionResult Success() =>
        new(true, new Dictionary<string, string[]>());

    /// <summary>
    /// Builds a validation-failure result from the supplied per-field error map.
    /// </summary>
    public static SurveySubmissionResult ValidationFailure(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(false, errors);
}
