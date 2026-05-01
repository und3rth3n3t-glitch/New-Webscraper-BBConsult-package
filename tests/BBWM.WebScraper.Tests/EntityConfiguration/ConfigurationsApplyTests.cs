using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.EntityConfiguration;

/// <summary>
/// Verifies all 7 entity configurations are applied. Carries from SmokeTests.
/// </summary>
public class ConfigurationsApplyTests
{
    [Fact]
    public void EntityModel_Applies_AllSevenEntities()
    {
        using var db = TestDb.CreateInMemory();
        Assert.NotNull(db.Model.FindEntityType(typeof(ScraperConfigEntity)));
        Assert.NotNull(db.Model.FindEntityType(typeof(ScraperConfigSubscription)));
        Assert.NotNull(db.Model.FindEntityType(typeof(TaskEntity)));
        Assert.NotNull(db.Model.FindEntityType(typeof(TaskBlock)));
        Assert.NotNull(db.Model.FindEntityType(typeof(RunItem)));
        Assert.NotNull(db.Model.FindEntityType(typeof(RunBatch)));
        Assert.NotNull(db.Model.FindEntityType(typeof(WorkerConnection)));
    }
}
