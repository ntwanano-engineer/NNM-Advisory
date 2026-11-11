using System.ComponentModel.DataAnnotations;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Strongly-typed settings for CORS policy enforcement.
/// Maps to the "Cors" configuration section.
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>
    /// The single allowed frontend origin for cross-origin requests.
    /// All other origins are rejected with HTTP 403 (Requirement 10.5).
    /// Sourced from CORS_ALLOWED_ORIGIN (local) or /nnm/cors/allowed-origin (AWS).
    /// Example: "http://localhost:3000" or "https://nnmadvisory.com"
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Cors:AllowedOrigin is required. " +
                       "Set CORS_ALLOWED_ORIGIN in your environment or .env file. " +
                       "Example: http://localhost:3000")]
    [Url(ErrorMessage = "Cors:AllowedOrigin must be a valid URL (e.g., https://nnmadvisory.com).")]
    public string AllowedOrigin { get; set; } = string.Empty;
}
