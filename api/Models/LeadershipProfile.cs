using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Represents a leadership team member shown on the About Us page.
///
/// Maps to the <c>leadership_profiles</c> table.
///
/// Requirements 4.1–4.8, 11.1: THE Database SHALL persist leadership profiles.
/// </summary>
public sealed class LeadershipProfile
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Team member's full name — max 100 characters.</summary>
    [Column("full_name")]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Job title, e.g. "Chief Operating Officer (COO)" — max 100 characters.</summary>
    [Column("title")]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Biographical text — max 2000 characters enforced by a CHECK constraint
    /// in the database and validated at the API layer before persistence.
    /// (Requirement 4.4, 4.7, 4.8)
    /// </summary>
    [Column("biography")]
    public string Biography { get; set; } = string.Empty;

    /// <summary>
    /// S3 object key for the profile photograph, e.g.
    /// "leadership/ntwanano-mabasa.jpg". NULL when no photo has been uploaded.
    /// </summary>
    [Column("photo_s3_key")]
    [MaxLength(500)]
    public string? PhotoS3Key { get; set; }

    /// <summary>
    /// Controls the visual ordering of profiles on the About Us page.
    /// Lower values appear first. (Requirement 4.1)
    /// </summary>
    [Column("display_order")]
    public int DisplayOrder { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
