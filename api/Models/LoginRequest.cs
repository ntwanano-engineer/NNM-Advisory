using System.ComponentModel.DataAnnotations;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Request body for <c>POST /api/auth/login</c>.
///
/// The length bounds are deliberately permissive guards against abuse
/// (oversized payloads), not credential-policy checks — the service layer
/// decides correctness. Keeping validation here identical for both fields
/// avoids leaking which field was malformed (Requirement 10.7).
/// </summary>
public sealed class LoginRequest
{
    /// <summary>The admin username. Non-empty, at most 254 characters.</summary>
    [Required(ErrorMessage = "The Username field is required.")]
    [MaxLength(254, ErrorMessage = "The Username field must not exceed 254 characters.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>The admin password. Non-empty, at most 128 characters.</summary>
    [Required(ErrorMessage = "The Password field is required.")]
    [MaxLength(128, ErrorMessage = "The Password field must not exceed 128 characters.")]
    public string Password { get; set; } = string.Empty;
}
