using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Services;

namespace NnmAdvisory.Api.Controllers;

/// <summary>
/// Public contact form endpoint.
///
/// <para>
/// <c>POST /api/contact</c> accepts a contact submission from any website
/// visitor — no bearer token is required. It delegates all validation and
/// persistence to <see cref="IContactService"/> and maps the
/// <see cref="ContactOutcome"/> result to an HTTP status code:
/// <list type="bullet">
///   <item><see cref="ContactOutcome.Accepted"/> → HTTP 202 Accepted (Requirement 10.3).</item>
///   <item><see cref="ContactOutcome.ValidationFailed"/> → HTTP 422 with a
///   per-field error map (Requirements 10.2, 10.4).</item>
/// </list>
/// </para>
///
/// <para>
/// Database / infrastructure failures are not caught here: they propagate to
/// the global exception handler, which returns HTTP 500. A malformed or
/// unparseable JSON body is rejected by model binding with HTTP 400.
/// </para>
///
/// Requirements 10.2, 10.3, 10.4.
/// </summary>
[ApiController]
[Route("api/contact")]
[AllowAnonymous]
public sealed class ContactController : ControllerBase
{
    private readonly IContactService _contactService;

    public ContactController(IContactService contactService)
    {
        _contactService = contactService;
    }

    /// <summary>
    /// Submits a contact form. Returns 202 when accepted, 422 with a per-field
    /// error map when validation fails.
    /// </summary>
    /// <remarks>Requirements 10.2, 10.3, 10.4.</remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Submit(
        [FromBody] ContactRequest request,
        CancellationToken cancellationToken)
    {
        var submission = new ContactSubmission
        {
            FullName = request.FullName ?? string.Empty,
            Email = request.Email ?? string.Empty,
            Subject = request.Subject ?? string.Empty,
            Message = request.Message ?? string.Empty
        };

        // Validation and persistence happen in the service. A database failure
        // during persistence throws and is handled by the global exception
        // handler (HTTP 500) — deliberately not caught here (Requirement 10.4).
        var result = await _contactService.SubmitAsync(submission, cancellationToken);

        return result.Outcome switch
        {
            // 202 Accepted: the submission was persisted; emails are dispatched
            // best-effort by the service (Requirement 10.3).
            ContactOutcome.Accepted =>
                Accepted(new { message = "Thank you for reaching out. We'll be in touch soon." }),

            // 422 with the per-field error map, ready to serialise as
            // { "errors": { field: [messages] } } (Requirements 10.2, 10.4).
            _ => UnprocessableEntity(new { errors = result.Errors }),
        };
    }
}
