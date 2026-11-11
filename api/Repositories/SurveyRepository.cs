using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISurveyRepository"/>.
///
/// Every write is compiled to a parameterised SQL statement by EF Core,
/// satisfying Requirement 11.3 (no raw string interpolation into SQL).
///
/// The repository is registered as a scoped service so that each HTTP request
/// receives its own <see cref="NnmDbContext"/> instance — the standard EF Core
/// lifetime pattern. (Requirement 10.1)
///
/// Anonymity guarantee (Requirement 7.6): for employee survey responses the
/// repository constructs a fresh entity populated only with the non-PII columns
/// defined in the schema (engagement score, workplace satisfaction, open
/// feedback). Even if a caller were to attach extra data, there are no PII
/// columns to persist it to.
/// </summary>
public sealed class SurveyRepository : ISurveyRepository
{
    private readonly NnmDbContext _db;

    public SurveyRepository(NnmDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------------
    // CustomerSurveyResponse
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<CustomerSurveyResponse> AddCustomerSurveyAsync(CustomerSurveyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Stamp submitted_at as UTC at insert time (Requirement 11.6),
        // overriding any value supplied by the caller.
        response.SubmittedAt = DateTimeOffset.UtcNow;

        await _db.CustomerSurveyResponses.AddAsync(response);
        await _db.SaveChangesAsync();

        return response;
    }

    // -----------------------------------------------------------------------
    // EmployeeSurveyResponse
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<EmployeeSurveyResponse> AddEmployeeSurveyAsync(EmployeeSurveyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Build a new entity from only the defined non-PII columns. This makes
        // the anonymity guarantee explicit: no name, email, or employee-ID data
        // can be persisted because no such columns exist (Requirement 7.6).
        var anonymous = new EmployeeSurveyResponse
        {
            EngagementScore = response.EngagementScore,
            WorkplaceSatisfaction = response.WorkplaceSatisfaction,
            OpenFeedback = response.OpenFeedback,
            // Stamp submitted_at as UTC at insert time (Requirement 11.6).
            SubmittedAt = DateTimeOffset.UtcNow,
        };

        await _db.EmployeeSurveyResponses.AddAsync(anonymous);
        await _db.SaveChangesAsync();

        return anonymous;
    }
}
