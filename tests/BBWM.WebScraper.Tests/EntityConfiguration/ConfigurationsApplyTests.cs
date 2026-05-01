using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.EntityConfiguration;

public class ConfigurationsApplyTests
{
    [Fact]
    public void EnsureCreated_SucceedsForAllEntityConfigurations()
    {
        using var db = TestDb.CreateInMemory();
        // EnsureCreated already called by TestDb — assertion is "no exception thrown".
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.ScraperConfigEntity)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.TaskEntity)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.WorkerConnection)));
        Assert.NotNull(db.Model.FindEntityType(typeof(BBWM.WebScraper.Entities.RunItem)));
    }
}
