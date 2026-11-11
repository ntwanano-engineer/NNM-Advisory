using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Represents one of NNM Advisory's service categories shown on the Services page.
///
/// Maps to the <c>service_blocks</c> table.
///
/// Requirements 3.1, 11.1: THE Database SHALL persist service block content.
/// </summary>
public sealed class ServiceBlock
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Machine-readable category identifier, e.g. "accounting", "it",
    /// "customer_survey", "employee_survey". Unique per row.
    /// </summary>
    [Column("category")]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Human-readable service title shown as the block heading.</summary>
    [Column("title")]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Paragraph text describing the service.</summary>
    [Column("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// List of distinct service offerings within this category,
    /// stored as a PostgreSQL TEXT[] array column.
    /// </summary>
    [Column("offerings", TypeName = "text[]")]
    public string[] Offerings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Controls the visual ordering of service blocks on the page.
    /// Lower values appear first.
    /// </summary>
    [Column("display_order")]
    public int DisplayOrder { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
