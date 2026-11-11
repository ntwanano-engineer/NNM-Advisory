using Microsoft.EntityFrameworkCore;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICompanyProfileRepository"/>.
///
/// Every query is compiled to a parameterised SQL statement by EF Core,
/// satisfying Requirement 11.3 (no raw string interpolation into SQL).
///
/// The repository is registered as a scoped service so that each HTTP request
/// receives its own <see cref="NnmDbContext"/> instance — the standard EF Core
/// lifetime pattern.  (Requirement 10.1)
/// </summary>
public sealed class CompanyProfileRepository : ICompanyProfileRepository
{
    private readonly NnmDbContext _db;

    public CompanyProfileRepository(NnmDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------------
    // CompanyContent
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CompanyContent>> GetAllContentAsync()
    {
        return await _db.CompanyContents
            .AsNoTracking()
            .OrderBy(c => c.ContentKey)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<CompanyContent?> GetContentByKeyAsync(string key)
    {
        // EF Core translates this to a parameterised WHERE clause;
        // the string comparison is case-insensitive on PostgreSQL by default
        // for the collation used on the column.
        return await _db.CompanyContents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ContentKey == key);
    }

    /// <inheritdoc/>
    public async Task<CompanyContent?> UpdateContentAsync(string key, string contentValue)
    {
        // The row is tracked (no AsNoTracking) so EF Core can detect the change
        // and emit a parameterised UPDATE. All work runs inside an explicit
        // transaction that rolls back on any failure (Requirement 11.5).
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var entity = await _db.CompanyContents
                .FirstOrDefaultAsync(c => c.ContentKey == key);

            if (entity is null)
            {
                await transaction.RollbackAsync();
                return null;
            }

            entity.ContentValue = contentValue;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return entity;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // ServiceBlock
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServiceBlock>> GetAllServiceBlocksAsync()
    {
        return await _db.ServiceBlocks
            .AsNoTracking()
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.Category)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<ServiceBlock?> UpdateServiceBlockAsync(
        Guid id,
        string title,
        string description,
        string[] offerings)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var entity = await _db.ServiceBlocks
                .FirstOrDefaultAsync(s => s.Id == id);

            if (entity is null)
            {
                await transaction.RollbackAsync();
                return null;
            }

            entity.Title = title;
            entity.Description = description;
            entity.Offerings = offerings;
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return entity;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // LeadershipProfile
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LeadershipProfile>> GetAllLeadershipProfilesAsync()
    {
        return await _db.LeadershipProfiles
            .AsNoTracking()
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.FullName)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<LeadershipProfile?> UpdateLeadershipProfileAsync(
        Guid id,
        string biography,
        string? photoS3Key)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var entity = await _db.LeadershipProfiles
                .FirstOrDefaultAsync(l => l.Id == id);

            if (entity is null)
            {
                await transaction.RollbackAsync();
                return null;
            }

            entity.Biography = biography;

            // A null photo key means "leave the existing photo unchanged";
            // an empty string is treated as an explicit value and stored.
            if (photoS3Key is not null)
            {
                entity.PhotoS3Key = photoS3Key;
            }

            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return entity;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
