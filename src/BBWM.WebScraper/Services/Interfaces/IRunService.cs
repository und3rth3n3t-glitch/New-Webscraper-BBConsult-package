using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum RunDispatchOutcome
{
    Created,
    NotFound,
    Forbidden,
    WorkerOffline,
    SendFailed,
}

public record RunDispatchResult(RunDispatchOutcome Outcome, Guid? RunItemId, string? Error);

public interface IRunService
{
    Task<RunDispatchResult> CreateAndDispatchAsync(string userId, Guid taskId, Guid workerId, CancellationToken ct = default);
    Task RecordProgressAsync(string connectionId, TaskProgressDto payload, CancellationToken ct = default);
    Task CompleteAsync(string connectionId, TaskCompleteDto payload, CancellationToken ct = default);
    Task FailAsync(string connectionId, TaskErrorDto payload, CancellationToken ct = default);
    Task MarkPausedAsync(string connectionId, TaskPausedDto payload, CancellationToken ct = default);
    Task<RunItemDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
}
