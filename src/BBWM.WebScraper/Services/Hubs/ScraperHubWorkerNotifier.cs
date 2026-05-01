using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Hubs;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BBWM.WebScraper.Services.Hubs;

public class ScraperHubWorkerNotifier : IWorkerNotifier
{
    private readonly IHubContext<ScraperHub> _hub;

    public ScraperHubWorkerNotifier(IHubContext<ScraperHub> hub)
    {
        _hub = hub;
    }

    public Task SendReceiveTaskAsync(string connectionId, QueueTaskDto task, CancellationToken ct = default)
        => _hub.Clients.Client(connectionId).SendAsync("ReceiveTask", task, ct);

    public Task SendCancelTaskAsync(string connectionId, string taskId, CancellationToken ct = default)
        => _hub.Clients.Client(connectionId).SendAsync("CancelTask", taskId, ct);

    public Task SendResumeAfterPauseAsync(string connectionId, string taskId, CancellationToken ct = default)
        => _hub.Clients.Client(connectionId).SendAsync("ResumeAfterPause", taskId, ct);

    // D4.b — server→client aggregate batch progress, scoped to the batch owner's user group.
    public Task SendBatchProgressToUserAsync(string userId, BatchProgressDto progress, CancellationToken ct = default)
        => _hub.Clients.Group($"user:{userId}").SendAsync("BatchProgress", progress, ct);

    // D5.d.c — notify subscribers when a shared config they're tracking is deleted.
    public Task SendConfigDeletedToUserAsync(string userId, Guid configId, CancellationToken ct = default)
        => _hub.Clients.Group($"user:{userId}").SendAsync("ScraperConfigDeleted", configId.ToString(), ct);
}
