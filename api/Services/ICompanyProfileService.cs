using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Discriminates the possible outcomes of a company profile update attempt.
/// The admin content controller (Task 10.3) maps each outcome to an HTTP
/// status code:
/// <list type="bullet">
///   <item><see cref="ProfileUpdateOutcome.Updated"/> → HTTP 200 OK.</item>
///   <item><see cref="ProfileUpdateOutcome.ValidationFailed"/> → HTTP 422 Unprocessable Entity.</item>
///   <item><see cref="ProfileUpdateOutcome.NotFound"/> → HTTP 404 Not Found.</item>
/// </list>
/// Database / infrastructure failures are surfaced as exceptions and mapped to
/// HTTP 500 by the global exception handler.
/// </summary>
public enum ProfileUpdateOutcome
{
    /// <summary>
    /// The update passed field-level validation and was persisted; the relevant
    /// cache entry was invalidated so subsequent reads reflect the new value
    /// (Requirements 2.4, 3.6, 9.2).
    /// </summary>
    Updated,

    /// <summary>
    /// One or more fields failed validation; <see cref="ProfileUpdateResult{T}.Errors"/>
    /// identifies each invalid field by name (Requirements 4.7, 4.8, 9.1). Nothing
    /// was persisted.
    /// </summary>
    ValidationFailed,

    /// <summary>
    /// No entity matched the supplied identifier / content key, so there was
    /// nothing to update. Nothing was persisted.
    /// </summary>
    NotFound
}

/// <summary>
/// The result of a company profile update attempt.
///
/// <list type="bullet">
///   <item>
///     <see cref="ProfileUpdateOutcome.Updated"/> — the write succeeded;
///     <see cref="Value"/> carries the updated entity and <see cref="Errors"/>
///     is empty (HTTP 200).
///   </item>
///   <item>
///     <see cref="ProfileUpdateOutcome.ValidationFailed"/> — nothing was
///     persisted; <see cref="Errors"/> maps each invalid field name to its
///     error messages, ready to serialise as the <c>{ "errors": { ... } }</c>
///     body (HTTP 422).
///   </item>
///   <item>
///     <see cref="ProfileUpdateOutcome.NotFound"/> — no matching entity existed;
///     <see cref="Value"/> is <c>null</c> and <see cref="Errors"/> is empty
///     (HTTP 404).
///   </item>
/// </list>
/// </summary>
/// <typeparam name="T">The entity type returned on a successful update.</typeparam>
public sealed record ProfileUpdateResult<T>(
    ProfileUpdateOutcome Outcome,
    T? Value,
    IReadOnlyDictionary<string, string[]> Errors)
    where T : class
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>();

    /// <summary>The update succeeded and persisted (HTTP 200).</summary>
    public static ProfileUpdateResult<T> Updated(T value) =>
        new(ProfileUpdateOutcome.Updated, value, NoErrors);

    /// <summary>
    /// The update failed validation (HTTP 422). <paramref name="errors"/>
    /// identifies each invalid field by name.
    /// </summary>
    public static ProfileUpdateResult<T> ValidationFailed(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(ProfileUpdateOutcome.ValidationFailed, null, errors);

    /// <summary>No matching entity existed (HTTP 404).</summary>
    public static ProfileUpdateResult<T> NotFound() =>
        new(ProfileUpdateOutcome.NotFound, null, NoErrors);
}

