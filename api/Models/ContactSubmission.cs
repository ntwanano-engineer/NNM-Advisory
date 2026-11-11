using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Stores a contact form submission sent by a website visitor.
///
/// Maps to the <c>contact_submissions</c> table.
///
/// Requirements 5.1–5.5, 11.1, 11.6: THE Database SHALL persist contact
/// submissions with a UTC timestamp.
/// </summary>
public sealed class ContactSubmission
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Submitter's full name — max 100 characters.</summary>
    [Column("full_name")]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Submitter's email address — max 254 characters (RFC 5321).</summary>
    [Column("email")]
    [MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Message subject — max 150 characters.</summary>
    [Column("subject")]
    [MaxLength(150)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Message body — between 10 and 2000 characters, enforced by a CHECK
    /// constraint in the database and validated at the API layer.
    /// (Requirement 5.1)
    /// </summary>
    [Column("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp recorded by the database at the moment of insertion.
    /// (Requirement 11.6)
    /// </summary>
    [Column("submitted_at")]
    public DateTimeOffset SubmittedAt { get; set; }
}
