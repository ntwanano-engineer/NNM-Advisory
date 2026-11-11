namespace NnmAdvisory.Api.Services;

/// <summary>
/// Business-logic contract for validating and persisting survey submissions.
///
/// This service sits between the <c>SurveyController</c> (Task 6.3) and the
/// <see cref="Repositories.ISurveyRepository"/> data layer. It owns all
/// server-side validation so the controller can stay a thin translation layer
/// that maps a <see cref="SurveySubmissionResult"/> to the correct HTTP status
/// (202 on success, 422 on validation failure — Requirements 6.4, 7.4, 10.4).
///
/// <para>
/// Customer survey rules (Requirement 6.2–6.4): service satisfaction must be an
/// integer in [1, 5], NPS must be an integer in [0, 10], and open feedback (when
/// present) must be at most 1000 characters.
/// </para>
///
/// <para>
/// Employee survey rules (Requirement 7.2, 7.4, 7.6): the three required answer
/// categories (engagement, workplace satisfaction, open feedback) must be
/// present, and no personally identifiable information is ever persisted — only
/// the non-PII answer fields are forwarded to the repository.
/// </para>
/// </summary>
public interface ISurveyService
{
    /// <summary>
    /// Validates and persists a customer survey submission.
    ///
    /// Returns <see cref="SurveySubmissionResult.Success"/> after the response is
    /// stored; otherwise returns a validation failure carrying a per-field error
    /// map and persists nothing (Requirement 6.4).
    /// </summary>
    /// <param name="request">The raw customer survey payload from the request body.</param>
    Task<SurveySubmissionResult> SubmitCustomerSurveyAsync(CustomerSurveyRequest request);

    /// <summary>
    /// Validates and persists an anonymous employee survey submission.
    ///
    /// Returns <see cref="SurveySubmissionResult.Success"/> after the response is
    /// stored; otherwise returns a validation failure carrying a per-field error
    /// map and persists nothing (Requirement 7.4). Only non-PII answer fields are
    /// forwarded for persistence (Requirement 7.6).
    /// </summary>
    /// <param name="request">The raw employee survey payload from the request body.</param>
    Task<SurveySubmissionResult> SubmitEmployeeSurveyAsync(EmployeeSurveyRequest request);
}
