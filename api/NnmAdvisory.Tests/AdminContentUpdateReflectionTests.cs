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
/// Property-based tests for the admin content update → read reflection
/// guarantee across all three editable company-profile entities.
///
/// <para><b>Property 8: Admin Content Update Reflected in Subsequent Reads</b></para>
/// <para>
/// For any valid content value (tagline ≤ 150 non-blank; service title ≤ 100
/// and description ≤ 500 non-blank; biography ≤ 2000 non-blank), after the
/// corresponding update method succeeds, the matching read method returns the
/// updated value rather than the previously-seeded value.
/// </para>
/// <para>
/// Each case <b>primes the read cache before the update</b> so the assertion
/// only passes if the update invalidates the stale cache entry — a read served
/// from a stale cache would still return the old value and fail the test. This
/// exercises the cache-invalidation-on-write behaviour of
/// <see cref="CompanyProfileService"/>.
/// </para>
///
/// <para><b>Validates: Requirements 2.4, 3.6, 9.2</b></para>
/// </summary>
public sealed class AdminContentUpdateReflectionTests
{
    // Minimum property-test iterations mandated by the design specification.
    private const int Iterations = 100;

    // Stable identifiers used to seed and target the service/leadership rows.
    private static readonly Guid SeededServiceId = Guid.NewGuid();
    private static readonly Guid SeededLeadershipId = Guid.NewGuid();

    // ------------------------------------------------------------------
    // Generators constrained to the VALID content input space. All values
    // are non-blank (contain at least one non-whitespace character) and sit
    // within the service's length limits, which are measured against the
    // trimmed value. Using printable-ASCII characters keeps the strings
    // human-meaningful while covering the input space broadly.
    // ------------------------------------------------------------------

    private static readonly Gen<char> GenPrintable = Gen.Char['!', '~'];

    /// <summary>Tagline: 1–150 non-blank characters (Requirement 9.1).</summary>
    private static readonly Gen<string> GenTagline =
        GenPrintable.Array[1, CompanyProfileService.MaxTaglineLength]
                    .Select(c => new string(c));

    /// <summary>Service title: 1–100 non-blank characters (Requirement 9.1).</summary>
    private static readonly Gen<string> GenServiceTitle =
        GenPrintable.Array[1, CompanyProfileService.MaxServiceTitleLength]
                    .Select(c => new string(c));

    /// <summary>Service description: 1–500 non-blank characters (Requirement 9.1).</summary>
    private static readonly Gen<string> GenServiceDescription =
        GenPrintable.Array[1, CompanyProfileService.MaxServiceDescriptionLength]
                    .Select(c => new string(c));

    /// <summary>Biography: 1–2000 non-blank characters (Requirements 4.7, 4.8).</summary>
    private static readonly Gen<string> GenBiography =
        GenPrintable.Array[1, CompanyProfileService.MaxBiographyLength]
                    .Select(c => new string(c));

    // ------------------------------------------------------------------
    // Test rig: real service + real repository + real MemoryCache over a
    // fresh in-memory NnmDbContext seeded with one row per entity.
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds an isolated in-memory context. The InMemory provider does not
    /// support the explicit transactions the repository opens, so the
    /// <see cref="InMemoryEventId.TransactionIgnoredWarning"/> is downgraded
    /// from an exception to a no-op — the update still runs end-to-end.
    /// </summary>
    private static NnmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NnmDbContext>()
            .UseInMemoryDatabase($"admin-update-reflection-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new NnmDbContext(options);
    }

