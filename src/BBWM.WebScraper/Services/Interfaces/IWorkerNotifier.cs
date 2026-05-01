using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public interface IWorkerNotifier
{
    Task SendReceiveTaskAsync(string connectionId, QueueTaskDto task, CancellationToken ct = default);
    Task SendCancelTaskAsync(string connectionId, string taskId, CancellationToken ct = default);
    Task SendResumeAfterPauseAsync(string connectionId, string taskId, CancellationToken ct = default);
    // v2.0:
    Task SendBatchProgressToUserAsync(string userId, BatchProgressDto progress, CancellationToken ct = default);   // D4.b
    Task SendConfigDeletedToUserAsync(string userId, Guid configId, CancellationToken ct = default);               // D5.d.c
}
