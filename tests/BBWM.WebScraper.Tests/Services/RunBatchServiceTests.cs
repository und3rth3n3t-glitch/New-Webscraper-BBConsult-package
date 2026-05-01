using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Expansion;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using BBWM.WebScraper.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BBWM.WebScraper.Tests.Services;

public class RunBatchServiceTests
{
    private static (RunBatchService svc, Mock<IQueueExpansionService> expander, Mock<IWorkerNotifier> notifier, Mock<IRunCsvExporter> csv)
        CreateService(TestWebScraperDbContext db)
    {
        var mapper = TestDb.CreateMapper();
        var expander = new Mock<IQueueExpansionService>();
        var notifier = new Mock<IWorkerNotifier>();
        notifier.Setup(n => n.SendReceiveTaskAsync(It.IsAny<string>(), It.IsAny<QueueTaskDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        notifier.Setup(n => n.SendBatchProgressToUserAsync(It.IsAny<string>(), It.IsAny<BatchProgressDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        var csv = new Mock<IRunCsvExporter>();
        var log = NullLogger<RunBatchService>.Instance;
        var svc = new RunBatchService(db, mapper, expander.Object, notifier.Object, csv.Object, log);
        return (svc, expander, notifier, csv);
    }

    private static ExpansionPreview OkPreview(int count = 3)
    {
        var configId = Guid.NewGuid();
        var results = Enumerable.Range(0, count).Select(i => new ExpansionResult(
            ScrapeBlockId: Guid.NewGuid(),
            ScraperConfigId: configId,
            ConfigName: "Config",
            Assignments: new Dictionary<Guid, string>(),
            IterationLabel: $"item-{i}",
            PatchedConfigJson: JsonDocument.Parse("{}").RootElement,
            SearchTerms: new List<string> { $"term-{i}" })).ToList();
        return new ExpansionPreview(ExpansionOutcome.Ok, count, results, new List<ExpansionWarning>());
    }

    private static async Task<(WorkerConnection worker, TaskEntity task)> SeedWorkerAndTaskAsync(
        TestWebScraperDbContext db, string userId, string connectionId = "conn-1")
    {
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = userId, Name = "W", CurrentConnection = connectionId };
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = userId, Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<WorkerConnection>().Add(worker);
        db.Set<TaskEntity>().Add(task);
        await db.SaveChangesAsync();
        return (worker, task);
    }

    // --- CreateAndDispatch happy path ---

    [Fact]
    public async Task CreateAndDispatch_HappyPath_CreatesRunBatchAndItems_AndDispatches()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, notifier, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OkPreview(3));

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        Assert.Equal(RunBatchOutcome.Created, result.Outcome);
        Assert.NotNull(result.BatchId);
        Assert.Equal(3, result.DispatchedCount);
        Assert.Equal(0, result.FailedCount);

        var batch = await db.Set<RunBatch>().FindAsync(result.BatchId!.Value);
        Assert.NotNull(batch);

        var itemCount = await db.Set<RunItem>().CountAsync(r => r.BatchId == result.BatchId);
        Assert.Equal(3, itemCount);

        notifier.Verify(n => n.SendReceiveTaskAsync(worker.CurrentConnection!, It.IsAny<QueueTaskDto>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task CreateAndDispatch_PopulateSnapshot_ContainsTreeSnapshotAndConfigSnapshots()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, _, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        // Add a block to the task
        var block = new BBWM.WebScraper.Entities.TaskBlock
        {
            Id = Guid.NewGuid(), TaskId = task.Id, BlockType = BlockType.Loop, OrderIndex = 0,
            ConfigJsonb = JsonDocument.Parse("""{"name":"L1","values":[]}"""),
        };
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().Add(block);
        await db.SaveChangesAsync();
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OkPreview(1));

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        var batch = await db.Set<RunBatch>().AsNoTracking().FirstAsync(b => b.Id == result.BatchId);
        var root = batch.PopulateSnapshot.RootElement;
        Assert.True(root.TryGetProperty("treeSnapshot", out _), "PopulateSnapshot must have treeSnapshot");
        Assert.True(root.TryGetProperty("configSnapshots", out _), "PopulateSnapshot must have configSnapshots");
    }

    // --- Outcomes for invalid input ---

    [Fact]
    public async Task CreateAndDispatch_WorkerNotFound_ReturnsNotFound()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _, _) = CreateService(db);

        var result = await svc.CreateAndDispatchAsync("user1", Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(RunBatchOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_WorkerOwnedByOtherUser_ReturnsForbidden()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user2");

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        Assert.Equal(RunBatchOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_WorkerOffline_ReturnsWorkerOffline()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _, _) = CreateService(db);
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = "user1", Name = "W", CurrentConnection = null };
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<WorkerConnection>().Add(worker);
        db.Set<TaskEntity>().Add(task);
        await db.SaveChangesAsync();

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        Assert.Equal(RunBatchOutcome.WorkerOffline, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_ExpansionBatchEmpty_ReturnsBatchEmpty()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, _, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExpansionPreview(ExpansionOutcome.BatchEmpty, 0, new(), new(), "empty"));

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        Assert.Equal(RunBatchOutcome.BatchEmpty, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_ExpansionBatchTooLarge_ReturnsBatchTooLarge()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, _, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExpansionPreview(ExpansionOutcome.BatchTooLarge, 0, new(), new(), "too large"));

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        Assert.Equal(RunBatchOutcome.BatchTooLarge, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_NestedLoopUnsupported_ReturnsNestedLoopUnsupported()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, _, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExpansionPreview(ExpansionOutcome.NestedLoopUnsupported, 0, new(), new(), "nested"));

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        Assert.Equal(RunBatchOutcome.NestedLoopUnsupported, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_PerItemDispatchFailure_IncrementsFailedCount_StatusFailed()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, notifier, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OkPreview(2));

        var callCount = 0;
        notifier.Setup(n => n.SendReceiveTaskAsync(worker.CurrentConnection!, It.IsAny<QueueTaskDto>(), It.IsAny<CancellationToken>()))
                .Returns<string, QueueTaskDto, CancellationToken>((_, _, _) =>
                {
                    callCount++;
                    if (callCount == 2) throw new Exception("Simulated disconnect");
                    return Task.CompletedTask;
                });

        var result = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        Assert.Equal(RunBatchOutcome.Created, result.Outcome);
        Assert.Equal(1, result.DispatchedCount);
        Assert.Equal(1, result.FailedCount);

        var failedRun = await db.Set<RunItem>().FirstOrDefaultAsync(r => r.Status == RunItemStatus.Failed);
        Assert.NotNull(failedRun);
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_ReturnsAggregateItemList()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, notifier, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OkPreview(4));

        var created = await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        var detail = await svc.GetAsync("user1", created.BatchId!.Value);

        Assert.NotNull(detail);
        Assert.Equal(4, detail.RunItems.Count);
    }

    // --- ListAsync paginates ---

    [Fact]
    public async Task ListAsync_PaginatesCorrectly()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, expander, _, _) = CreateService(db);
        var (worker, task) = await SeedWorkerAndTaskAsync(db, "user1");
        expander.Setup(e => e.ExpandAsync("user1", task.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(OkPreview(1));

        // Create 3 batches
        for (int i = 0; i < 3; i++)
            await svc.CreateAndDispatchAsync("user1", task.Id, worker.Id);

        var page1 = await svc.ListAsync("user1", new RunBatchListQueryDto { Page = 1, PageSize = 2 });
        var page2 = await svc.ListAsync("user1", new RunBatchListQueryDto { Page = 2, PageSize = 2 });

        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
    }
}
