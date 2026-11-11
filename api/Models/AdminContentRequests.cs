namespace NnmAdvisory.Api.Models;

/// <summary>
/// Request body for <c>PUT /api/admin/profile</c>.
///
/// Carries the new company tagline. Field-level validation (non-blank, ≤ 150
/// characters) is performed in the service layer
/// (<see cref="Services.ICompanyProfileService.UpdateTaglineAsync"/>), which
/// returns a structured validation result rather than relying on model-state
/// binding attributes. This keeps the validation rules in one place and lets
/// the controller map the service outcome to the correct HTTP status.
/// </summary>
public sealed class UpdateTaglineRequest
{
    /// <summary>The new tagline value.</summary>
    public string Tagline { get; set; } = string.Empty;
}

/// <summary>
/// Request body for <c>PUT /api/admin/services/{id}</c>.
///
/// Carries the editable fields of a service block. Validation (title non-blank
/// ≤ 100 chars, description non-blank ≤ 500 chars) is performed in
/// <see cref="Services.ICompanyProfileService.UpdateServiceBlockAsync"/>.
/// </summary>
public sealed class UpdateServiceBlockRequest
{
    /// <summary>The new service title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The new service description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The new list of service offerings. Defaults to an empty array when
    /// omitted from the request body.
    /// </summary>
    public string[] Offerings { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// Request body for <c>PUT /api/admin/leadership/{id}</c>.
///
/// Carries the editable biography and, optionally, an updated photo S3 key.
/// Validation (biography non-blank ≤ 2000 chars) is performed in
/// <see cref="Services.ICompanyProfileService.UpdateLeadershipBiographyAsync"/>.
/// </summary>
public sealed class UpdateLeadershipRequest
{
    /// <summary>The new biographical text.</summary>
    public string Biography { get; set; } = string.Empty;

    /// <summary>
    /// Optional S3 object key for the profile photograph. When <c>null</c> the
    /// existing photo key is left unchanged.
    /// </summary>
    public string? PhotoS3Key { get; set; }
}
