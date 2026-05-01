using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.Services;

public class WorkerServiceTests
{
    private static (WorkerService svc, TestWebScraperDbContext db) Build()
    {
        var db = TestDb.CreateInMemory();
        var svc = new WorkerService(db, TestDb.CreateMapper());
        return (svc, db);
    }

    [Fact]
    public async Task RegisterAsync_NoExisting_CreatesRow()
    {
        var (svc, db) = Build();
        var userId = "user-1";

        var worker = await svc.RegisterAsync(userId, "Laptop", "1.0.0", "conn-1");

        Assert.Equal("Laptop", worker.Name);
        Assert.Equal(userId, worker.UserId);
        Assert.Equal("conn-1", worker.CurrentConnection);
        Assert.Equal("1.0.0", worker.ExtensionVersion);
        Assert.NotNull(worker.LastConnectedAt);
        Assert.NotNull(worker.LastSeenAt);
        Assert.Equal(1, await db.Set<WorkerConnection>().CountAsync());
    }

    [Fact]
    public async Task RegisterAsync_SameUserAndName_ReusesRow()
    {
        var (svc, db) = Build();
        var userId = "user-2";

        var first = await svc.RegisterAsync(userId, "Laptop", "1.0.0", "conn-1");
        var second = await svc.RegisterAsync(userId, "Laptop", "1.0.1", "conn-2");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("conn-2", second.CurrentConnection);
        Assert.Equal("1.0.1", second.ExtensionVersion);
        Assert.Equal(1, await db.Set<WorkerConnection>().CountAsync());
    }

    [Fact]
    public async Task RegisterAsync_BlankClientName_DefaultsToMyBrowser()
    {
        var (svc, db) = Build();

        var worker = await svc.RegisterAsync("user-3", " ", "1.0.0", "conn-blank");

        Assert.Equal("My Browser", worker.Name);
    }

    [Fact]
    public async Task HandleDisconnectAsync_ClearsConnection_AndFailsInFlightRuns()
    {
        var (svc, db) = Build();
        var userId = "user-4";
        var worker = await svc.RegisterAsync(userId, "W", "1", "conn-x");

        var sentRun = new RunItem { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), WorkerId = worker.Id, Status = RunItemStatus.Sent, RequestedAt = DateTimeOffset.UtcNow };
        var runningRun = new RunItem { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), WorkerId = worker.Id, Status = RunItemStatus.Running, RequestedAt = DateTimeOffset.UtcNow };
        var pausedRun = new RunItem { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), WorkerId = worker.Id, Status = RunItemStatus.Paused, RequestedAt = DateTimeOffset.UtcNow };
        var completedRun = new RunItem { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), WorkerId = worker.Id, Status = RunItemStatus.Completed, RequestedAt = DateTimeOffset.UtcNow };
        db.Set<RunItem>().AddRange(sentRun, runningRun, pausedRun, completedRun);
        await db.SaveChangesAsync();

        await svc.HandleDisconnectAsync("conn-x");

        var reloaded = await db.Set<WorkerConnection>().SingleAsync(w => w.Id == worker.Id);
        Assert.Null(reloaded.CurrentConnection);
        Assert.NotNull(reloaded.LastSeenAt);

        Assert.Equal(RunItemStatus.Failed, (await db.Set<RunItem>().SingleAsync(r => r.Id == sentRun.Id)).Status);
        Assert.Equal(RunItemStatus.Failed, (await db.Set<RunItem>().SingleAsync(r => r.Id == runningRun.Id)).Status);
        Assert.Equal(RunItemStatus.Failed, (await db.Set<RunItem>().SingleAsync(r => r.Id == pausedRun.Id)).Status);
        Assert.Equal(RunItemStatus.Completed, (await db.Set<RunItem>().SingleAsync(r => r.Id == completedRun.Id)).Status);

        Assert.Equal("Worker disconnected", (await db.Set<RunItem>().SingleAsync(r => r.Id == sentRun.Id)).ErrorMessage);
    }

    [Fact]
    public async Task ListAsync_OnlyReturnsCallersWorkers()
    {
        var (svc, db) = Build();
        var userId = "user-5";
        var otherId = "user-other";

        // Register for our user (online + offline)
        var w1 = await svc.RegisterAsync(userId, "Browser A", "1", "conn-a");
        var w2 = await svc.RegisterAsync(userId, "Browser B", "1", "conn-b");
        // Disconnect w2
        await svc.HandleDisconnectAsync("conn-b");
        // Register for another user
        await svc.RegisterAsync(otherId, "Browser X", "1", "conn-x");

        var list = await svc.ListAsync(userId);

        Assert.Equal(2, list.Count);
        Assert.All(list, w => Assert.DoesNotContain("X", w.Name));

        var onlineDto = list.Single(w => w.Name == "Browser A");
        Assert.True(onlineDto.Online);

        var offlineDto = list.Single(w => w.Name == "Browser B");
        Assert.False(offlineDto.Online);
    }
}
