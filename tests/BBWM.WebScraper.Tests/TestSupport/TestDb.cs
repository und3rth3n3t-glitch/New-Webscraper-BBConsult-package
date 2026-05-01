using AutoMapper;
using BBWM.WebScraper.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BBWM.WebScraper.Tests.TestSupport;

public static class TestDb
{
    public static TestWebScraperDbContext CreateInMemory()
    {
        var opts = new DbContextOptionsBuilder<TestWebScraperDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // Suppress the warning that InMemory doesn't support real transactions.
            // The service uses BeginTransactionAsync for lock semantics on real DBs;
            // the in-memory provider silently no-ops, which is fine for unit tests.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new TestWebScraperDbContext(opts);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static IMapper CreateMapper()
        => new MapperConfiguration(cfg => cfg.AddProfile<WebScraperAutoMapperProfile>()).CreateMapper();
}
