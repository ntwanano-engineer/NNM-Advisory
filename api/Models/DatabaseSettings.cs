using System.ComponentModel.DataAnnotations;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Strongly-typed settings for the PostgreSQL database connection.
/// Maps to the "Database" configuration section.
/// </summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>
    /// Full PostgreSQL connection string.
    /// Sourced from DB_CONNECTION_STRING (local) or
    /// /nnm/db/connection-string (AWS Parameter Store).
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Database:ConnectionString is required. " +
                       "Set DB_CONNECTION_STRING in your environment or .env file.")]
    public string ConnectionString { get; set; } = string.Empty;
}
