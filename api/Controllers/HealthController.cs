using Microsoft.AspNetCore.Mvc;

namespace NnmAdvisory.Api.Controllers;

/// <summary>
/// Lightweight health check endpoint used by the ALB, ECS, and Docker Compose
/// to confirm the API process is running and accepting connections.
/// </summary>
[ApiController]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Returns HTTP 200 with a JSON status payload.
    /// No authentication required — the endpoint must be reachable by the load
    /// balancer before any admin session exists.
    /// </summary>
    /// <remarks>
    /// Requirements 13.4, 13.8: the Backend API SHALL expose a health check
    /// endpoint that returns an HTTP 2xx response within 5 seconds of a request,
    /// and the endpoint SHALL be reachable from the Frontend's configured HTTPS
    /// endpoint after deployment is complete.
    /// </remarks>
    [HttpGet("/health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "healthy" });
    }
}
