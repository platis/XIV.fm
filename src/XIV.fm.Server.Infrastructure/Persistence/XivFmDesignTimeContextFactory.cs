using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace XIV.fm.Server.Infrastructure.Persistence;

/// <summary>
/// Lets the EF Core tools create migrations and SQL scripts without a database or server configuration.
/// </summary>
internal sealed class XivFmDesignTimeContextFactory : IDesignTimeDbContextFactory<XivFmDbContext>
{
    public XivFmDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<XivFmDbContext>().UseNpgsql().Options);
}
