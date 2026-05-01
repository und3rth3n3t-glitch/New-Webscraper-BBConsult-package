using AutoMapper;
using BBWM.WebScraper.Mapping;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Tests.TestSupport;

public static class TestDb
{
    public static TestWebScraperDbContext CreateInMemory()
    {
        var opts = new DbContextOptionsBuilder<TestWebScraperDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var ctx = new TestWebScraperDbContext(opts);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    public static IMapper CreateMapper()
        => new MapperConfiguration(cfg => cfg.AddProfile<WebScraperAutoMapperProfile>()).CreateMapper();
}
