using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Repositories;

/// <summary>
/// Data-access contract for the two survey response entities:
/// <see cref="CustomerSurveyResponse"/> and <see cref="EmployeeSurveyResponse"/>.
///
/// All writes execute as parameterised SQL through EF Core (Requirement 11.3)
/// and are kept in a dedicated repository layer so the service layer stays free
/// of EF Core / infrastructure concerns (Requirement 10.1).
///
/// Anonymity guarantee (Requirement 7.6): the employee survey table deliberately
/// contains no name, email, or employee-ID columns. The repository persists only
/// the columns defined on <see cref="EmployeeSurveyResponse"/>, so no PII can be
/// written even if a caller supplies it.
/// </summary>
public interface ISurveyRepository
{
    // -----------------------------------------------------------------------
    // CustomerSurveyResponse
    // -----------------------------------------------------------------------

    /// <summary>
    /// Persists a customer survey response. The <c>submitted_at</c> column is
    /// stamped with the current UTC instant at insert time (Requirement 11.6),
    /// overriding any value supplied by the caller.
    /// </summary>
    /// <param name="response">The customer survey response to persist.</param>
    /// <returns>The persisted entity, including the database-generated id.</returns>
    Task<CustomerSurveyResponse> AddCustomerSurveyAsync(CustomerSurveyResponse response);

    // -----------------------------------------------------------------------
    // EmployeeSurveyResponse
    // -----------------------------------------------------------------------

    /// <summary>
    /// Persists an anonymous employee survey response. Only the engagement
    /// score, workplace satisfaction, and open feedback columns are written;
    /// no PII fields exist in the schema, preserving respondent anonymity
    /// (Requirement 7.6). The <c>submitted_at</c> column is stamped with the
    /// current UTC instant at insert time (Requirement 11.6).
    /// </summary>
    /// <param name="response">The employee survey response to persist.</param>
    /// <returns>The persisted entity, including the database-generated id.</returns>
    Task<EmployeeSurveyResponse> AddEmployeeSurveyAsync(EmployeeSurveyResponse response);
}
