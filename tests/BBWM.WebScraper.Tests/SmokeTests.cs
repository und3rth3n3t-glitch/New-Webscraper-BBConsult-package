using AutoMapper;
using BBWM.WebScraper.Mapping;
using BBWM.WebScraper.Tests.TestSupport;
using Xunit;

namespace BBWM.WebScraper.Tests;

// Placeholder smoke tests pending the full v2.0 test suite (~106 tests planned).
// These verify the module's data layer and AutoMapper profile load cleanly.
public class SmokeTests
{
    [Fact]
    public void EntityModel_Applies_AllSevenEntities()
    {
        using var db = TestDb.CreateInMemory();
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.ScraperConfigEntity)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.ScraperConfigSubscription)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.TaskEntity)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.TaskBlock)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.RunItem)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.RunBatch)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.WorkerConnection)));
    }

    [Fact]
    public void AutoMapperProfile_IsValid()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<WebScraperAutoMapperProfile>());
        config.AssertConfigurationIsValid();
    }
}
