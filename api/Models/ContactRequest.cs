namespace NnmAdvisory.Api.Models;

/// <summary>
/// Request body for <c>POST /api/contact</c>.
///
/// <para>
/// Intentionally carries no validation data annotations. All field-level
/// validation is performed by <see cref="Services.IContactService"/>, which
/// returns a per-field error map that the controller maps to HTTP 422
/// (Requirements 10.2, 10.4). If validation attributes lived here,
/// <c>[ApiController]</c> would auto-reject invalid payloads with HTTP 400
/// ProblemDetails before the service ran, bypassing the required 422 contract.
/// </para>
///
/// <para>
/// A missing or structurally malformed JSON body is still rejected by the
/// framework with HTTP 400 during model binding, which is the intended
/// behaviour for unparseable requests.
/// </para>
/// </summary>
public sealed class ContactRequest
{
    /// <summary>Submitter's full name.</summary>
    public string? FullName { get; set; }

    /// <summary>Submitter's email address.</summary>
    public string? Email { get; set; }

    /// <summary>Message subject.</summary>
    public string? Subject { get; set; }

    /// <summary>Message body.</summary>
    public string? Message { get; set; }
}
