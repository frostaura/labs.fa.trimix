using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trimix.Api.Data;

namespace Trimix.Api.Tests;

/// <summary>
/// Hermetic API host for integration tests: SQLite in-memory on a kept-open connection replaces
/// Npgsql (schema via EnsureCreated in Program startup — no Docker dependency, per architecture
/// overview §5). One factory instance = one fresh database.
/// </summary>
public sealed class TrimixApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Sandbox billing shared secret the test host is configured with.</summary>
    public const string SandboxSecret = "trimix-integration-test-sandbox-secret";

    private const string JwtSecret = "trimix-integration-test-jwt-secret-0123456789-0123456789-0123456789";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = JwtSecret,
            ["Billing:SandboxSecret"] = SandboxSecret,
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <summary>Runs an assertion/arrangement against the test host's database.</summary>
    public T WithDb<T>(Func<AppDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Mutates the test host's database (SaveChanges included).</summary>
    public void MutateDb(Action<AppDbContext> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
