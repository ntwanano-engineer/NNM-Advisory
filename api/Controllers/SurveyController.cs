using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NnmAdvisory.Api.Services;

namespace NnmAdvisory.Api.Controllers;

/// <summary>
/// Public survey submission endpoints.
///
/// <para>
/// Both endpoints are public (no bearer token required) — site visitors and
/// employees submit surveys without authenticating. The controller is a thin
/// translation layer: it binds the request body, delegates all validation and
/// persistence to <see cref="ISurveyService"/>, and maps the resulting
/// <see cref="SurveySubmissionResult"/> to an HTTP status:
/// <list type="bullet">
///   <item><see cref="SurveySubmissionResult.IsSuccess"/> → 202 Accepted with a confirmation message.</item>
///   <item>validation failure → 422 Unprocessable Entity with a per-field error map.</item>
/// </list>
/// </para>
///
/// <para>
/// Database errors are deliberately not caught here; they propagate to the
/// global exception handler, which returns HTTP 500 (Requirement 10.2).
/// </para>
///
/// Requirements 10.2, 10.4.
/// </summary>
[ApiController]
[Route("api/surveys")]
[AllowAnonymous]
public sealed class SurveyController : ControllerBase
{
    private readonly ISurveyService _surveyService;

    public SurveyController(ISurveyService surveyService)
    {
        _surveyService = surveyService;
    }

    /// <summary>
    /// Submits a customer survey response.
    /// </summary>
    /// <remarks>Requirements 10.2, 10.4.</remarks>
    [HttpPost("customer")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SubmitCustomerSurvey([FromBody] CustomerSurveyRequest request)
    {
        var result = await _surveyService.SubmitCustomerSurveyAsync(request);

        if (!result.IsSuccess)
        {
            return UnprocessableEntity(new { errors = result.Errors });
        }

        return Accepted(new { message = "Thank you. Your survey response has been received." });
    }

    /// <summary>
    /// Submits an anonymous employee survey response.
    /// </summary>
    /// <remarks>Requirements 10.2, 10.4.</remarks>
    [HttpPost("employee")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SubmitEmployeeSurvey([FromBody] EmployeeSurveyRequest request)
    {
        var result = await _surveyService.SubmitEmployeeSurveyAsync(request);

        if (!result.IsSuccess)
        {
            return UnprocessableEntity(new { errors = result.Errors });
        }

        return Accepted(new { message = "Thank you. Your survey response has been received." });
    }
}
