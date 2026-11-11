using CsCheck;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based test for the admin authentication lockout invariant.
///
/// <para><b>Property 3: Admin Authentication Lockout Invariant</b></para>
/// <para>
/// After an account accrues 5 or more consecutive failed login attempts it is
/// locked for 15 minutes. While that lockout window is active, EVERY subsequent
/// attempt — whether the credentials are correct or incorrect — returns the
/// <see cref="AuthOutcome.AccountLocked"/> outcome. Correct credentials do not
/// unlock the account early (Requirement 8.5).
/// </para>
///
/// <para><b>Validates: Requirements 8.5</b></para>
/// </summary>
public sealed class AuthenticationLockoutTests
{
    private const string Username = "admin";
    private const string CorrectPassword = "correct-horse-battery-staple";
    private const string WrongPassword = "definitely-not-the-password";

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
        private readonly AdminUser _user;

        public InMemoryAdminUserRepository(AdminUser user) => _user = user;

        public Task<AdminUser?> GetByUsernameAsync(string username) =>
            Task.FromResult(
                string.Equals(username, _user.Username, StringComparison.Ordinal)
                    ? _user
                    : null);

        public Task SaveChangesAsync() => Task.CompletedTask;
    }

    /// <summary>Valid JWT settings so token issuance on success never throws.</summary>
    private static IOptions<JwtSettings> JwtOptions() =>
        Options.Create(new JwtSettings
        {
            Secret = "this-is-a-sufficiently-long-test-secret-key-123456",
            Issuer = "NnmAdvisory",
            Audience = "NnmAdvisoryAdmin",
            ExpiryMinutes = 60,
        });

    private static (AuthService service, FakeTimeProvider clock) CreateService(DateTimeOffset start)
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
        var service = new AuthService(
            new InMemoryAdminUserRepository(user),
            JwtOptions(),
            NullLogger<AuthService>.Instance,
            clock);

        return (service, clock);
    }

    // ------------------------------------------------------------------
    // Property 3 — once locked, every attempt within 15 minutes is rejected
    // as AccountLocked regardless of credential correctness.
    // ------------------------------------------------------------------

    [Fact]
    public void Lockout_RejectsAllAttemptsWithinWindow_RegardlessOfCredentials()
    {
        // Generator: number of initial failed attempts in [5, 50].
        var genFailedAttempts = Gen.Int[5, 50];

        // Generator: a sequence of 1–20 post-lockout probes. Each probe pairs a
        // credential choice (correct vs. wrong password) with an elapsed offset
        // (in seconds) that stays strictly inside the 15-minute window.
        var genProbe = Gen.Select(
            Gen.Bool,                                   // true => use CORRECT password
            Gen.Int[0, (15 * 60) - 1],                  // seconds since lockout start (< 900)
            (useCorrect, seconds) => (useCorrect, seconds));
        var genProbes = genProbe.List[1, 20];

        Gen.Select(genFailedAttempts, genProbes)
           .Sample(tuple =>
           {
               var (failedAttempts, probes) = tuple;

               var start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
               var (service, clock) = CreateService(start);

               // Drive the account into the locked state with N wrong-password
               // attempts. The Nth attempt (N >= 5) must report AccountLocked.
               AuthResult lastFailure = null!;
               for (var i = 0; i < failedAttempts; i++)
               {
                   lastFailure = service.AuthenticateAsync(Username, WrongPassword)
                                        .GetAwaiter().GetResult();
               }

               Assert.Equal(AuthOutcome.AccountLocked, lastFailure.Outcome);
               Assert.NotNull(lastFailure.LockedUntil);

               // The unlock time is exactly 15 minutes after the 5th failure,
               // which occurred at `start` (the clock has not advanced yet).
               var expectedUnlock = start.AddMinutes(15);
               Assert.Equal(expectedUnlock, lastFailure.LockedUntil);

               // Every probe inside the window must be rejected as locked,
               // whether it supplies the correct or an incorrect password.
               foreach (var (useCorrect, seconds) in probes)
               {
                   // Place the clock at `start + seconds` (always < 900s < unlock).
                   clock.Advance(TimeSpan.FromSeconds(seconds) - (clock.GetUtcNow() - start));

                   Assert.True(clock.GetUtcNow() < expectedUnlock);

                   var password = useCorrect ? CorrectPassword : WrongPassword;
                   var result = service.AuthenticateAsync(Username, password)
                                       .GetAwaiter().GetResult();

                   Assert.Equal(AuthOutcome.AccountLocked, result.Outcome);
                   Assert.Equal(expectedUnlock, result.LockedUntil);
               }
           });
    }
}
