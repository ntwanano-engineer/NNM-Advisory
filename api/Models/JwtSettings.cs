using System.ComponentModel.DataAnnotations;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Strongly-typed settings for JWT bearer token issuance and validation.
/// Maps to the "Jwt" configuration section.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HS256 signing secret — must be at least 32 characters (256 bits).
    /// Sourced from JWT_SECRET (local) or /nnm/jwt/secret (AWS Parameter Store).
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Jwt:Secret is required. " +
                       "Set JWT_SECRET in your environment or .env file.")]
    [MinLength(32,
        ErrorMessage = "Jwt:Secret must be at least 32 characters (256 bits) for HS256 signing.")]
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Issuer claim embedded in the token. Defaults to "NnmAdvisory".
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Jwt:Issuer is required. Set it in appsettings.json or your environment.")]
    public string Issuer { get; set; } = "NnmAdvisory";

    /// <summary>
    /// Audience claim embedded in the token. Defaults to "NnmAdvisoryAdmin".
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Jwt:Audience is required. Set it in appsettings.json or your environment.")]
    public string Audience { get; set; } = "NnmAdvisoryAdmin";

    /// <summary>
    /// Token lifetime in minutes. Must be between 1 and 1440 (24 hours).
    /// Requirement 8.6 specifies a 60-minute inactivity timeout; this sets the token expiry.
    /// </summary>
    [Range(1, 1440,
        ErrorMessage = "Jwt:ExpiryMinutes must be between 1 and 1440.")]
    public int ExpiryMinutes { get; set; } = 60;
}
