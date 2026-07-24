using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Trimix.Api.Data;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations</c>: always Npgsql (the canonical runtime
/// provider whose migrations are checked in) without booting the app. No connection is opened
/// for <c>migrations add</c>; the connection string is a placeholder.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5544;Database=trimix;Username=trimix;Password=trimix")
            .Options;
        return new AppDbContext(options);
    }
}
