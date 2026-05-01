using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum CancelRunOutcome { Cancelled, NotFound, Forbidden, NotCancellable }

public interface IRunService
{
    // Hub-driven progress recording. connectionId is verified against run.Worker.CurrentConnection (D4 carry).
    Task RecordProgressAsync(string connectionId, TaskProgressDto payload, CancellationToken ct = default);
    Task CompleteAsync(string connectionId, TaskCompleteDto payload, CancellationToken ct = default);
    Task FailAsync(string connectionId, TaskErrorDto payload, CancellationToken ct = default);
    Task MarkPausedAsync(string connectionId, TaskPausedDto payload, CancellationToken ct = default);

    Task<RunItemDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<PagedResultDto<RunListItemDto>> ListAsync(string userId, RunListQueryDto query, CancellationToken ct = default);
    Task<CancelRunOutcome> CancelAsync(string userId, Guid runId, CancellationToken ct = default);
    Task<byte[]?> ExportCsvAsync(string userId, Guid runId, CancellationToken ct = default);
}
