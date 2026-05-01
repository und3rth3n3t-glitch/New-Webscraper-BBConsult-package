using AutoMapper;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Tests.Services;

public class WorkerServiceTests
{
    private static WorkerService CreateService(TestWebScraperDbContext db)
    {
        var mapper = TestDb.CreateMapper();
        return new WorkerService(db, mapper);
    }

    [Fact]
    public async Task Register_BlankName_DefaultsToMyBrowser()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);

        var worker = await svc.RegisterAsync("user1", "", "1.0", "conn-1");

        Assert.Equal("My Browser", worker.Name);
    }

    [Fact]
    public async Task Register_SameUserSameName_DeduplicatesRow()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);

        var w1 = await svc.RegisterAsync("user1", "Home", "1.0", "conn-a");
        var w2 = await svc.RegisterAsync("user1", "Home", "1.1", "conn-b");

        Assert.Equal(w1.Id, w2.Id);
        Assert.Equal("conn-b", w2.CurrentConnection);
        var count = await db.Set<WorkerConnection>().CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task HandleDisconnect_ClearsCurrentConnection_AndFailsInFlightRuns()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);

        var worker = await svc.RegisterAsync("user1", "W1", "1.0", "conn-x");

        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);

        var runSent = new RunItem { Id = Guid.NewGuid(), TaskId = task.Id, WorkerId = worker.Id, Status = RunItemStatus.Sent, RequestedAt = DateTimeOffset.UtcNow };
        var runRunning = new RunItem { Id = Guid.NewGuid(), TaskId = task.Id, WorkerId = worker.Id, Status = RunItemStatus.Running, RequestedAt = DateTimeOffset.UtcNow };
        var runPaused = new RunItem { Id = Guid.NewGuid(), TaskId = task.Id, WorkerId = worker.Id, Status = RunItemStatus.Paused, RequestedAt = DateTimeOffset.UtcNow };
        var runCompleted = new RunItem { Id = Guid.NewGuid(), TaskId = task.Id, WorkerId = worker.Id, Status = RunItemStatus.Completed, RequestedAt = DateTimeOffset.UtcNow };
        db.Set<RunItem>().AddRange(runSent, runRunning, runPaused, runCompleted);
        await db.SaveChangesAsync();

        await svc.HandleDisconnectAsync("conn-x");

        var updatedWorker = await db.Set<WorkerConnection>().FindAsync(worker.Id);
        Assert.Null(updatedWorker!.CurrentConnection);

        var sent = await db.Set<RunItem>().FindAsync(runSent.Id);
        Assert.Equal(RunItemStatus.Failed, sent!.Status);
        Assert.Equal("Worker disconnected", sent.ErrorMessage);

        var running = await db.Set<RunItem>().FindAsync(runRunning.Id);
        Assert.Equal(RunItemStatus.Failed, running!.Status);

        var paused = await db.Set<RunItem>().FindAsync(runPaused.Id);
        Assert.Equal(RunItemStatus.Failed, paused!.Status);

        // Completed run should not be touched
        var completed = await db.Set<RunItem>().FindAsync(runCompleted.Id);
        Assert.Equal(RunItemStatus.Completed, completed!.Status);
    }

    [Fact]
    public async Task HandleDisconnect_UnknownConnection_IsNoOp()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);

        // Should not throw
        await svc.HandleDisconnectAsync("unknown-conn");
    }

    [Fact]
    public async Task List_ReturnsOnlyCallerWorkers_WithOnlineProjection()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);

        await svc.RegisterAsync("user1", "A", "1.0", "conn-a");
        var w2 = await svc.RegisterAsync("user1", "B", "1.0", "conn-b");
        await svc.RegisterAsync("user2", "C", "1.0", "conn-c");

        // Disconnect B
        await svc.HandleDisconnectAsync("conn-b");

        var list = await svc.ListAsync("user1");

        Assert.Equal(2, list.Count);
        var wA = list.Single(w => w.Name == "A");
        var wB = list.Single(w => w.Name == "B");
        Assert.True(wA.Online);
        Assert.False(wB.Online);
    }
}
