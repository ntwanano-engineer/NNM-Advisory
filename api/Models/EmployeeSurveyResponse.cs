using System.ComponentModel.DataAnnotations.Schema;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Stores a single anonymous employee survey response.
///
/// Maps to the <c>employee_survey_responses</c> table.
///
/// Requirements 7.2–7.3, 7.6, 11.1, 11.6: THE Database SHALL persist employee
/// survey responses with a UTC timestamp. THIS TABLE DELIBERATELY CONTAINS NO
/// PII FIELDS (no name, email, or employee ID) to preserve respondent anonymity.
/// </summary>
public sealed class EmployeeSurveyResponse
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Employee engagement rating stored as SMALLINT.
    /// (Requirement 7.2)
    /// </summary>
    [Column("engagement_score")]
    public short EngagementScore { get; set; }

    /// <summary>
    /// Workplace satisfaction answer — a short text response to the
    /// workplace satisfaction question. (Requirement 7.2)
    /// </summary>
    [Column("workplace_satisfaction")]
    public string WorkplaceSatisfaction { get; set; } = string.Empty;

    /// <summary>
    /// Optional open-ended feedback text. (Requirement 7.2)
    /// </summary>
    [Column("open_feedback")]
    public string? OpenFeedback { get; set; }

    /// <summary>
    /// UTC timestamp recorded at the moment the response was received.
    /// (Requirement 7.3, 11.6)
    /// </summary>
    [Column("submitted_at")]
    public DateTimeOffset SubmittedAt { get; set; }
}
