using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Example-based unit tests for <see cref="AuthService"/>.
///
/// Covers credential validation, the consecutive-failure lockout counter,
/// lockout release after the 15-minute window, and the shape of the issued
/// HS256 JWT.
///
/// <para><b>Validates: Requirements 8.5, 8.6</b></para>
/// </summary>
public sealed class AuthServiceTests
{
    private const string Username = "admin";
    private const string CorrectPassword = "correct-horse-battery-staple";
    private const string WrongPassword = "definitely-not-the-password";
    private const string Secret = "this-is-a-sufficiently-long-test-secret-key-123456";
    private const string Issuer = "NnmAdvisory";
    private const string Audience = "NnmAdvisoryAdmin";
    private const int ExpiryMinutes = 60;

    /// <summary>
    /// A <see cref="TimeProvider"/> whose current instant is fully under test
    /// control, letting us advance the clock to probe the lockout window
    /// without real-time waits.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        /// <summary>Moves the clock forward by <paramref name="delta"/>.</summary>
        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    /// <summary>
    /// Minimal in-memory <see cref="IAdminUserRepository"/> holding a single
    /// change-tracked <see cref="AdminUser"/>. Mutations made by the service
    /// are visible immediately (same object reference), mirroring EF Core's
    /// change-tracking behaviour. <see cref="SaveChangesAsync"/> is a no-op
    /// because the state lives on the tracked instance.
    /// </summary>
    private sealed class InMemoryAdminUserRepository : IAdminUserRepository
    {
        public AdminUser User { get; }

        public InMemoryAdminUserRepository(AdminUser user) => User = user;

        public Task<AdminUser?> GetByUsernameAsync(string username) =>
            Task.FromResult(
                string.Equals(username, User.Username, StringComparison.Ordinal)
                    ? User
                    : null);

        public Task SaveChangesAsync() => Task.CompletedTask;
    }

