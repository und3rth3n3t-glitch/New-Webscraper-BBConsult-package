using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.Services;

public class ScraperConfigServiceTests
{
    private static (ScraperConfigService svc, TestWebScraperDbContext db) Build()
    {
        var db = TestDb.CreateInMemory();
        var svc = new ScraperConfigService(db, TestDb.CreateMapper());
        return (svc, db);
    }

    private static CreateScraperConfigDto MakeDto(string name = "Config A", string configJson = "{}")
        => new() { Name = name, Domain = "example.com", ConfigJson = JsonDocument.Parse(configJson).RootElement, SchemaVersion = 3 };

    [Fact]
    public async Task ListAsync_FiltersToUserId()
    {
        var (svc, _) = Build();
        await svc.CreateAsync("user-1", MakeDto("C1"));
        await svc.CreateAsync("user-1", MakeDto("C2"));
        await svc.CreateAsync("user-2", MakeDto("C3"));

        var list = await svc.ListAsync("user-1");

        Assert.Equal(2, list.Count);
        Assert.All(list, c => Assert.NotEqual("C3", c.Name));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForOtherUser()
    {
        var (svc, _) = Build();
        var created = await svc.CreateAsync("user-1", MakeDto());

        var result = await svc.GetAsync("user-2", created.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_RoundTripsConfigJson()
    {
        var (svc, _) = Build();
        var json = """{"key":"value","num":42}""";

        var created = await svc.CreateAsync("user-1", MakeDto(configJson: json));

        Assert.Equal("value", created.ConfigJson.GetProperty("key").GetString());
        Assert.Equal(42, created.ConfigJson.GetProperty("num").GetInt32());
    }

    [Fact]
    public async Task UpdateAsync_SucceedsForOwner()
    {
        var (svc, _) = Build();
        var created = await svc.CreateAsync("user-1", MakeDto("Old Name"));

        var updated = await svc.UpdateAsync("user-1", created.Id, MakeDto("New Name"));

        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.Name);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNullForNonOwner()
    {
        var (svc, _) = Build();
        var created = await svc.CreateAsync("user-1", MakeDto());

        var result = await svc.UpdateAsync("user-2", created.Id, MakeDto("Hijacked"));

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_PreservesSchemaVersion_WhenDtoSchemaVersionIsZeroOrNegative()
    {
        var (svc, _) = Build();
        var created = await svc.CreateAsync("user-1", MakeDto());
        Assert.Equal(3, created.SchemaVersion);

        var updated = await svc.UpdateAsync("user-1", created.Id, new CreateScraperConfigDto
        {
            Name = "Same",
            Domain = "example.com",
            ConfigJson = JsonDocument.Parse("{}").RootElement,
            SchemaVersion = 0, // <= 0 should not overwrite existing
        });

        Assert.NotNull(updated);
        Assert.Equal(3, updated!.SchemaVersion); // preserved
    }
}
