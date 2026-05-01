using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using BBWM.WebScraper.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BBWM.WebScraper.Tests.Services;

public class RunServiceTests
{
    private static (RunService svc, Mock<IWorkerNotifier> notifier, Mock<IRunCsvExporter> csv) CreateService(TestWebScraperDbContext db)
    {
        var mapper = TestDb.CreateMapper();
        var notifier = new Mock<IWorkerNotifier>();
        notifier.Setup(n => n.SendBatchProgressToUserAsync(It.IsAny<string>(), It.IsAny<BatchProgressDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        notifier.Setup(n => n.SendCancelTaskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        var csv = new Mock<IRunCsvExporter>();
        var svc = new RunService(db, mapper, notifier.Object, csv.Object);
        return (svc, notifier, csv);
    }

    private static async Task<(TaskEntity task, WorkerConnection worker, RunItem run)> SeedRunAsync(
        TestWebScraperDbContext db, string userId, RunItemStatus status = RunItemStatus.Sent, string connectionId = "conn-1")
    {
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = userId, Name = "W", CurrentConnection = connectionId };
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = userId, Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<WorkerConnection>().Add(worker);
        db.Set<TaskEntity>().Add(task);
        var run = new RunItem
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            WorkerId = worker.Id,
            Status = status,
            RequestedAt = DateTimeOffset.UtcNow,
        };
        db.Set<RunItem>().Add(run);
        await db.SaveChangesAsync();
        return (task, worker, run);
    }

    private static async Task<(TaskEntity task, WorkerConnection worker, RunBatch batch, List<RunItem> runs)>
        SeedBatchAsync(TestWebScraperDbContext db, string userId, int itemCount = 5, string connectionId = "conn-b")
    {
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = userId, Name = "BW", CurrentConnection = connectionId };
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = userId, Name = "BT", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<WorkerConnection>().Add(worker);
        db.Set<TaskEntity>().Add(task);
        var batch = new RunBatch
        {
            Id = Guid.NewGuid(), TaskId = task.Id, UserId = userId, WorkerId = worker.Id,
            PopulateSnapshot = JsonDocument.Parse("{}"), CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<RunBatch>().Add(batch);
        var runs = new List<RunItem>();
        for (int i = 0; i < itemCount; i++)
        {
            var run = new RunItem
            {
                Id = Guid.NewGuid(), TaskId = task.Id, WorkerId = worker.Id, BatchId = batch.Id,
                Status = RunItemStatus.Sent, RequestedAt = DateTimeOffset.UtcNow,
            };
            db.Set<RunItem>().Add(run);
            runs.Add(run);
        }
        await db.SaveChangesAsync();
        return (task, worker, batch, runs);
    }

    // --- GetAsync ---

    [Fact]
    public async Task GetAsync_FiltersByTaskUserId()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        var (_, _, run) = await SeedRunAsync(db, "user1");

        var forOwner = await svc.GetAsync("user1", run.Id);
        var forOther = await svc.GetAsync("user2", run.Id);

        Assert.NotNull(forOwner);
        Assert.Null(forOther);
    }

    // --- ListAsync (paged) ---

    [Fact]
    public async Task ListAsync_FiltersByUserId_ExcludesOtherUsers()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        await SeedRunAsync(db, "user1");
        await SeedRunAsync(db, "user2");

        var result = await svc.ListAsync("user1", new RunListQueryDto { Page = 1, PageSize = 25 });

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task ListAsync_Paginates_WithQueryDto()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        for (int i = 0; i < 5; i++) await SeedRunAsync(db, "user1");

        var page1 = await svc.ListAsync("user1", new RunListQueryDto { Page = 1, PageSize = 3 });
        var page2 = await svc.ListAsync("user1", new RunListQueryDto { Page = 2, PageSize = 3 });

