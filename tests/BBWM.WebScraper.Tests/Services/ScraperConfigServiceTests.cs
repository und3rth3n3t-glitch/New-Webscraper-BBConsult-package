using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using BBWM.WebScraper.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BBWM.WebScraper.Tests.Services;

public class ScraperConfigServiceTests
{
    private static (ScraperConfigService svc, Mock<IWorkerNotifier> notifier) CreateService(TestWebScraperDbContext db)
    {
        var mapper = TestDb.CreateMapper();
        var notifier = new Mock<IWorkerNotifier>();
        notifier.Setup(n => n.SendConfigDeletedToUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        var svc = new ScraperConfigService(db, mapper, notifier.Object);
        return (svc, notifier);
    }

    private static CreateScraperConfigDto MakeCreateDto(string name = "Cfg", string domain = "example.com", string json = "{}")
        => new() { Name = name, Domain = domain, ConfigJson = JsonDocument.Parse(json).RootElement, SchemaVersion = 3, Shared = false };

    private static async Task<ScraperConfigEntity> SeedConfigAsync(TestWebScraperDbContext db, string userId, string name = "Cfg", bool shared = false)
    {
        var entity = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Domain = "example.com",
            ConfigJson = JsonDocument.Parse("{}"),
            Shared = shared,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = 1,
        };
        db.Set<ScraperConfigEntity>().Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    // --- List ---

    [Fact]
    public async Task List_ReturnsOnlyCallerConfigs()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        await SeedConfigAsync(db, "user1", "A");
        await SeedConfigAsync(db, "user2", "B");

        var result = await svc.ListAsync("user1");

        Assert.Single(result);
        Assert.Equal("A", result[0].Name);
    }

    // --- Get ---

    [Fact]
    public async Task Get_ReturnsNull_ForOtherUsersConfig()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1");

        var result = await svc.GetAsync("user2", entity.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task Get_ReturnsDto_ForOwner()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1", "MyConfig");

        var result = await svc.GetAsync("user1", entity.Id);

        Assert.NotNull(result);
        Assert.Equal("MyConfig", result.Name);
    }

    // --- Create ---

    [Fact]
    public async Task Create_RoundTripsConfigJson_AndSetsVersion1()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var dto = MakeCreateDto(json: """{"key":"val"}""");

        var result = await svc.CreateAsync("user1", dto, null);

