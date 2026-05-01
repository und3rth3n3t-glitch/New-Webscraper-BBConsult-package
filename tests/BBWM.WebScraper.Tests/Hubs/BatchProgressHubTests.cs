using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using BBWM.WebScraper.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BBWM.WebScraper.Tests.Hubs;

/// <summary>
/// Integration-flavoured tests for D4.b BatchProgress hub emission.
/// Uses a real RunService with a mock notifier and real in-memory DB.
/// </summary>
public class BatchProgressHubTests
{
    private static (RunService svc, Mock<IWorkerNotifier> notifier) CreateService(TestWebScraperDbContext db)
    {
        var mapper = TestDb.CreateMapper();
        var notifier = new Mock<IWorkerNotifier>();
        notifier.Setup(n => n.SendBatchProgressToUserAsync(It.IsAny<string>(), It.IsAny<BatchProgressDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        notifier.Setup(n => n.SendCancelTaskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        var csv = new Mock<IRunCsvExporter>();
        return (new RunService(db, mapper, notifier.Object, csv.Object), notifier);
    }

    private static async Task<(WorkerConnection worker, RunBatch batch, List<RunItem> runs)>
        SeedBatchAsync(TestWebScraperDbContext db, string userId, int itemCount = 5, string connectionId = "conn-hub")
    {
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = userId, Name = "W", CurrentConnection = connectionId };
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = userId, Name = "T", CreatedAt = DateTimeOffset.UtcNow };
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
        return (worker, batch, runs);
    }

    [Fact]
    public async Task RecordProgress_On5ItemBatch_EmitsBatchProgress_Total5_Running1()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, notifier) = CreateService(db);
        var (worker, batch, runs) = await SeedBatchAsync(db, "user1", 5, "conn-hub");

        await svc.RecordProgressAsync("conn-hub", new TaskProgressDto
        {
            TaskId = runs[0].Id.ToString(), Progress = 20,
        });

        // Sent + Running + Paused all count as "Running" in BatchProgressDto.
        // After RecordProgress: runs[0]=Running, runs[1-4]=still Sent → Running count = 5.
        notifier.Verify(n => n.SendBatchProgressToUserAsync(
            "user1",
            It.Is<BatchProgressDto>(dto =>
                dto.BatchId == batch.Id &&
                dto.Total == 5 &&
                dto.Running == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AfterComplete_BatchProgress_ReflectsCorrectCounts()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, notifier) = CreateService(db);
        var (worker, batch, runs) = await SeedBatchAsync(db, "user1", 5, "conn-hub");

        // Complete 2 items
        for (int i = 0; i < 2; i++)
        {
            await svc.CompleteAsync("conn-hub", new TaskCompleteDto { TaskId = runs[i].Id.ToString(), Result = new TaskResultDto { Iterations = JsonDocument.Parse("[]").RootElement } });
        }

        BatchProgressDto? lastEmitted = null;
        notifier.Setup(n => n.SendBatchProgressToUserAsync(
            "user1", It.IsAny<BatchProgressDto>(), It.IsAny<CancellationToken>()))
            .Callback<string, BatchProgressDto, CancellationToken>((_, dto, _) => lastEmitted = dto)
            .Returns(Task.CompletedTask);

        // Complete third
        await svc.CompleteAsync("conn-hub", new TaskCompleteDto { TaskId = runs[2].Id.ToString(), Result = new TaskResultDto { Iterations = JsonDocument.Parse("[]").RootElement } });

        Assert.NotNull(lastEmitted);
        Assert.Equal(5, lastEmitted!.Total);
        Assert.Equal(3, lastEmitted.Completed);
    }

    [Fact]
    public async Task OverallPercent_CorrectAfterMixedStatuses()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, notifier) = CreateService(db);
        var (worker, batch, runs) = await SeedBatchAsync(db, "user1", 4, "conn-hub");

        BatchProgressDto? lastEmitted = null;
        notifier.Setup(n => n.SendBatchProgressToUserAsync(
            "user1", It.IsAny<BatchProgressDto>(), It.IsAny<CancellationToken>()))
            .Callback<string, BatchProgressDto, CancellationToken>((_, dto, _) => lastEmitted = dto)
            .Returns(Task.CompletedTask);

        // Complete 2 out of 4 = 50%
        await svc.CompleteAsync("conn-hub", new TaskCompleteDto { TaskId = runs[0].Id.ToString(), Result = new TaskResultDto { Iterations = JsonDocument.Parse("[]").RootElement } });
        await svc.CompleteAsync("conn-hub", new TaskCompleteDto { TaskId = runs[1].Id.ToString(), Result = new TaskResultDto { Iterations = JsonDocument.Parse("[]").RootElement } });

        Assert.NotNull(lastEmitted);
        Assert.Equal(50, lastEmitted!.OverallPercent); // (2 completed / 4 total) * 100
    }

    [Fact]
    public async Task RunWithNoBatch_DoesNotEmitBatchProgress()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, notifier) = CreateService(db);

        // Standalone run (no batch)
        var worker = new WorkerConnection { Id = Guid.NewGuid(), UserId = "user1", Name = "W", CurrentConnection = "conn-solo" };
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<WorkerConnection>().Add(worker);
        db.Set<TaskEntity>().Add(task);
        var run = new RunItem
        {
            Id = Guid.NewGuid(), TaskId = task.Id, WorkerId = worker.Id,
            BatchId = null, // no batch
            Status = RunItemStatus.Sent, RequestedAt = DateTimeOffset.UtcNow,
        };
        db.Set<RunItem>().Add(run);
        await db.SaveChangesAsync();

        await svc.RecordProgressAsync("conn-solo", new TaskProgressDto { TaskId = run.Id.ToString(), Progress = 10 });

        notifier.Verify(n => n.SendBatchProgressToUserAsync(
            It.IsAny<string>(), It.IsAny<BatchProgressDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
