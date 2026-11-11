using System.ComponentModel.DataAnnotations;

namespace NnmAdvisory.Api.Models;

/// <summary>
/// Strongly-typed settings for email delivery.
/// Maps to the "Email" configuration section.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>
    /// Selects the email provider implementation.
    /// Accepted values: "smtp" (local/Mailhog), "ses" (AWS SES).
    /// Sourced from EMAIL_PROVIDER.
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Email:Provider is required. " +
                       "Set EMAIL_PROVIDER to 'smtp' (local) or 'ses' (AWS).")]
    [RegularExpression("^(smtp|ses)$",
        ErrorMessage = "Email:Provider must be either 'smtp' or 'ses'.")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The verified sender email address shown in the From header.
    /// Sourced from SENDER_EMAIL (local) or /nnm/ses/sender-email (AWS).
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Email:SenderEmail is required. " +
                       "Set SENDER_EMAIL in your environment or .env file.")]
    [EmailAddress(ErrorMessage = "Email:SenderEmail must be a valid email address.")]
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>
    /// The company admin inbox that receives client contact notifications.
    /// Sourced from ADMIN_EMAIL (local) or /nnm/ses/admin-email (AWS).
    /// </summary>
    [Required(AllowEmptyStrings = false,
        ErrorMessage = "Email:AdminEmail is required. " +
                       "Set ADMIN_EMAIL in your environment or .env file.")]
    [EmailAddress(ErrorMessage = "Email:AdminEmail must be a valid email address.")]
    public string AdminEmail { get; set; } = string.Empty;

    // -------------------------------------------------------------------------
    // SMTP-only settings (required when Provider == "smtp")
    // -------------------------------------------------------------------------

    /// <summary>
    /// SMTP server hostname (e.g., "mailhog" in Docker, "localhost" outside).
    /// Required when Email:Provider is "smtp".
    /// Sourced from SMTP_HOST.
    /// </summary>
    public string? SmtpHost { get; set; }

    /// <summary>
    /// SMTP server port (e.g., 1025 for Mailhog, 587 for TLS).
    /// Required when Email:Provider is "smtp".
    /// Sourced from SMTP_PORT.
    /// </summary>
    [Range(1, 65535, ErrorMessage = "Email:SmtpPort must be between 1 and 65535.")]
    public int SmtpPort { get; set; } = 1025;
}