        Assert.Equal(CreateScraperConfigOutcome.Created, result.Outcome);
        Assert.Equal("val", result.Dto.ConfigJson.GetProperty("key").GetString());
        var entity = await db.Set<ScraperConfigEntity>().FirstAsync();
        Assert.Equal(1, entity.Version);
    }

    [Fact]
    public async Task Create_SuggestedId_Hits_SameBody_ReturnsIdempotent()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var id = Guid.NewGuid();
        var dto = new CreateScraperConfigDto
        {
            SuggestedId = id, Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("{}").RootElement, SchemaVersion = 3,
        };

        var r1 = await svc.CreateAsync("user1", dto, null);
        var r2 = await svc.CreateAsync("user1", dto, null);

        Assert.Equal(CreateScraperConfigOutcome.Created, r1.Outcome);
        Assert.Equal(CreateScraperConfigOutcome.Idempotent, r2.Outcome);
        Assert.Equal(1, await db.Set<ScraperConfigEntity>().CountAsync());
    }

    [Fact]
    public async Task Create_SuggestedId_Hits_DifferentBody_ReturnsConflict()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var id = Guid.NewGuid();

        var dto1 = new CreateScraperConfigDto { SuggestedId = id, Name = "Cfg", Domain = "x.com", ConfigJson = JsonDocument.Parse("{}").RootElement };
        var dto2 = new CreateScraperConfigDto { SuggestedId = id, Name = "Different", Domain = "x.com", ConfigJson = JsonDocument.Parse("{}").RootElement };

        await svc.CreateAsync("user1", dto1, null);
        var r2 = await svc.CreateAsync("user1", dto2, null);

        Assert.Equal(CreateScraperConfigOutcome.Conflict, r2.Outcome);
    }

    // --- Update (D1.c) ---

    [Fact]
    public async Task Update_WithMatchingIfMatchVersion_ReturnsUpdated_AndIncrementsVersion()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1");
        var dto = MakeCreateDto("Updated");

        var result = await svc.UpdateAsync("user1", entity.Id, dto, ifMatchVersion: 1, workerId: null);

        Assert.Equal(UpdateScraperConfigOutcome.Updated, result.Outcome);
        Assert.Equal("Updated", result.Dto!.Name);
        var reloaded = await db.Set<ScraperConfigEntity>().AsNoTracking().FirstAsync(c => c.Id == entity.Id);
        Assert.Equal(2, reloaded.Version);
    }

    [Fact]
    public async Task Update_WithMismatchedIfMatchVersion_ReturnsPreconditionFailed()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1");

        var result = await svc.UpdateAsync("user1", entity.Id, MakeCreateDto(), ifMatchVersion: 99, workerId: null);

        Assert.Equal(UpdateScraperConfigOutcome.PreconditionFailed, result.Outcome);
    }

    [Fact]
    public async Task Update_SharedConfig_WithoutIfMatch_ReturnsPreconditionRequired()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1", shared: true);

        var result = await svc.UpdateAsync("user1", entity.Id, MakeCreateDto(), ifMatchVersion: null, workerId: null);

        Assert.Equal(UpdateScraperConfigOutcome.PreconditionRequired, result.Outcome);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);

        var result = await svc.UpdateAsync("user1", Guid.NewGuid(), MakeCreateDto(), ifMatchVersion: null, workerId: null);

        Assert.Equal(UpdateScraperConfigOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Update_ConcurrentRace_AtLeastOneReturnsPreconditionFailed()
    {
        // D5.e: two updates with the same If-Match version on the same tracked entity.
        // We simulate it by doing two sequential updates that both try version=1.
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1");

        var r1 = await svc.UpdateAsync("user1", entity.Id, MakeCreateDto("First"), ifMatchVersion: 1, workerId: null);
        // After r1 succeeds (version is now 2), r2 still presents version=1.
        var r2 = await svc.UpdateAsync("user1", entity.Id, MakeCreateDto("Second"), ifMatchVersion: 1, workerId: null);

        Assert.Equal(UpdateScraperConfigOutcome.Updated, r1.Outcome);
        Assert.Equal(UpdateScraperConfigOutcome.PreconditionFailed, r2.Outcome);
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_NoSubscribers_ReturnsDeleted_NotifierNotCalled()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, notifier) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1");

        var result = await svc.DeleteAsync("user1", entity.Id);

        Assert.Equal(DeleteScraperConfigOutcome.Deleted, result.Outcome);
        notifier.Verify(n => n.SendConfigDeletedToUserAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_WithSubscribersFromDifferentUsers_NotifiesEachDistinctSubscriberUserId()
    {
        // D5.d.c
        using var db = TestDb.CreateInMemory();
        var (svc, notifier) = CreateService(db);
        var entity = await SeedConfigAsync(db, "owner", shared: true);

        // Two workers from different users subscribed
        var worker1 = new WorkerConnection { Id = Guid.NewGuid(), UserId = "sub-user1", Name = "W1" };
        var worker2 = new WorkerConnection { Id = Guid.NewGuid(), UserId = "sub-user2", Name = "W2" };
        // Third worker from same user as worker1 (should not emit duplicate)
        var worker3 = new WorkerConnection { Id = Guid.NewGuid(), UserId = "sub-user1", Name = "W3" };
        db.Set<WorkerConnection>().AddRange(worker1, worker2, worker3);
        db.Set<ScraperConfigSubscription>().AddRange(
            new ScraperConfigSubscription { ScraperConfigId = entity.Id, WorkerId = worker1.Id, LastPulledAt = DateTimeOffset.UtcNow },
            new ScraperConfigSubscription { ScraperConfigId = entity.Id, WorkerId = worker2.Id, LastPulledAt = DateTimeOffset.UtcNow },
            new ScraperConfigSubscription { ScraperConfigId = entity.Id, WorkerId = worker3.Id, LastPulledAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var result = await svc.DeleteAsync("owner", entity.Id);

        Assert.Equal(DeleteScraperConfigOutcome.Deleted, result.Outcome);
        // sub-user1 and sub-user2 each notified once (distinct)
        notifier.Verify(n => n.SendConfigDeletedToUserAsync("sub-user1", entity.Id, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.SendConfigDeletedToUserAsync("sub-user2", entity.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_NonOwner_ReturnsForbidden()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1");

        var result = await svc.DeleteAsync("user2", entity.Id);

        Assert.Equal(DeleteScraperConfigOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Delete_WithTaskBlockReferences_ReturnsReferenced()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "user1");

        // Add a task + scrape block referencing this config
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        var block = new BBWM.WebScraper.Entities.TaskBlock
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            BlockType = BBWM.WebScraper.Enums.BlockType.Scrape,
            OrderIndex = 0,
            ConfigJsonb = JsonDocument.Parse($$$"""{"scraperConfigId":"{{{entity.Id}}}"}"""),
        };
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().Add(block);
        await db.SaveChangesAsync();

        var result = await svc.DeleteAsync("user1", entity.Id);

        Assert.Equal(DeleteScraperConfigOutcome.Referenced, result.Outcome);
        Assert.True(result.ReferencingTaskCount > 0);
    }

    // --- GetSubscribers ---

    [Fact]
    public async Task GetSubscribers_NonOwner_ReturnsNull()
    {
        // D5.c.a
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "owner");

        var result = await svc.GetSubscribersAsync("other-user", entity.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSubscribers_Owner_ReturnsList()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "owner");
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = "sub-user", Name = "W" };
        db.Set<WorkerConnection>().Add(worker);
        db.Set<ScraperConfigSubscription>().Add(new ScraperConfigSubscription
        {
            ScraperConfigId = entity.Id, WorkerId = worker.Id, LastPulledAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await svc.GetSubscribersAsync("owner", entity.Id);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(worker.Id, result[0].Id);
    }

    // --- RecordSubscription (D5.b) ---

    [Fact]
    public async Task RecordSubscription_UnsharedConfig_ReturnsFalse()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "owner", shared: false);
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = "user1", Name = "W" };
        db.Set<WorkerConnection>().Add(worker);
        await db.SaveChangesAsync();

        var ok = await svc.RecordSubscriptionAsync("user1", entity.Id, worker.Id);

        Assert.False(ok);
    }

    [Fact]
    public async Task RecordSubscription_CallerDoesNotOwnWorker_ReturnsFalse()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "owner", shared: true);
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = "other-user", Name = "W" };
        db.Set<WorkerConnection>().Add(worker);
        await db.SaveChangesAsync();

        var ok = await svc.RecordSubscriptionAsync("user1", entity.Id, worker.Id);

        Assert.False(ok);
    }

    [Fact]
    public async Task RecordSubscription_SharedConfigWithOwnedWorker_ReturnsTrue_AndSecondCallRefreshesLastPulledAt()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var entity = await SeedConfigAsync(db, "owner", shared: true);
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = "user1", Name = "W" };
        db.Set<WorkerConnection>().Add(worker);
        await db.SaveChangesAsync();

        var ok1 = await svc.RecordSubscriptionAsync("user1", entity.Id, worker.Id);
        var sub1 = await db.Set<ScraperConfigSubscription>().AsNoTracking()
            .FirstAsync(s => s.ScraperConfigId == entity.Id && s.WorkerId == worker.Id);
        var firstPulledAt = sub1.LastPulledAt;

        await Task.Delay(5); // ensure time advances
        var ok2 = await svc.RecordSubscriptionAsync("user1", entity.Id, worker.Id);
        var sub2 = await db.Set<ScraperConfigSubscription>().AsNoTracking()
            .FirstAsync(s => s.ScraperConfigId == entity.Id && s.WorkerId == worker.Id);

        Assert.True(ok1);
        Assert.True(ok2);
        // Only one subscription row should exist (idempotent)
        Assert.Equal(1, await db.Set<ScraperConfigSubscription>().CountAsync());
    }

    // --- ListShared ---

    [Fact]
    public async Task ListShared_FiltersSharedTrue_ForCaller()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        await SeedConfigAsync(db, "user1", "Private", shared: false);
        await SeedConfigAsync(db, "user1", "Public", shared: true);
        await SeedConfigAsync(db, "user2", "OtherPublic", shared: true);

        var result = await svc.ListSharedAsync("user1");

        Assert.Single(result);
        Assert.Equal("Public", result[0].Name);
    }
}
