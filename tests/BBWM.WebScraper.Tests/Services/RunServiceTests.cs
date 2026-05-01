using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.Services;

public class RunServiceTests
{
    private const string UserId = "user-run-tests";
    private const string ConnId = "conn-1";

    private static async Task<(RunService svc, TestWebScraperDbContext db, Mock<IWorkerNotifier> notifier, TaskEntity task, WorkerConnection worker)> Build()
    {
        var db = TestDb.CreateInMemory();
        var notifier = new Mock<IWorkerNotifier>(MockBehavior.Loose);
        var svc = new RunService(db, TestDb.CreateMapper(), notifier.Object);

        var config = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Name = "Config1",
            Domain = "example.com",
            ConfigJson = JsonDocument.Parse("{}"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);

        var task = new TaskEntity
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Name = "Task1",
            ScraperConfigId = config.Id,
            SearchTerms = Array.Empty<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<TaskEntity>().Add(task);

        var worker = new WorkerConnection
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Name = "W",
            CurrentConnection = ConnId,
        };
        db.Set<WorkerConnection>().Add(worker);

        await db.SaveChangesAsync();
        return (svc, db, notifier, task, worker);
    }

    private static async Task<RunItem> SeedRunItem(TestWebScraperDbContext db, Guid taskId, Guid workerId, string status, string? connectionId = ConnId)
    {
        var run = new RunItem
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            WorkerId = workerId,
            Status = status,
            RequestedAt = DateTimeOffset.UtcNow,
            SentAt = DateTimeOffset.UtcNow,
        };
        db.Set<RunItem>().Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    [Fact]
    public async Task CreateAndDispatch_HappyPath_ReturnsCreatedAndCallsNotifierOnce()
    {
        var (svc, db, notifier, task, worker) = await Build();
        notifier.Setup(n => n.SendReceiveTaskAsync(ConnId, It.IsAny<QueueTaskDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var result = await svc.CreateAndDispatchAsync(UserId, task.Id, worker.Id);

        Assert.Equal(RunDispatchOutcome.Created, result.Outcome);
        Assert.NotNull(result.RunItemId);
        notifier.Verify(n => n.SendReceiveTaskAsync(ConnId, It.IsAny<QueueTaskDto>(), It.IsAny<CancellationToken>()), Times.Once);

        var run = await db.Set<RunItem>().SingleAsync(r => r.Id == result.RunItemId);
        Assert.Equal(RunItemStatus.Sent, run.Status);
    }

    [Fact]
    public async Task CreateAndDispatch_WorkerNotFound_ReturnsNotFound()
    {
        var (svc, _, _, task, _) = await Build();

        var result = await svc.CreateAndDispatchAsync(UserId, task.Id, Guid.NewGuid());

        Assert.Equal(RunDispatchOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_WorkerOtherUser_ReturnsForbidden()
    {
        var (svc, db, _, task, _) = await Build();
        var otherWorker = new WorkerConnection { Id = Guid.NewGuid(), UserId = "other-user", Name = "OW", CurrentConnection = "conn-other" };
        db.Set<WorkerConnection>().Add(otherWorker);
        await db.SaveChangesAsync();

        var result = await svc.CreateAndDispatchAsync(UserId, task.Id, otherWorker.Id);

        Assert.Equal(RunDispatchOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_TaskOtherUser_ReturnsForbidden()
    {
        var (svc, db, _, _, worker) = await Build();
        var config = new ScraperConfigEntity { Id = Guid.NewGuid(), UserId = "other-user", Name = "C", Domain = "x.com", ConfigJson = JsonDocument.Parse("{}"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var otherTask = new TaskEntity { Id = Guid.NewGuid(), UserId = "other-user", Name = "OT", ScraperConfigId = config.Id, SearchTerms = Array.Empty<string>(), CreatedAt = DateTimeOffset.UtcNow };
        db.Set<ScraperConfigEntity>().Add(config);
        db.Set<TaskEntity>().Add(otherTask);
        await db.SaveChangesAsync();

        var result = await svc.CreateAndDispatchAsync(UserId, otherTask.Id, worker.Id);

        Assert.Equal(RunDispatchOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_WorkerOffline_ReturnsWorkerOffline()
    {
        var (svc, db, _, task, worker) = await Build();
        worker.CurrentConnection = null;
        await db.SaveChangesAsync();

        var result = await svc.CreateAndDispatchAsync(UserId, task.Id, worker.Id);

        Assert.Equal(RunDispatchOutcome.WorkerOffline, result.Outcome);
    }

    [Fact]
    public async Task CreateAndDispatch_NotifierThrows_ReturnsSendFailed()
    {
        var (svc, db, notifier, task, worker) = await Build();
        notifier.Setup(n => n.SendReceiveTaskAsync(It.IsAny<string>(), It.IsAny<QueueTaskDto>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("disconnected"));

        var result = await svc.CreateAndDispatchAsync(UserId, task.Id, worker.Id);

        Assert.Equal(RunDispatchOutcome.SendFailed, result.Outcome);
        Assert.NotNull(result.RunItemId);

        var run = await db.Set<RunItem>().SingleAsync(r => r.Id == result.RunItemId);
        Assert.Equal(RunItemStatus.Failed, run.Status);
        Assert.NotNull(run.ErrorMessage);
    }

    [Theory]
    [InlineData("RecordProgress")]
    [InlineData("Complete")]
    [InlineData("Fail")]
    [InlineData("MarkPaused")]
    public async Task HubProgress_ConnectionIdMismatch_DropsSilently(string method)
    {
        var (svc, db, _, task, worker) = await Build();
        var run = await SeedRunItem(db, task.Id, worker.Id, RunItemStatus.Sent);
        var wrongConn = "wrong-conn";

        switch (method)
        {
            case "RecordProgress":
                await svc.RecordProgressAsync(wrongConn, new TaskProgressDto { TaskId = run.Id.ToString(), Progress = 50 });
                break;
            case "Complete":
                await svc.CompleteAsync(wrongConn, new TaskCompleteDto { TaskId = run.Id.ToString(), Result = new TaskResultDto { Iterations = JsonDocument.Parse("[]").RootElement } });
                break;
            case "Fail":
                await svc.FailAsync(wrongConn, new TaskErrorDto { TaskId = run.Id.ToString(), Error = "err" });
                break;
            case "MarkPaused":
                await svc.MarkPausedAsync(wrongConn, new TaskPausedDto { TaskId = run.Id.ToString() });
                break;
        }

        var reloaded = await db.Set<RunItem>().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(RunItemStatus.Sent, reloaded.Status); // unchanged
    }

    [Fact]
    public async Task RecordProgress_ValidConnection_Sent_TransitionsToRunning()
    {
        var (svc, db, _, task, worker) = await Build();
        var run = await SeedRunItem(db, task.Id, worker.Id, RunItemStatus.Sent);

        await svc.RecordProgressAsync(ConnId, new TaskProgressDto
        {
            TaskId = run.Id.ToString(),
            Progress = 42,
            CurrentTerm = "alpha",
            CurrentStep = "search",
            Phase = "loop",
        });

        var reloaded = await db.Set<RunItem>().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(RunItemStatus.Running, reloaded.Status);
        Assert.NotNull(reloaded.StartedAt);
        Assert.Equal(42, reloaded.ProgressPercent);
        Assert.Equal("alpha", reloaded.CurrentTerm);
        Assert.Equal("search", reloaded.CurrentStep);
    }
}
