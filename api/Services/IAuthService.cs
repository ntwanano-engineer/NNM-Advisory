namespace NnmAdvisory.Api.Services;

/// <summary>
/// Discriminates the possible outcomes of an admin login attempt.
/// The controller (Task 9.3) maps each outcome to an HTTP status code.
/// </summary>
public enum AuthOutcome
{
    /// <summary>Credentials were valid; a JWT was issued.</summary>
    Success,

    /// <summary>
    /// Credentials were invalid. The caller MUST NOT reveal whether the
    /// username or the password was wrong (Requirement 10.7).
    /// </summary>
    InvalidCredentials,

    /// <summary>
    /// The account is currently locked following 5 or more consecutive failed
    /// attempts. <see cref="AuthResult.LockedUntil"/> indicates when it unlocks
    /// (Requirement 8.5).
    /// </summary>
    AccountLocked
}

/// <summary>
/// The result of an authentication attempt.
///
/// <list type="bullet">
///   <item><see cref="AuthOutcome.Success"/> — <see cref="Token"/> is populated.</item>
///   <item><see cref="AuthOutcome.AccountLocked"/> — <see cref="LockedUntil"/> is populated.</item>
///   <item><see cref="AuthOutcome.InvalidCredentials"/> — both are <c>null</c>.</item>
/// </list>
/// </summary>
public sealed record AuthResult(
    AuthOutcome Outcome,
    string? Token = null,
    DateTimeOffset? LockedUntil = null)
{
    public static AuthResult Success(string token) => new(AuthOutcome.Success, Token: token);

    public static AuthResult InvalidCredentials() => new(AuthOutcome.InvalidCredentials);

    public static AuthResult Locked(DateTimeOffset lockedUntil) =>
        new(AuthOutcome.AccountLocked, LockedUntil: lockedUntil);
}

/// <summary>
/// Authenticates admin users and enforces the account-lockout policy.
///
/// Responsibilities (Requirements 8.1, 8.4, 8.5, 8.6, 8.7):
/// <list type="bullet">
///   <item>Validate the supplied password against the stored bcrypt hash.</item>
///   <item>Increment the consecutive failed-attempt counter on failure.</item>
///   <item>Lock the account for 15 minutes after 5 consecutive failures.</item>
///   <item>Reset the counter and issue an HS256 JWT (60-minute expiry) on success.</item>
/// </list>
///
/// The service never discloses whether the username or password was wrong —
/// both conditions yield <see cref="AuthOutcome.InvalidCredentials"/>.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Attempts to authenticate the supplied credentials.
    /// </summary>
    /// <param name="username">The submitted admin username.</param>
    /// <param name="password">The submitted plaintext password.</param>
    /// <returns>
    /// An <see cref="AuthResult"/> describing the outcome. On success it carries
    /// a signed JWT; on lockout it carries the UTC unlock time.
    /// </returns>
    Task<AuthResult> AuthenticateAsync(string username, string password);
}
