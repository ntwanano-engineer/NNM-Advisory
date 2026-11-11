using CsCheck;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using NnmAdvisory.Api.Infrastructure;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based tests for leadership biography length / blank validation.
///
/// <para><b>Property 4: Biography Length Validation</b></para>
/// <para>
/// For any biography that violates the field rules — longer than 2000
/// characters, or whitespace-only — a call to
/// <see cref="CompanyProfileService.UpdateLeadershipBiographyAsync"/> returns a
/// <see cref="ProfileUpdateOutcome.ValidationFailed"/> result (with a
/// <c>biography</c> error) and nothing is persisted: re-querying the leadership
/// profile shows its biography is byte-for-byte identical to the seeded
/// original.
/// </para>
///
/// <para><b>Validates: Requirements 4.7, 4.8</b></para>
///
/// <para>
/// The test wires up the real collaborators — a real
/// <see cref="CompanyProfileRepository"/> over an in-memory
/// <see cref="NnmDbContext"/> and a real <see cref="MemoryCache"/> — so the
/// validation-then-persist path is exercised end to end rather than simulated.
/// </para>
/// </summary>
public sealed class BiographyLengthValidationTests
{
    /// <summary>The biography length limit enforced by the service / schema.</summary>
    private const int MaxBiographyLength = CompanyProfileService.MaxBiographyLength; // 2000

    /// <summary>The known-good biography seeded before each invalid attempt.</summary>
    private const string SeededBiography =
        "Seasoned advisory professional with two decades of experience.";

    // ------------------------------------------------------------------
    // Generators constrained to the INVALID biography input space.
    // ------------------------------------------------------------------

    /// <summary>
    /// Over-length biographies: non-blank text strictly longer than the 2000
    /// character limit (2001 … 2200 chars). Visible characters guarantee the
    /// value is rejected for length rather than for being blank.
    /// </summary>
    private static readonly Gen<string> GenTooLongBiography =
        Gen.Char['A', 'z']
           .Array[MaxBiographyLength + 1, MaxBiographyLength + 200]
           .Select(chars => new string(chars));

    /// <summary>
    /// Whitespace-only biographies: 1–50 characters drawn exclusively from a
    /// pool of whitespace code points (space, tab, newline, carriage return,
    /// vertical tab, form feed). Every value is non-empty yet blank.
    /// </summary>
    private static readonly Gen<string> GenWhitespaceOnlyBiography =
        Gen.OneOfConst(' ', '\t', '\n', '\r', '\v', '\f')
           .Array[1, 50]
           .Select(chars => new string(chars));

    /// <summary>
    /// The union of the two invalidity classes the property covers, so a single
    /// <see cref="Gen{T}.Sample"/> run exercises both over-length and
    /// whitespace-only biographies.
    /// </summary>
    private static readonly Gen<string> GenInvalidBiography =
        Gen.OneOf(GenTooLongBiography, GenWhitespaceOnlyBiography);

    // ------------------------------------------------------------------
    // Test-infrastructure helpers.
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a fresh in-memory <see cref="NnmDbContext"/> isolated to a unique
    /// database name so iterations never bleed into one another. The InMemory
    /// provider does not support explicit transactions (which the real
    /// repository opens), so the corresponding warning is suppressed.
    /// </summary>
    private static NnmDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<NnmDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new NnmDbContext(options);
    }

    /// <summary>
    /// Seeds a single <see cref="LeadershipProfile"/> carrying
    /// <see cref="SeededBiography"/> and returns its primary key.
    /// </summary>
    private static Guid SeedLeadershipProfile(NnmDbContext context)
    {
        var profile = new LeadershipProfile
        {
            Id = Guid.NewGuid(),
            FullName = "Ntwanano Mabasa",
            Title = "Chief Operating Officer (COO)",
            Biography = SeededBiography,
            DisplayOrder = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        context.LeadershipProfiles.Add(profile);
        context.SaveChanges();
        return profile.Id;
    }

    // ------------------------------------------------------------------
    // Property 4 — invalid biographies are rejected and never persisted.
    // ------------------------------------------------------------------

    [Fact]
    public void InvalidBiography_IsRejected_AndLeavesStoredBiographyUnchanged()
    {
        GenInvalidBiography.Sample(invalidBiography =>
        {
            // (CsCheck runs at least 100 iterations — see the iter argument below.)
            // A unique store per iteration keeps iterations fully isolated.
            var databaseName = $"biography-validation-{Guid.NewGuid()}";

            using var context = CreateContext(databaseName);
            var seededId = SeedLeadershipProfile(context);

            var repository = new CompanyProfileRepository(context);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var service = new CompanyProfileService(repository, cache);

            // Act: attempt the update with an invalid biography.
            var result = service
                .UpdateLeadershipBiographyAsync(seededId, invalidBiography)
                .GetAwaiter()
                .GetResult();

            // The API must report a validation failure, not a successful write
            // (Requirements 4.7, 4.8).
            Assert.Equal(ProfileUpdateOutcome.ValidationFailed, result.Outcome);
            Assert.Null(result.Value);
            Assert.True(
                result.Errors.ContainsKey("biography"),
                "a validation error should be reported against the 'biography' field");

            // Nothing was persisted: the stored biography is byte-for-byte the
            // seeded original. Query through a clean context bound to the same
            // store so no identity-map state can mask a stray write.
            using var verifyContext = CreateContext(databaseName);
            var stored = verifyContext.LeadershipProfiles
                                      .AsNoTracking()
                                      .Single(l => l.Id == seededId);

            Assert.Equal(SeededBiography, stored.Biography);
        },
        iter: 100);
    }
}
