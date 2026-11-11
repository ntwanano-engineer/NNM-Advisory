using Microsoft.Extensions.Caching.Memory;
using NnmAdvisory.Api.Models;
using NnmAdvisory.Api.Repositories;
using NnmAdvisory.Api.Services;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Unit tests for <see cref="CompanyProfileService"/> cache behaviour (Task 4.4).
///
/// The service wraps <see cref="ICompanyProfileRepository"/> and caches the
/// three read operations in an <see cref="IMemoryCache"/> with a 5-second TTL.
/// The core guarantee under test: a second read within the TTL window returns
/// the cached value and never touches the repository, which is what lets the
/// public site reflect persisted content within 5 seconds (Requirement 3.8).
///
/// Tests use a hand-written counting fake repository (no mocking framework is a
/// project dependency) and a real <see cref="MemoryCache"/> instance so the
/// caching path is exercised for real rather than simulated.
///
/// <para><b>Validates: Requirements 3.8</b></para>
/// </summary>
public sealed class CompanyProfileServiceTests
{
    // ------------------------------------------------------------------
    // Hand-written counting test double — no mocking framework dependency.
    // ------------------------------------------------------------------

    /// <summary>
    /// Fake repository that returns a fixed snapshot of data and counts how many
    /// times each read method is invoked, so tests can assert whether a call hit
    /// the repository or was served from the cache.
    /// </summary>
    private sealed class CountingCompanyProfileRepository : ICompanyProfileRepository
    {
        public int ContentCallCount { get; private set; }
        public int ServiceBlocksCallCount { get; private set; }
        public int LeadershipCallCount { get; private set; }

        public IReadOnlyList<CompanyContent> Content { get; init; } = new List<CompanyContent>
        {
            new() { Id = Guid.NewGuid(), ContentKey = "tagline", ContentValue = "Original tagline" },
        };

        public IReadOnlyList<ServiceBlock> ServiceBlocks { get; init; } = new List<ServiceBlock>
        {
            new() { Id = Guid.NewGuid(), Category = "accounting", Title = "Accounting", Description = "desc" },
        };

        public IReadOnlyList<LeadershipProfile> Leadership { get; init; } = new List<LeadershipProfile>
        {
            new() { Id = Guid.NewGuid(), FullName = "Ntwanano Mabasa", Title = "COO", Biography = "bio" },
        };

        public Task<IReadOnlyList<CompanyContent>> GetAllContentAsync()
        {
            ContentCallCount++;
            return Task.FromResult(Content);
        }

        public Task<IReadOnlyList<ServiceBlock>> GetAllServiceBlocksAsync()
        {
            ServiceBlocksCallCount++;
            return Task.FromResult(ServiceBlocks);
        }

        public Task<IReadOnlyList<LeadershipProfile>> GetAllLeadershipProfilesAsync()
        {
            LeadershipCallCount++;
            return Task.FromResult(Leadership);
        }

        // --- Read-by-key and update members: not exercised by the cache-hit
        // tests, but required to satisfy the interface. The tagline update is
        // used by the cache-invalidation test below.

        public Task<CompanyContent?> GetContentByKeyAsync(string key) =>
            Task.FromResult(Content.FirstOrDefault(c =>
                string.Equals(c.ContentKey, key, StringComparison.OrdinalIgnoreCase)));

        public Task<CompanyContent?> UpdateContentAsync(string key, string contentValue)
        {
            var row = Content.FirstOrDefault(c =>
                string.Equals(c.ContentKey, key, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                return Task.FromResult<CompanyContent?>(null);
            }

            row.ContentValue = contentValue;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<CompanyContent?>(row);
        }

        public Task<ServiceBlock?> UpdateServiceBlockAsync(
            Guid id, string title, string description, string[] offerings) =>
            Task.FromResult(ServiceBlocks.FirstOrDefault(s => s.Id == id));

        public Task<LeadershipProfile?> UpdateLeadershipProfileAsync(
            Guid id, string biography, string? photoS3Key) =>
            Task.FromResult(Leadership.FirstOrDefault(l => l.Id == id));
    }

