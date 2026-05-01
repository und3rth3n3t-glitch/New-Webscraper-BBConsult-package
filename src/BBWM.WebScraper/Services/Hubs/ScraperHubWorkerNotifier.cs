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
}
