using Microsoft.EntityFrameworkCore;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAdminUserRepository"/>.
///
/// Every query is compiled to a parameterised SQL statement by EF Core,
/// satisfying Requirement 11.3 (no raw string interpolation into SQL).
///
/// Registered as a scoped service so each HTTP request receives its own
/// <see cref="NnmDbContext"/> instance.
/// </summary>
public sealed class AdminUserRepository : IAdminUserRepository
{
    private readonly NnmDbContext _db;

    public AdminUserRepository(NnmDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<AdminUser?> GetByUsernameAsync(string username)
    {
        // Change-tracked (no AsNoTracking) so the AuthService can update
        // FailedLoginAttempts / LockedUntil on the returned entity.
        return await _db.AdminUsers
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