    private static IOptions<JwtSettings> JwtOptions() =>
        Options.Create(new JwtSettings
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            ExpiryMinutes = ExpiryMinutes,
        });

    private static (AuthService service, FakeTimeProvider clock, InMemoryAdminUserRepository repo)
        CreateService(DateTimeOffset start)
    {
        var user = new AdminUser
        {
            Id = Guid.NewGuid(),
            Username = Username,
            // A real bcrypt hash of the known correct password.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(CorrectPassword),
            FailedLoginAttempts = 0,
            LockedUntil = null,
            CreatedAt = start,
            UpdatedAt = start,
        };

        var clock = new FakeTimeProvider(start);
        var repo = new InMemoryAdminUserRepository(user);
        var service = new AuthService(
            repo,
            JwtOptions(),
            NullLogger<AuthService>.Instance,
            clock);

        return (service, clock, repo);
    }

    private static DateTimeOffset Start() =>
        new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------
    // 1. Correct credentials succeed, issue a token, and reset the counter.
    // ------------------------------------------------------------------

    [Fact]
    public async Task CorrectCredentials_ReturnsSuccessWithTokenAndResetsCounter()
    {
        var (service, _, repo) = CreateService(Start());
        // Simulate a prior failure so we can observe the reset to 0 on success.
        repo.User.FailedLoginAttempts = 3;

        var result = await service.AuthenticateAsync(Username, CorrectPassword);

        Assert.Equal(AuthOutcome.Success, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.Null(result.LockedUntil);
        Assert.Equal(0, repo.User.FailedLoginAttempts);
        Assert.Null(repo.User.LockedUntil);
    }

    // ------------------------------------------------------------------
    // 2. A wrong password yields InvalidCredentials and increments the counter.
    // ------------------------------------------------------------------

    [Fact]
    public async Task WrongPassword_ReturnsInvalidCredentialsAndIncrementsCounter()
    {
        var (service, _, repo) = CreateService(Start());

        var result = await service.AuthenticateAsync(Username, WrongPassword);

        Assert.Equal(AuthOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.Token);
        Assert.Null(result.LockedUntil);
        Assert.Equal(1, repo.User.FailedLoginAttempts);
        Assert.Null(repo.User.LockedUntil);
    }

    // ------------------------------------------------------------------
    // 3. Five consecutive wrong passwords lock the account for 15 minutes.
    // ------------------------------------------------------------------

    [Fact]
    public async Task FiveConsecutiveFailures_LocksAccountForFifteenMinutes()
    {
        var start = Start();
        var (service, _, repo) = CreateService(start);

        AuthResult result = null!;
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            result = await service.AuthenticateAsync(Username, WrongPassword);
        }

        Assert.Equal(AuthOutcome.AccountLocked, result.Outcome);
        Assert.Equal(start.Add(AuthService.LockoutDuration), result.LockedUntil);
        Assert.Equal(AuthService.MaxFailedAttempts, repo.User.FailedLoginAttempts);
        Assert.Equal(start.AddMinutes(15), repo.User.LockedUntil);
    }

    // ------------------------------------------------------------------
    // 4. Lockout release: after advancing past 15 minutes, correct
    //    credentials succeed again (Requirements 8.5 / 8.6).
    // ------------------------------------------------------------------

    [Fact]
    public async Task LockoutReleases_AfterFifteenMinutes_CorrectCredentialsSucceed()
    {
        var start = Start();
        var (service, clock, repo) = CreateService(start);

        // Drive the account into the locked state.
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            await service.AuthenticateAsync(Username, WrongPassword);
        }

        // Still locked one second before the window ends.
        clock.Advance(AuthService.LockoutDuration - TimeSpan.FromSeconds(1));
        var stillLocked = await service.AuthenticateAsync(Username, CorrectPassword);
        Assert.Equal(AuthOutcome.AccountLocked, stillLocked.Outcome);

        // Advance just past the 15-minute window; correct credentials now work.
        clock.Advance(TimeSpan.FromSeconds(2));
        var afterRelease = await service.AuthenticateAsync(Username, CorrectPassword);

        Assert.Equal(AuthOutcome.Success, afterRelease.Outcome);
        Assert.False(string.IsNullOrEmpty(afterRelease.Token));
        Assert.Equal(0, repo.User.FailedLoginAttempts);
        Assert.Null(repo.User.LockedUntil);
    }

    // ------------------------------------------------------------------
    // 5. JWT issuance: decode the token and assert its claims, expiry, and
    //    signature.
    // ------------------------------------------------------------------

    [Fact]
    public async Task IssuedToken_HasExpectedClaimsExpiryAndSignature()
    {
        var start = Start();
        var (service, _, repo) = CreateService(start);

        var result = await service.AuthenticateAsync(Username, CorrectPassword);
        Assert.Equal(AuthOutcome.Success, result.Outcome);
        Assert.NotNull(result.Token);

        var handler = new JwtSecurityTokenHandler();

        // The token must validate against the configured secret/issuer/audience.
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero,
        };

        var principal = handler.ValidateToken(
            result.Token, validationParameters, out var validatedToken);

        var jwt = Assert.IsType<JwtSecurityToken>(validatedToken);

        // Signed with HS256.
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);

        // sub — the admin user's id.
        var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Assert.Equal(repo.User.Id.ToString(), sub);

        // username — the custom login-name claim.
        Assert.Equal(Username, principal.FindFirst("username")?.Value);

        // jti — a non-empty unique token identifier.
        var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
        Assert.False(string.IsNullOrEmpty(jti));

        // iat — issued-at matches the controlled clock.
        var iat = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Iat)?.Value;
        Assert.NotNull(iat);
        var iatSeconds = long.Parse(iat!);
        Assert.Equal(start.ToUnixTimeSeconds(), iatSeconds);

        // exp ~ iat + 60 minutes.
        var expSeconds = ((DateTimeOffset)jwt.ValidTo).ToUnixTimeSeconds();
        Assert.Equal(iatSeconds + ExpiryMinutes * 60, expSeconds);
    }
}
