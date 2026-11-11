using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Stores key-value pairs of configurable company content such as the tagline
/// and hero call-to-action label.
///
/// Maps to the <c>company_content</c> table.
///
/// Requirements 11.1: THE Database SHALL persist company profile content.
/// </summary>
public sealed class CompanyContent
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Logical identifier for the content item, e.g. "tagline" or
    /// "hero_cta_label". Must be unique across all rows.
    /// </summary>
    [Column("content_key")]
    [MaxLength(100)]
    public string ContentKey { get; set; } = string.Empty;

    /// <summary>The current value for this content item.</summary>
    [Column("content_value")]
    public string ContentValue { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
