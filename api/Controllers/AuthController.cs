using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Services;

namespace NnmAdvisory.Api.Controllers;

/// <summary>
/// Admin authentication endpoints.
///
/// <para>
/// Both endpoints are public (no bearer token required) — an admin cannot hold
/// a token before logging in, and logout is stateless for a JWT-based scheme.
/// </para>
///
/// <para>
/// Login maps the three <see cref="AuthOutcome"/> values to HTTP status codes:
/// <list type="bullet">
///   <item><see cref="AuthOutcome.Success"/> → 200 OK with the issued token.</item>
///   <item><see cref="AuthOutcome.InvalidCredentials"/> → 401 Unauthorized.</item>
///   <item><see cref="AuthOutcome.AccountLocked"/> → 429 Too Many Requests.</item>
/// </list>
/// The 401 response is identical whether the username or the password was
/// wrong, so the API never discloses which field failed (Requirement 10.7).
/// </para>
///
/// Requirements 8.3, 8.4, 8.5, 10.7.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Authenticates an admin user and, on success, returns a signed JWT.
    /// </summary>
    /// <remarks>
    /// Requirements 8.3, 8.4, 8.5, 10.7.
    /// </remarks>
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // [Required]/[MaxLength] data annotations are enforced automatically by
        // [ApiController], but [Required] accepts a whitespace-only string.
        // Guard against that explicitly so a blank credential is treated as a
        // validation error rather than reaching the service layer. The error
        // payload is intentionally generic and does not name which field was
        // blank (Requirement 10.7).
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return UnprocessableEntity(new
            {
                error = "Username and password are required."
            });
        }

        var result = await _authService.AuthenticateAsync(request.Username, request.Password);

        return result.Outcome switch
        {
            AuthOutcome.Success =>
                Ok(new { token = result.Token }),

            // 429 with the ISO 8601 UTC unlock time (Requirement 8.5).
            AuthOutcome.AccountLocked =>
                StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = $"Account locked. Try again after {FormatUnlockTime(result.LockedUntil)}."
                }),

            // Generic 401 for any invalid-credential case — does not reveal
            // whether the username or password was wrong (Requirement 10.7).
            _ => Unauthorized(new { error = "Invalid credentials." }),
        };
    }

    /// <summary>
    /// Logs the admin out. JWT sessions are stateless, so there is no
    /// server-side session to invalidate; the client discards its token. This
    /// endpoint returns 200 so the frontend can confirm the logout action.
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Logout()
    {
        return Ok(new { message = "Logged out." });
    }

    /// <summary>
    /// Formats the lockout-release instant as an ISO 8601 UTC timestamp
    /// (e.g. <c>2024-01-01T12:15:00Z</c>). Falls back to "now" defensively if
    /// the service returned a locked outcome without a timestamp, which should
    /// not occur per the <see cref="AuthResult"/> contract.
    /// </summary>
    private static string FormatUnlockTime(DateTimeOffset? lockedUntil)
    {
        var instant = lockedUntil ?? DateTimeOffset.UtcNow;
        return instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}
