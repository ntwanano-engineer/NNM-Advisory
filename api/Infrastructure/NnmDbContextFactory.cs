using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NnmAdvisory.Api.Infrastructure;

/// <summary>
/// Design-time factory used exclusively by EF Core tooling (dotnet-ef migrations,
/// dotnet-ef database update) so that the CLI can instantiate <see cref="NnmDbContext"/>
/// without running the full ASP.NET Core host — and therefore without triggering
/// the <c>ValidateOnStart()</c> settings validation that requires real environment
/// variables to be present.
///
/// The connection string supplied here is a placeholder used only during migration
/// generation; it is never used at runtime. At runtime, <see cref="Program"/>
/// registers the context with the real connection string sourced from
/// <c>DB_CONNECTION_STRING</c> / AWS Parameter Store.
///
/// Requirement 11.4: database schema managed via versioned migration scripts.
/// </summary>
public sealed class NnmDbContextFactory : IDesignTimeDbContextFactory<NnmDbContext>
{
    public NnmDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NnmDbContext>();

        // Placeholder connection string — EF tooling only needs a syntactically
        // valid Npgsql connection string; it does NOT connect to the database
        // during "migrations add".
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=nnmdb;Username=nnm;Password=devpassword",
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"));

        return new NnmDbContext(optionsBuilder.Options);
    }
}