/// <summary>
/// Business-logic contract for reading and updating company profile content.
///
/// This service sits between the controllers and the
/// <see cref="Repositories.ICompanyProfileRepository"/> data layer. It caches
/// the three read operations — profile content, service blocks, and leadership
/// profiles — in memory with a 5-second time-to-live (TTL).
///
/// <para>
/// Per the design document, responses are cached in memory with a 5-second TTL
/// (Requirement 3.8): a second read within the TTL window is served from the
/// cache without hitting the repository (and therefore without issuing a SQL
/// query). After the TTL expires, the next read repopulates the cache from the
/// repository.
/// </para>
///
/// <para>
/// The update methods validate their inputs, persist through the repository
/// when valid, and — on a successful write — invalidate the matching cache
/// entry so the next read reflects the change (Requirements 2.4, 3.6, 9.2).
/// </para>
/// </summary>
public interface ICompanyProfileService
{
    /// <summary>
    /// Returns every <see cref="CompanyContent"/> row. Served from the 5-second
    /// in-memory cache when a prior read populated it within the TTL window;
    /// otherwise read from the repository and cached.
    /// </summary>
    Task<IReadOnlyList<CompanyContent>> GetProfileAsync();

    /// <summary>
    /// Returns every <see cref="ServiceBlock"/> row. Served from the 5-second
    /// in-memory cache when a prior read populated it within the TTL window;
    /// otherwise read from the repository and cached.
    /// </summary>
    Task<IReadOnlyList<ServiceBlock>> GetServicesAsync();

    /// <summary>
    /// Returns every <see cref="LeadershipProfile"/> row. Served from the
    /// 5-second in-memory cache when a prior read populated it within the TTL
    /// window; otherwise read from the repository and cached.
    /// </summary>
    Task<IReadOnlyList<LeadershipProfile>> GetLeadershipAsync();

    /// <summary>
    /// Validates and, when valid, updates the company tagline (the
    /// <c>"tagline"</c> content key). The value must be non-blank and at most
    /// 150 characters (Requirement 9.1). On a successful write the profile
    /// content cache entry is invalidated so the next read reflects the new
    /// tagline (Requirements 2.4, 9.2).
    /// </summary>
    /// <param name="tagline">The new tagline value.</param>
    /// <returns>
    /// A <see cref="ProfileUpdateResult{CompanyContent}"/> describing the
    /// outcome: updated, validation-failed (per-field errors), or not-found
    /// (no <c>"tagline"</c> row exists).
    /// </returns>
    Task<ProfileUpdateResult<CompanyContent>> UpdateTaglineAsync(string tagline);

    /// <summary>
    /// Validates and, when valid, updates a service block's title, description,
    /// and offerings. The title must be non-blank and at most 100 characters and
    /// the description non-blank and at most 500 characters (Requirement 9.1). On
    /// a successful write the services cache entry is invalidated so the next
    /// read reflects the change (Requirements 3.6, 9.2).
    /// </summary>
    /// <param name="id">Primary key of the service block to update.</param>
    /// <param name="title">The new service title.</param>
    /// <param name="description">The new service description.</param>
    /// <param name="offerings">The new list of service offerings.</param>
    /// <returns>
    /// A <see cref="ProfileUpdateResult{ServiceBlock}"/> describing the outcome:
    /// updated, validation-failed (per-field errors), or not-found.
    /// </returns>
    Task<ProfileUpdateResult<ServiceBlock>> UpdateServiceBlockAsync(
        Guid id,
        string title,
        string description,
        string[] offerings);

    /// <summary>
    /// Validates and, when valid, updates a leadership profile's biography and,
    /// optionally, its photo S3 key. The biography must be non-blank and at most
    /// 2000 characters (Requirements 4.7, 4.8). On a successful write the
    /// leadership cache entry is invalidated so the next read reflects the change
    /// (Requirement 9.2).
    /// </summary>
    /// <param name="id">Primary key of the leadership profile to update.</param>
    /// <param name="biography">The new biographical text.</param>
    /// <param name="photoS3Key">
    /// The new S3 object key for the profile photograph. Pass <c>null</c> to
    /// leave the existing photo key unchanged.
    /// </param>
    /// <returns>
    /// A <see cref="ProfileUpdateResult{LeadershipProfile}"/> describing the
    /// outcome: updated, validation-failed (per-field errors), or not-found.
    /// </returns>
    Task<ProfileUpdateResult<LeadershipProfile>> UpdateLeadershipBiographyAsync(
        Guid id,
        string biography,
        string? photoS3Key = null);
}
