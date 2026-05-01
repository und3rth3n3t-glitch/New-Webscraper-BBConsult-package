using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.Services;

public class TaskServiceTests
{
    private static (TaskService svc, TestWebScraperDbContext db) Build()
    {
        var db = TestDb.CreateInMemory();
        var svc = new TaskService(db, TestDb.CreateMapper());
        return (svc, db);
    }

    private static async Task<ScraperConfigEntity> SeedConfig(TestWebScraperDbContext db, string userId)
    {
        var config = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Config",
            Domain = "example.com",
            ConfigJson = JsonDocument.Parse("{}"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);
        await db.SaveChangesAsync();
        return config;
    }

    [Fact]
    public async Task CreateAsync_RejectsNonOwnerConfig()
    {
        var (svc, db) = Build();
        var config = await SeedConfig(db, "user-owner");

        var result = await svc.CreateAsync("user-other", new CreateTaskDto
        {
            Name = "Hijacked",
            ScraperConfigId = config.Id,
            SearchTerms = new[] { "term" },
        });

        Assert.Null(result);
        Assert.Equal(0, await db.Set<TaskEntity>().CountAsync());
    }

    [Fact]
    public async Task CreateAsync_RoundTripsSearchTerms()
    {
        var (svc, db) = Build();
        var config = await SeedConfig(db, "user-1");
        var terms = new[] { "apple", "banana", "cherry" };

        var created = await svc.CreateAsync("user-1", new CreateTaskDto
        {
            Name = "MyTask",
            ScraperConfigId = config.Id,
            SearchTerms = terms,
        });

        Assert.NotNull(created);
        Assert.Equal(terms, created!.SearchTerms);
    }

    [Fact]
    public async Task ListAsync_OrdersByCreatedAtDesc()
    {
        var (svc, db) = Build();
        var config = await SeedConfig(db, "user-1");

        var t1 = new TaskEntity { Id = Guid.NewGuid(), UserId = "user-1", Name = "First", ScraperConfigId = config.Id, SearchTerms = Array.Empty<string>(), CreatedAt = DateTimeOffset.UtcNow.AddHours(-2) };
        var t2 = new TaskEntity { Id = Guid.NewGuid(), UserId = "user-1", Name = "Second", ScraperConfigId = config.Id, SearchTerms = Array.Empty<string>(), CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        var t3 = new TaskEntity { Id = Guid.NewGuid(), UserId = "user-1", Name = "Third", ScraperConfigId = config.Id, SearchTerms = Array.Empty<string>(), CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().AddRange(t1, t2, t3);
        await db.SaveChangesAsync();

        var list = await svc.ListAsync("user-1");

        Assert.Equal(3, list.Count);
        Assert.Equal("Third", list[0].Name);
        Assert.Equal("First", list[2].Name);
    }

    [Fact]
    public async Task GetAsync_ExcludesOtherUsersTask()
    {
        var (svc, db) = Build();
        var config = await SeedConfig(db, "user-1");
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user-1", Name = "T", ScraperConfigId = config.Id, SearchTerms = Array.Empty<string>(), CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        await db.SaveChangesAsync();

        var asOther = await svc.GetAsync("user-2", task.Id);
        Assert.Null(asOther);

        var asOwner = await svc.GetAsync("user-1", task.Id);
        Assert.NotNull(asOwner);
    }
}