    /// <summary>
    /// Seeds one tagline content row, one service block, and one leadership
    /// profile with known "original" values so the property can prove the
    /// updated value replaces them.
    /// </summary>
    private static void Seed(NnmDbContext context)
    {
        context.CompanyContents.Add(new CompanyContent
        {
            Id = Guid.NewGuid(),
            ContentKey = CompanyProfileService.TaglineContentKey,
            ContentValue = "Original tagline",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        context.ServiceBlocks.Add(new ServiceBlock
        {
            Id = SeededServiceId,
            Category = "accounting",
            Title = "Original title",
            Description = "Original description",
            Offerings = new[] { "Original offering" },
            DisplayOrder = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        context.LeadershipProfiles.Add(new LeadershipProfile
        {
            Id = SeededLeadershipId,
            FullName = "Ntwanano Mabasa",
            Title = "Chief Operating Officer (COO)",
            Biography = "Original biography",
            DisplayOrder = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        context.SaveChanges();
    }

    private static CompanyProfileService CreateService(NnmDbContext context)
    {
        var repository = new CompanyProfileRepository(context);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new CompanyProfileService(repository, cache);
    }

    // ------------------------------------------------------------------
    // Property 8 — tagline update is reflected by the profile read.
    // ------------------------------------------------------------------

    [Fact]
    public void UpdatedTagline_IsReflected_ByGetProfileAsync()
    {
        GenTagline.Sample(newTagline =>
        {
            using var context = CreateContext();
            Seed(context);
            var service = CreateService(context);

            // Prime the cache: the next read must observe the invalidation.
            var original = service.GetProfileAsync().GetAwaiter().GetResult()
                                  .Single(c => c.ContentKey == CompanyProfileService.TaglineContentKey)
                                  .ContentValue;

            var result = service.UpdateTaglineAsync(newTagline).GetAwaiter().GetResult();
            Assert.Equal(ProfileUpdateOutcome.Updated, result.Outcome);

            var reflected = service.GetProfileAsync().GetAwaiter().GetResult()
                                   .Single(c => c.ContentKey == CompanyProfileService.TaglineContentKey)
                                   .ContentValue;

            // The read returns the new (trimmed) value, not the primed original.
            return reflected == newTagline.Trim() && reflected != original;
        }, iter: Iterations);
    }

    // ------------------------------------------------------------------
    // Property 8 — service block update is reflected by the services read.
    // ------------------------------------------------------------------

    [Fact]
    public void UpdatedServiceBlock_IsReflected_ByGetServicesAsync()
    {
        Gen.Select(GenServiceTitle, GenServiceDescription).Sample(pair =>
        {
            var (newTitle, newDescription) = pair;

            using var context = CreateContext();
            Seed(context);
            var service = CreateService(context);

            // Prime the cache.
            var original = service.GetServicesAsync().GetAwaiter().GetResult()
                                  .Single(s => s.Id == SeededServiceId);
            var originalTitle = original.Title;
            var originalDescription = original.Description;

            var result = service.UpdateServiceBlockAsync(
                SeededServiceId,
                newTitle,
                newDescription,
                new[] { "offering" }).GetAwaiter().GetResult();
            Assert.Equal(ProfileUpdateOutcome.Updated, result.Outcome);

            var reflected = service.GetServicesAsync().GetAwaiter().GetResult()
                                   .Single(s => s.Id == SeededServiceId);

            return reflected.Title == newTitle.Trim()
                   && reflected.Description == newDescription.Trim()
                   && reflected.Title != originalTitle
                   && reflected.Description != originalDescription;
        }, iter: Iterations);
    }

    // ------------------------------------------------------------------
    // Property 8 — biography update is reflected by the leadership read.
    // ------------------------------------------------------------------

    [Fact]
    public void UpdatedBiography_IsReflected_ByGetLeadershipAsync()
    {
        GenBiography.Sample(newBiography =>
        {
            using var context = CreateContext();
            Seed(context);
            var service = CreateService(context);

            // Prime the cache.
            var original = service.GetLeadershipAsync().GetAwaiter().GetResult()
                                  .Single(l => l.Id == SeededLeadershipId)
                                  .Biography;

            var result = service.UpdateLeadershipBiographyAsync(SeededLeadershipId, newBiography)
                                .GetAwaiter().GetResult();
            Assert.Equal(ProfileUpdateOutcome.Updated, result.Outcome);

            var reflected = service.GetLeadershipAsync().GetAwaiter().GetResult()
                                   .Single(l => l.Id == SeededLeadershipId)
                                   .Biography;

            return reflected == newBiography.Trim() && reflected != original;
        }, iter: Iterations);
    }
}
