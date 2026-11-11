using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Default implementation of <see cref="IAuthService"/>.
///
/// Enforces the admin authentication and lockout policy (Requirement 8) and
/// issues HS256 JSON Web Tokens signed with the secret from
/// <see cref="JwtSettings"/>.
///
/// Registered as a scoped service (shares the request-scoped
/// <see cref="IAdminUserRepository"/> / DbContext lifetime).
/// </summary>
public sealed class AuthService : IAuthService
{
    /// <summary>Consecutive failures that trigger a lockout (Requirement 8.5).</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Duration an account stays locked (Requirement 8.5).</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IAdminUserRepository _adminUsers;
    private readonly JwtSettings _jwt;
    private readonly ILogger<AuthService> _logger;
    private readonly TimeProvider _timeProvider;

    public AuthService(
        IAdminUserRepository adminUsers,
        IOptions<JwtSettings> jwtOptions,
        ILogger<AuthService> logger,
        TimeProvider? timeProvider = null)
    {
        _adminUsers = adminUsers;
        _jwt = jwtOptions.Value;
        _logger = logger;
        // TimeProvider is injectable so unit tests can advance the clock to
        // verify lockout release after 15 minutes (Requirement 8.5).
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<AuthResult> AuthenticateAsync(string username, string password)
    {
        var now = _timeProvider.GetUtcNow();

        var user = await _adminUsers.GetByUsernameAsync(username ?? string.Empty);

        // Unknown username — return the generic failure outcome WITHOUT
        // disclosing that the username did not exist (Requirement 10.7).
        // A bcrypt verify is intentionally skipped here; timing is not a
        // disclosure vector because the response shape is identical.
        if (user is null)
        {
            _logger.LogWarning("Login failed: unknown username.");
            return AuthResult.InvalidCredentials();
        }

        // 1. Account currently locked? Reject regardless of credential
        //    correctness while the lockout window is active (Requirement 8.5).
        if (user.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            _logger.LogWarning(
                "Login rejected: account locked until {LockedUntil:O}.", lockedUntil);
            return AuthResult.Locked(lockedUntil);
        }

        // 2. Verify the password against the stored bcrypt hash (Requirement 8.7).
        bool passwordValid;
        try
        {
            passwordValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed/non-bcrypt stored hash should fail closed rather than
            // throw. Treat as an invalid credential outcome.
            _logger.LogError("Stored password hash for an admin user is not a valid bcrypt hash.");
            passwordValid = false;
        }

        if (!passwordValid)
        {
            return await RegisterFailedAttemptAsync(user, now);
        }

        // 3. Success — reset the failure counter and clear any stale lockout
        //    (Requirement 8.5), then issue a token (Requirement 8.1).
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        await _adminUsers.SaveChangesAsync();

        var token = IssueToken(user, now);
        _logger.LogInformation("Admin login succeeded for user {UserId}.", user.Id);
        return AuthResult.Success(token);
    }

    /// <summary>
    /// Increments the consecutive-failure counter and applies a 15-minute
    /// lockout once the threshold is reached (Requirement 8.5).
    /// </summary>
    private async Task<AuthResult> RegisterFailedAttemptAsync(AdminUser user, DateTimeOffset now)
    {
        user.FailedLoginAttempts += 1;
        user.UpdatedAt = now;

        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            var lockedUntil = now.Add(LockoutDuration);
            user.LockedUntil = lockedUntil;
            await _adminUsers.SaveChangesAsync();

            _logger.LogWarning(
                "Account locked after {Attempts} consecutive failed attempts; unlocks at {LockedUntil:O}.",
                user.FailedLoginAttempts, lockedUntil);
            return AuthResult.Locked(lockedUntil);
        }

        await _adminUsers.SaveChangesAsync();
        return AuthResult.InvalidCredentials();
    }

    /// <summary>
    /// Builds and signs an HS256 JWT with a 60-minute expiry and the claims
    /// <c>sub</c>, <c>username</c>, <c>iat</c>, <c>exp</c>, and <c>jti</c>.
    /// </summary>
    private string IssueToken(AdminUser user, DateTimeOffset issuedAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expires = issuedAt.Add(TimeSpan.FromMinutes(_jwt.ExpiryMinutes));

        var claims = new List<Claim>
        {
            // sub — subject: the admin user's UUID.
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            // username — custom claim carrying the login name.
            new("username", user.Username),
            // jti — unique token identifier.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // iat — issued-at, as Unix seconds.
            new(JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            // exp — expiry is derived from notBefore/expires by the handler.
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
