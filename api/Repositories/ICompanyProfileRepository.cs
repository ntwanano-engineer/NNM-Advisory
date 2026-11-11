using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Repositories;

/// <summary>
/// Read-only data-access contract for the three company profile entities:
/// <see cref="CompanyContent"/>, <see cref="ServiceBlock"/>, and
/// <see cref="LeadershipProfile"/>.
///
/// All queries execute as parameterised SQL through EF Core (Requirement 11.3)
/// and are kept in a dedicated repository layer so the service layer stays
/// free of EF Core / infrastructure concerns (Requirement 10.1).
///
/// Update methods that support admin content management (Requirement 9.2)
/// wrap their writes in an explicit EF Core transaction so that any failure
/// rolls the entire operation back and leaves all affected rows unchanged
/// (Requirement 11.5).
/// </summary>
public interface ICompanyProfileRepository
{
    // -----------------------------------------------------------------------
    // CompanyContent
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns every row in the <c>company_content</c> table ordered by
    /// <c>content_key</c> ascending.
    /// </summary>
    Task<IReadOnlyList<CompanyContent>> GetAllContentAsync();

    /// <summary>
    /// Returns the <see cref="CompanyContent"/> row whose <c>content_key</c>
    /// matches <paramref name="key"/> (case-insensitive), or <c>null</c> if
    /// no such row exists.
    /// </summary>
    /// <param name="key">
    /// The logical content identifier, e.g. <c>"tagline"</c> or
    /// <c>"hero_cta_label"</c>.
    /// </param>
    Task<CompanyContent?> GetContentByKeyAsync(string key);

    /// <summary>
    /// Updates the <c>content_value</c> and <c>updated_at</c> columns of the
    /// <see cref="CompanyContent"/> row whose <c>content_key</c> matches
    /// <paramref name="key"/>. The write is wrapped in an explicit transaction
    /// that is rolled back if persistence fails (Requirement 11.5).
    /// </summary>
    /// <param name="key">The logical content identifier to update.</param>
    /// <param name="contentValue">The new value to store.</param>
    /// <returns>
    /// The updated <see cref="CompanyContent"/> row, or <c>null</c> if no row
    /// with the given <paramref name="key"/> exists.
    /// </returns>
    Task<CompanyContent?> UpdateContentAsync(string key, string contentValue);

    // -----------------------------------------------------------------------
    // ServiceBlock
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns every row in the <c>service_blocks</c> table ordered by
    /// <c>display_order</c> ascending, then <c>category</c> ascending.
    /// </summary>
    Task<IReadOnlyList<ServiceBlock>> GetAllServiceBlocksAsync();

    /// <summary>
    /// Updates the <c>title</c>, <c>description</c>, <c>offerings</c>, and
    /// <c>updated_at</c> columns of the <see cref="ServiceBlock"/> identified by
    /// <paramref name="id"/>. The write is wrapped in an explicit transaction
    /// that is rolled back if persistence fails (Requirement 11.5).
    /// </summary>
    /// <param name="id">Primary key of the service block to update.</param>
    /// <param name="title">The new service title.</param>
    /// <param name="description">The new service description.</param>
    /// <param name="offerings">The new list of service offerings.</param>
    /// <returns>
    /// The updated <see cref="ServiceBlock"/>, or <c>null</c> if no row with the
    /// given <paramref name="id"/> exists.
    /// </returns>
    Task<ServiceBlock?> UpdateServiceBlockAsync(
        Guid id,
        string title,
        string description,
        string[] offerings);

    // -----------------------------------------------------------------------
    // LeadershipProfile
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns every row in the <c>leadership_profiles</c> table ordered by
    /// <c>display_order</c> ascending, then <c>full_name</c> ascending.
    /// </summary>
    Task<IReadOnlyList<LeadershipProfile>> GetAllLeadershipProfilesAsync();

    /// <summary>
    /// Updates the <c>biography</c>, optional <c>photo_s3_key</c>, and
    /// <c>updated_at</c> columns of the <see cref="LeadershipProfile"/>
    /// identified by <paramref name="id"/>. The write is wrapped in an explicit
    /// transaction that is rolled back if persistence fails (Requirement 11.5).
    /// </summary>
    /// <param name="id">Primary key of the leadership profile to update.</param>
    /// <param name="biography">The new biographical text.</param>
    /// <param name="photoS3Key">
    /// The new S3 object key for the profile photograph. Pass <c>null</c> to
    /// leave the existing photo key unchanged.
    /// </param>
    /// <returns>
    /// The updated <see cref="LeadershipProfile"/>, or <c>null</c> if no row
    /// with the given <paramref name="id"/> exists.
    /// </returns>
    Task<LeadershipProfile?> UpdateLeadershipProfileAsync(
        Guid id,
        string biography,
        string? photoS3Key);
}
