using Microsoft.Extensions.Caching.Memory;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;

namespace NnmAdvisory.Api.Services;

/// <summary>
/// Default implementation of <see cref="ICompanyProfileService"/>.
///
/// Wraps <see cref="ICompanyProfileRepository"/> and caches the three read
/// operations in a shared <see cref="IMemoryCache"/> with a 5-second TTL
/// (Requirement 3.8). A second read within the TTL window returns the cached
/// value and never touches the repository; once the entry expires the next
/// read repopulates it from the database.
///
/// <para>
/// Lifetime: registered as <c>Scoped</c> so it can depend on the scoped
/// repository / <see cref="NnmDbContext"/>. The <see cref="IMemoryCache"/> it
/// consumes is a process-wide singleton, so cache entries survive across
/// requests and the TTL is measured in wall-clock time rather than per-request.
/// </para>
/// </summary>
public sealed class CompanyProfileService : ICompanyProfileService
{
    /// <summary>
    /// The time-to-live applied to every cached read (Requirement 3.8).
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    /// <summary>The content key under which the tagline is stored.</summary>
    public const string TaglineContentKey = "tagline";

    /// <summary>Tagline length limit (Requirement 9.1).</summary>
    public const int MaxTaglineLength = 150;

    /// <summary>Service title length limit (Requirement 9.1).</summary>
    public const int MaxServiceTitleLength = 100;

    /// <summary>Service description length limit (Requirement 9.1).</summary>
    public const int MaxServiceDescriptionLength = 500;

    /// <summary>Leadership biography length limit (Requirements 4.7, 4.8).</summary>
    public const int MaxBiographyLength = 2000;

    // Stable cache keys for the three read operations. Keeping them private and
    // constant avoids accidental collisions with other cache consumers.
    private const string ProfileCacheKey = "company-profile:content";
    private const string ServicesCacheKey = "company-profile:services";
    private const string LeadershipCacheKey = "company-profile:leadership";

    private readonly ICompanyProfileRepository _repository;
    private readonly IMemoryCache _cache;

    public CompanyProfileService(ICompanyProfileRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CompanyContent>> GetProfileAsync() =>
        GetOrAddAsync(ProfileCacheKey, _repository.GetAllContentAsync);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ServiceBlock>> GetServicesAsync() =>
        GetOrAddAsync(ServicesCacheKey, _repository.GetAllServiceBlocksAsync);

    /// <inheritdoc/>
    public Task<IReadOnlyList<LeadershipProfile>> GetLeadershipAsync() =>
        GetOrAddAsync(LeadershipCacheKey, _repository.GetAllLeadershipProfilesAsync);

    /// <summary>
    /// Returns the cached value for <paramref name="cacheKey"/> when present and
    /// unexpired; otherwise invokes <paramref name="factory"/> (a repository
    /// read), caches the result with the 5-second absolute TTL, and returns it.
    /// </summary>
    private async Task<IReadOnlyList<T>> GetOrAddAsync<T>(
        string cacheKey,
        Func<Task<IReadOnlyList<T>>> factory)
    {
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<T>? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory();

        _cache.Set(
            cacheKey,
            value,
            new MemoryCacheEntryOptions
            {
                // Absolute expiration relative to now gives a true 5-second TTL:
                // the entry is evicted 5 seconds after it was written, regardless
                // of how often it is read in the meantime.
                AbsoluteExpirationRelativeToNow = CacheTtl
            });

        return value;
    }

    // -----------------------------------------------------------------------
    // Updates (Requirements 2.4, 3.6, 4.7, 4.8, 9.1, 9.2)
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<ProfileUpdateResult<CompanyContent>> UpdateTaglineAsync(string tagline)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateRequiredText(tagline, "tagline", MaxTaglineLength, errors);

        if (errors.Count > 0)
        {
            return ProfileUpdateResult<CompanyContent>.ValidationFailed(errors);
        }

        var updated = await _repository.UpdateContentAsync(TaglineContentKey, tagline.Trim());
        if (updated is null)
        {
            return ProfileUpdateResult<CompanyContent>.NotFound();
        }

        // The write succeeded: drop the stale profile content so the next read
        // repopulates it with the new tagline (Requirements 2.4, 9.2).
        _cache.Remove(ProfileCacheKey);

        return ProfileUpdateResult<CompanyContent>.Updated(updated);
    }

    /// <inheritdoc/>
    public async Task<ProfileUpdateResult<ServiceBlock>> UpdateServiceBlockAsync(
        Guid id,
        string title,
        string description,
        string[] offerings)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateRequiredText(title, "title", MaxServiceTitleLength, errors);
        ValidateRequiredText(description, "description", MaxServiceDescriptionLength, errors);

        if (errors.Count > 0)
        {
            return ProfileUpdateResult<ServiceBlock>.ValidationFailed(errors);
        }

        var updated = await _repository.UpdateServiceBlockAsync(
            id,
            title.Trim(),
            description.Trim(),
            offerings ?? Array.Empty<string>());

        if (updated is null)
        {
            return ProfileUpdateResult<ServiceBlock>.NotFound();
        }

        // The write succeeded: drop the stale services list so the next read
        // reflects the change (Requirements 3.6, 9.2).
        _cache.Remove(ServicesCacheKey);

        return ProfileUpdateResult<ServiceBlock>.Updated(updated);
    }

    /// <inheritdoc/>
    public async Task<ProfileUpdateResult<LeadershipProfile>> UpdateLeadershipBiographyAsync(
        Guid id,
        string biography,
        string? photoS3Key = null)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateRequiredText(biography, "biography", MaxBiographyLength, errors);

        if (errors.Count > 0)
        {
            return ProfileUpdateResult<LeadershipProfile>.ValidationFailed(errors);
        }

        var updated = await _repository.UpdateLeadershipProfileAsync(
            id,
            biography.Trim(),
            photoS3Key);

        if (updated is null)
        {
            return ProfileUpdateResult<LeadershipProfile>.NotFound();
        }

        // The write succeeded: drop the stale leadership list so the next read
        // reflects the change (Requirement 9.2).
        _cache.Remove(LeadershipCacheKey);

        return ProfileUpdateResult<LeadershipProfile>.Updated(updated);
    }

    /// <summary>
    /// Validates a required free-text field: it must be present, not
    /// whitespace-only, and at most <paramref name="maxLength"/> characters.
    /// Any failure appends a message under <paramref name="fieldName"/> in
    /// <paramref name="errors"/>. Length is measured against the trimmed value
    /// so leading / trailing whitespace never pushes a value over the limit.
    /// </summary>
    private static void ValidateRequiredText(
        string? value,
        string fieldName,
        int maxLength,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[fieldName] = new[] { $"{fieldName} is required and cannot be blank." };
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[fieldName] = new[] { $"{fieldName} must be at most {maxLength} characters." };
        }
    }
}