    private static (CompanyProfileService Service, CountingCompanyProfileRepository Repository, MemoryCache Cache)
        CreateService(CountingCompanyProfileRepository? repository = null)
    {
        repository ??= new CountingCompanyProfileRepository();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CompanyProfileService(repository, cache);
        return (service, repository, cache);
    }

    // ------------------------------------------------------------------
    // Core guarantee: a second read within the TTL is served from cache
    // and does not hit the repository (Requirement 3.8).
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetProfileAsync_SecondCallWithinTtl_ReturnsCachedDataWithoutHittingRepository()
    {
        var (service, repository, _) = CreateService();

        var first = await service.GetProfileAsync();
        var second = await service.GetProfileAsync();

        // The repository was consulted exactly once; the second read was cached.
        Assert.Equal(1, repository.ContentCallCount);
        // Both reads return the same, equal snapshot.
        Assert.Same(first, second);
        Assert.Equal(repository.Content, second);
    }

    [Fact]
    public async Task GetServicesAsync_SecondCallWithinTtl_ReturnsCachedDataWithoutHittingRepository()
    {
        var (service, repository, _) = CreateService();

        var first = await service.GetServicesAsync();
        var second = await service.GetServicesAsync();

        Assert.Equal(1, repository.ServiceBlocksCallCount);
        Assert.Same(first, second);
        Assert.Equal(repository.ServiceBlocks, second);
    }

    [Fact]
    public async Task GetLeadershipAsync_SecondCallWithinTtl_ReturnsCachedDataWithoutHittingRepository()
    {
        var (service, repository, _) = CreateService();

        var first = await service.GetLeadershipAsync();
        var second = await service.GetLeadershipAsync();

        Assert.Equal(1, repository.LeadershipCallCount);
        Assert.Same(first, second);
        Assert.Equal(repository.Leadership, second);
    }

    [Fact]
    public async Task GetProfileAsync_MultipleReadsWithinTtl_HitRepositoryOnlyOnce()
    {
        var (service, repository, _) = CreateService();

        for (var i = 0; i < 5; i++)
        {
            await service.GetProfileAsync();
        }

        Assert.Equal(1, repository.ContentCallCount);
    }

    // ------------------------------------------------------------------
    // Caches are independent: reading one entity does not populate another.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reads_UseSeparateCacheKeysPerEntity()
    {
        var (service, repository, _) = CreateService();

        await service.GetProfileAsync();
        await service.GetServicesAsync();
        await service.GetLeadershipAsync();

        Assert.Equal(1, repository.ContentCallCount);
        Assert.Equal(1, repository.ServiceBlocksCallCount);
        Assert.Equal(1, repository.LeadershipCallCount);
    }

    // ------------------------------------------------------------------
    // A successful update invalidates the relevant cache so the next read
    // repopulates it from the repository (Requirement 9.2 behaviour that
    // supports the "reflect within 5 seconds" guarantee of Requirement 3.8).
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateTaglineAsync_InvalidatesProfileCache_SoNextReadHitsRepositoryAgain()
    {
        var (service, repository, _) = CreateService();

        // Prime the profile cache (repo hit #1).
        await service.GetProfileAsync();
        Assert.Equal(1, repository.ContentCallCount);

        // A successful update must drop the stale profile cache entry.
        var result = await service.UpdateTaglineAsync("Updated tagline");
        Assert.Equal(ProfileUpdateOutcome.Updated, result.Outcome);

        // The next read therefore repopulates from the repository (repo hit #2).
        await service.GetProfileAsync();
        Assert.Equal(2, repository.ContentCallCount);
    }

    [Fact]
    public async Task UpdateTaglineAsync_DoesNotInvalidateUnrelatedCaches()
    {
        var (service, repository, _) = CreateService();

        await service.GetServicesAsync();
        await service.GetLeadershipAsync();
        await service.UpdateTaglineAsync("Updated tagline");

        // Services and leadership caches are untouched by a tagline update.
        await service.GetServicesAsync();
        await service.GetLeadershipAsync();

        Assert.Equal(1, repository.ServiceBlocksCallCount);
        Assert.Equal(1, repository.LeadershipCallCount);
    }
}
