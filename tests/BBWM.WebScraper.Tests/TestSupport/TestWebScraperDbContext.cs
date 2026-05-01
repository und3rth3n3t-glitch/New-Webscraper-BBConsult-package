using BBWM.Core.Data;
using BBWM.WebScraper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BBWM.WebScraper.Tests.TestSupport;

// Minimal DbContext implementing IDbContext, with the module's entity configurations applied.
public class TestWebScraperDbContext : DbContext, IDbContext
{
    public TestWebScraperDbContext(DbContextOptions<TestWebScraperDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
        => builder.ApplyConfigurationsFromAssembly(typeof(WebScraperModuleLinkage).Assembly);

    // IDbContext explicit members are inherited from DbContext (Database, Model, Set<T>, SaveChanges, SaveChangesAsync, Entry<T>).
}
