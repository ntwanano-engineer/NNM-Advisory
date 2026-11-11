using System.ComponentModel.DataAnnotations.Schema;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Stores a single customer survey response.
///
/// Maps to the <c>customer_survey_responses</c> table.
///
/// Requirements 6.2–6.4, 11.1, 11.6: THE Database SHALL persist customer survey
/// responses with a UTC timestamp.
/// </summary>
public sealed class CustomerSurveyResponse
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Service satisfaction rating — integer in [1, 5].
    /// Stored as SMALLINT with a CHECK constraint in the database.
    /// (Requirement 6.2)
    /// </summary>
    [Column("service_satisfaction")]
    public short ServiceSatisfaction { get; set; }

    /// <summary>
    /// Net Promoter Score — integer in [0, 10].
    /// Stored as SMALLINT with a CHECK constraint in the database.
    /// (Requirement 6.2)
    /// </summary>
    [Column("nps_score")]
    public short NpsScore { get; set; }

    /// <summary>
    /// Optional free-text feedback — max 1000 characters enforced by a CHECK
    /// constraint in the database.
    /// (Requirement 6.2)
    /// </summary>
    [Column("open_feedback")]
    public string? OpenFeedback { get; set; }

    /// <summary>
    /// UTC timestamp recorded at the moment the response was received.
    /// (Requirement 6.3, 11.6)
    /// </summary>
    [Column("submitted_at")]
    public DateTimeOffset SubmittedAt { get; set; }
}
