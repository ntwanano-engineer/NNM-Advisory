using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;

namespace NnmAdvisory.Api.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IContactRepository"/>.
///
/// The insert is compiled to a parameterised SQL statement by EF Core,
/// satisfying Requirement 11.3 (no raw string interpolation into SQL).
///
/// EF Core's <c>SaveChangesAsync</c> wraps the write in an implicit
/// transaction: if the statement fails (constraint violation, system error,
/// or lost connection) the transaction is rolled back automatically, the
/// tracked entity is not persisted, and the exception propagates to the
/// caller — satisfying Requirement 11.5 (roll back the entire transaction,
/// leave all affected rows unchanged, surface an error).
///
/// Registered as a scoped service so each HTTP request receives its own
/// <see cref="NnmDbContext"/> instance — the standard EF Core lifetime
/// pattern. (Requirement 10.1)
/// </summary>
public sealed class ContactRepository : IContactRepository
{
    private readonly NnmDbContext _db;

    public ContactRepository(NnmDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<ContactSubmission> AddAsync(
        ContactSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        // Stamp the creation time as UTC at insert time (Requirement 11.6).
        // This is set explicitly here rather than relying solely on the
        // database default so the persisted entity returned to the caller
        // carries an accurate timestamp.
        submission.SubmittedAt = DateTimeOffset.UtcNow;

        _db.ContactSubmissions.Add(submission);

        // SaveChangesAsync executes the INSERT inside an implicit transaction.
        // On failure EF Core rolls it back and rethrows, leaving no row
        // persisted (Requirement 11.5).
        await _db.SaveChangesAsync(cancellationToken);

        return submission;
    }
}
