using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Repositories;

/// <summary>
/// Data-access contract for the <see cref="AdminUser"/> entity.
///
/// All queries execute as parameterised SQL through EF Core (Requirement 11.3).
/// The repository supports the admin authentication flow (Requirement 8):
/// looking up a user by username and persisting lockout / failed-attempt state.
/// </summary>
public interface IAdminUserRepository
{
    /// <summary>
    /// Returns the <see cref="AdminUser"/> whose <c>username</c> matches
    /// <paramref name="username"/>, or <c>null</c> if no such user exists.
    ///
    /// The returned entity is change-tracked so the caller can mutate lockout
    /// and failed-attempt fields and persist them via
    /// <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<AdminUser?> GetByUsernameAsync(string username);

    /// <summary>
    /// Persists any pending changes to tracked <see cref="AdminUser"/> entities
    /// (e.g. updated <c>FailedLoginAttempts</c> or <c>LockedUntil</c>).
    /// </summary>
    Task SaveChangesAsync();
}