        Assert.Equal(5, page1.Total);
        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
    }

    // --- CancelAsync ---

    [Fact]
    public async Task CancelAsync_HappyPath_ReturnsCancelledAndSendsCancelTask()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, notifier, _) = CreateService(db);
        var (_, worker, run) = await SeedRunAsync(db, "user1", RunItemStatus.Running);

        var outcome = await svc.CancelAsync("user1", run.Id);

        Assert.Equal(CancelRunOutcome.Cancelled, outcome);
        var updated = await db.Set<RunItem>().FindAsync(run.Id);
        Assert.Equal(RunItemStatus.Cancelled, updated!.Status);
        notifier.Verify(n => n.SendCancelTaskAsync(worker.CurrentConnection!, run.Id.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_NotFound_ReturnsNotFound()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);

        var outcome = await svc.CancelAsync("user1", Guid.NewGuid());

        Assert.Equal(CancelRunOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task CancelAsync_WrongUser_ReturnsForbidden()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        var (_, _, run) = await SeedRunAsync(db, "user1");

        var outcome = await svc.CancelAsync("user2", run.Id);

        Assert.Equal(CancelRunOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task CancelAsync_AlreadyCompleted_ReturnsNotCancellable()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        var (_, _, run) = await SeedRunAsync(db, "user1", RunItemStatus.Completed);

        var outcome = await svc.CancelAsync("user1", run.Id);

        Assert.Equal(CancelRunOutcome.NotCancellable, outcome);
    }

    // --- D4 connection-binding: all 4 progress methods silent-drop on connectionId mismatch ---

    public static IEnumerable<object[]> ProgressMethods_ConnectionMismatch =>
        new List<object[]>
        {
            new object[] { "RecordProgress" },
            new object[] { "Complete" },
            new object[] { "Fail" },
            new object[] { "MarkPaused" },
        };

    [Theory]
    [MemberData(nameof(ProgressMethods_ConnectionMismatch))]
    public async Task ProgressMethod_ConnectionIdMismatch_SilentDropsRunUnchanged(string methodName)
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        var (_, _, run) = await SeedRunAsync(db, "user1", RunItemStatus.Sent, "real-conn");

        // Call with wrong connectionId
        switch (methodName)
        {
            case "RecordProgress":
                await svc.RecordProgressAsync("wrong-conn", new TaskProgressDto { TaskId = run.Id.ToString(), Progress = 50 });
                break;
            case "Complete":
                await svc.CompleteAsync("wrong-conn", new TaskCompleteDto { TaskId = run.Id.ToString() });
                break;
            case "Fail":
                await svc.FailAsync("wrong-conn", new TaskErrorDto { TaskId = run.Id.ToString(), Error = "err" });
                break;
            case "MarkPaused":
                await svc.MarkPausedAsync("wrong-conn", new TaskPausedDto { TaskId = run.Id.ToString() });
                break;
        }

        var reloaded = await db.Set<RunItem>().AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal(RunItemStatus.Sent, reloaded.Status); // unchanged
    }

    // --- RecordProgress transitions Sent → Running with StartedAt set ---

    [Fact]
    public async Task RecordProgress_ValidConnection_SentStatus_TransitionsToRunning_SetsStartedAt()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        var (_, _, run) = await SeedRunAsync(db, "user1", RunItemStatus.Sent, "conn-ok");

        await svc.RecordProgressAsync("conn-ok", new TaskProgressDto
        {
            TaskId = run.Id.ToString(), Progress = 30, CurrentTerm = "term1", CurrentStep = "step1", Phase = "loop",
        });

        var reloaded = await db.Set<RunItem>().AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal(RunItemStatus.Running, reloaded.Status);
        Assert.NotNull(reloaded.StartedAt);
        Assert.Equal(30, reloaded.ProgressPercent);
    }

    // --- Complete persists ResultJsonb ---

    [Fact]
    public async Task CompleteAsync_PersistsResultJsonb()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, _) = CreateService(db);
        var (_, _, run) = await SeedRunAsync(db, "user1", RunItemStatus.Running, "conn-ok");

        var result = new TaskResultDto
        {
            TaskId = run.Id.ToString(), Status = "ok",
            Iterations = JsonDocument.Parse("""[{"status":"ok","data":[]}]""").RootElement,
        };
        await svc.CompleteAsync("conn-ok", new TaskCompleteDto { TaskId = run.Id.ToString(), Result = result });

        var reloaded = await db.Set<RunItem>().AsNoTracking().FirstAsync(r => r.Id == run.Id);
        Assert.Equal(RunItemStatus.Completed, reloaded.Status);
        Assert.NotNull(reloaded.ResultJsonb);
        Assert.Equal(100, reloaded.ProgressPercent);
    }

    // --- D4.b: BatchProgress emitted with correct counts ---

    [Fact]
    public async Task RecordProgress_EmitsBatchProgress_WithCorrectCounts()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, notifier, _) = CreateService(db);
        var (_, worker, batch, runs) = await SeedBatchAsync(db, "user1", 5, "conn-b");

        // Trigger RecordProgress on the first run
        await svc.RecordProgressAsync("conn-b", new TaskProgressDto
        {
            TaskId = runs[0].Id.ToString(), Progress = 10,
        });

        // Sent + Running + Paused all count as "Running" in BatchProgressDto (by design).
        // After RecordProgress: runs[0]=Running, runs[1-4]=still Sent → Running count = 5.
        notifier.Verify(n => n.SendBatchProgressToUserAsync(
            "user1",
            It.Is<BatchProgressDto>(dto =>
                dto.BatchId == batch.Id &&
                dto.Total == 5 &&
                dto.Running == 5 &&
                dto.Pending == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- ExportCsvAsync delegates to exporter ---

    [Fact]
    public async Task ExportCsvAsync_DelegatesToExporter_ForOwner()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, csv) = CreateService(db);
        csv.Setup(e => e.ExportRun(It.IsAny<RunItem>(), It.IsAny<ScraperConfigEntity?>(), It.IsAny<RunBatch?>()))
           .Returns(new byte[] { 1, 2, 3 });

        var (_, _, run) = await SeedRunAsync(db, "user1", RunItemStatus.Completed, "conn-x");

        var bytes = await svc.ExportCsvAsync("user1", run.Id);

        Assert.NotNull(bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public async Task ExportCsvAsync_NonOwner_ReturnsNull()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _, csv) = CreateService(db);
        csv.Setup(e => e.ExportRun(It.IsAny<RunItem>(), It.IsAny<ScraperConfigEntity?>(), It.IsAny<RunBatch>()))
           .Returns(Array.Empty<byte>());
        var (_, _, run) = await SeedRunAsync(db, "user1", RunItemStatus.Completed, "conn-x");

        var bytes = await svc.ExportCsvAsync("user2", run.Id);

        Assert.Null(bytes);
    }
}
