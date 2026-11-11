using System.Text.Json;

namespace NnmAdvisory.Api.Infrastructure;

/// <summary>
/// Catches any unhandled exception that bubbles up through the middleware pipeline,
/// logs it with a UTC timestamp, the request path, and the error message, then
/// returns a generic HTTP 500 response.
///
/// Requirements 10.6: The Backend API SHALL log all unhandled exceptions with a
/// UTC timestamp, request path, and error message to a persistent log store.
///
/// Requirements 10.8: IF the Backend API fails to connect to the persistent log
/// store, THEN the Backend API SHALL continue processing requests and attempt to
/// write log entries to a fallback local log file (handled by Serilog sink
/// configuration in appsettings.json).
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Log the full exception details for internal diagnostics.
            // Serilog enriches this with the UTC timestamp automatically.
            _logger.LogError(
                ex,
                "Unhandled exception at {UtcTimestamp:O} on {RequestPath}. Error: {ErrorMessage}",
                DateTime.UtcNow,
                context.Request.Path,
                ex.Message);

            // Guard against writing the response if headers were already sent
            // (e.g., the exception occurred mid-stream during a response write).
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";

                var body = JsonSerializer.Serialize(
                    new { error = "An unexpected error occurred." },
                    JsonOptions);

                await context.Response.WriteAsync(body);
            }
        }
    }
}
