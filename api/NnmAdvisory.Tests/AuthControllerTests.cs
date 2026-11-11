using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NnmAdvisory.Api.Controllers;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Unit tests for <see cref="AuthController"/>, covering the mapping of each
/// <see cref="AuthOutcome"/> to its HTTP status code, the credential
/// non-disclosure guarantee, input validation, and the stateless logout.
///
/// <para><b>Validates: Requirements 8.3, 8.4, 8.5, 10.7</b></para>
/// </summary>
public sealed class AuthControllerTests
{
    /// <summary>
    /// A hand-rolled <see cref="IAuthService"/> stub returning a pre-set
    /// result and recording the credentials it was called with.
    /// </summary>
    private sealed class StubAuthService : IAuthService
    {
        private readonly AuthResult _result;

        public string? ReceivedUsername { get; private set; }
        public string? ReceivedPassword { get; private set; }
        public int CallCount { get; private set; }

        public StubAuthService(AuthResult result) => _result = result;

        public Task<AuthResult> AuthenticateAsync(string username, string password)
        {
            CallCount++;
            ReceivedUsername = username;
            ReceivedPassword = password;
            return Task.FromResult(_result);
        }
    }

    // ------------------------------------------------------------------
    // Success → 200 with token.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Login_Success_Returns200WithToken()
    {
        var stub = new StubAuthService(AuthResult.Success("jwt-token-value"));
        var controller = new AuthController(stub);

        var response = await controller.Login(
            new LoginRequest { Username = "admin", Password = "correct-horse" });

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal("jwt-token-value", GetProperty(ok.Value, "token"));
    }

    // ------------------------------------------------------------------
    // InvalidCredentials → 401 with a generic message (Requirement 10.7).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Login_InvalidCredentials_Returns401()
    {
        var stub = new StubAuthService(AuthResult.InvalidCredentials());
        var controller = new AuthController(stub);

        var response = await controller.Login(
            new LoginRequest { Username = "admin", Password = "wrong" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(response);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Equal("Invalid credentials.", GetProperty(unauthorized.Value, "error"));
    }

    [Fact]
    public async Task Login_InvalidCredentials_DoesNotDiscloseWhichFieldWasWrong()
    {
        // Both an unknown-username and a wrong-password attempt surface the same
        // InvalidCredentials outcome; the response must be byte-identical so the
        // caller cannot distinguish the two (Requirement 10.7).
        var controller = new AuthController(new StubAuthService(AuthResult.InvalidCredentials()));

        var unknownUser = Assert.IsType<UnauthorizedObjectResult>(
            await controller.Login(new LoginRequest { Username = "ghost", Password = "whatever" }));
        var wrongPassword = Assert.IsType<UnauthorizedObjectResult>(
            await controller.Login(new LoginRequest { Username = "admin", Password = "nope" }));

        Assert.Equal(unknownUser.StatusCode, wrongPassword.StatusCode);
        Assert.Equal(
            GetProperty(unknownUser.Value, "error"),
            GetProperty(wrongPassword.Value, "error"));
    }

    // ------------------------------------------------------------------
    // AccountLocked → 429 with an ISO 8601 UTC unlock timestamp (Req 8.5).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Login_AccountLocked_Returns429WithIsoUnlockTime()
    {
        var unlockAt = new DateTimeOffset(2024, 1, 1, 12, 15, 0, TimeSpan.Zero);
        var stub = new StubAuthService(AuthResult.Locked(unlockAt));
        var controller = new AuthController(stub);

        var response = await controller.Login(
            new LoginRequest { Username = "admin", Password = "correct-but-locked" });

        var locked = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status429TooManyRequests, locked.StatusCode);
        Assert.Equal(
            "Account locked. Try again after 2024-01-01T12:15:00Z.",
            GetProperty(locked.Value, "error"));
    }

    [Fact]
    public async Task Login_AccountLocked_FormatsNonUtcOffsetAsUtc()
    {
        // A lockout time expressed in a non-zero offset must still be emitted as
        // a UTC (Z-suffixed) ISO 8601 timestamp.
        var unlockAtPlusTwo = new DateTimeOffset(2024, 1, 1, 14, 15, 0, TimeSpan.FromHours(2));
        var controller = new AuthController(new StubAuthService(AuthResult.Locked(unlockAtPlusTwo)));

        var response = await controller.Login(
            new LoginRequest { Username = "admin", Password = "x" });

        var locked = Assert.IsType<ObjectResult>(response);
        Assert.Equal(
            "Account locked. Try again after 2024-01-01T12:15:00Z.",
            GetProperty(locked.Value, "error"));
    }

    // ------------------------------------------------------------------
    // Blank credentials → 422 without reaching the service (Req 10.7).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("admin", "")]
    [InlineData("admin", "   ")]
    public async Task Login_BlankCredential_Returns422AndDoesNotCallService(
        string username, string password)
    {
        var stub = new StubAuthService(AuthResult.Success("should-not-be-issued"));
        var controller = new AuthController(stub);

        var response = await controller.Login(
            new LoginRequest { Username = username, Password = password });

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(response);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, unprocessable.StatusCode);
        Assert.Equal(0, stub.CallCount);
    }

    // ------------------------------------------------------------------
    // Logout → 200 (stateless JWT).
    // ------------------------------------------------------------------

    [Fact]
    public void Logout_Returns200WithMessage()
    {
        var controller = new AuthController(new StubAuthService(AuthResult.InvalidCredentials()));

        var response = controller.Logout();

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal("Logged out.", GetProperty(ok.Value, "message"));
    }

    /// <summary>
    /// Reads a named property value from an anonymous response object via
    /// reflection (the controller returns anonymous types).
    /// </summary>
    private static string? GetProperty(object? value, string propertyName)
    {
        Assert.NotNull(value);
        var property = value!.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return property!.GetValue(value)?.ToString();
    }
}
