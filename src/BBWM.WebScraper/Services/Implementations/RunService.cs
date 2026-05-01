using System.Text.Json;
using System.Text.Json.Nodes;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class RunService : IRunService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly IWorkerNotifier _notifier;

    public RunService(IDbContext db, IMapper mapper, IWorkerNotifier notifier)
    {
        _db = db;
        _mapper = mapper;
        _notifier = notifier;
    }

    public async Task<RunDispatchResult> CreateAndDispatchAsync(string userId, Guid taskId, Guid workerId, CancellationToken ct = default)
    {
        var worker = await _db.Set<WorkerConnection>().FirstOrDefaultAsync(w => w.Id == workerId, ct);
        if (worker is null) return new(RunDispatchOutcome.NotFound, null, "Worker not found");
        if (worker.UserId != userId) return new(RunDispatchOutcome.Forbidden, null, "Worker does not belong to user");

        var task = await _db.Set<TaskEntity>()
            .Include(t => t.ScraperConfig)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return new(RunDispatchOutcome.NotFound, null, "Task not found");
        if (task.UserId != userId) return new(RunDispatchOutcome.Forbidden, null, "Task does not belong to user");

        if (string.IsNullOrEmpty(worker.CurrentConnection))
            return new(RunDispatchOutcome.WorkerOffline, null, "Worker is offline");

        var connectionId = worker.CurrentConnection;
        var run = new RunItem
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            WorkerId = worker.Id,
            Status = RunItemStatus.Pending,
            RequestedAt = DateTimeOffset.UtcNow,
        };
        _db.Set<RunItem>().Add(run);
        await _db.SaveChangesAsync(ct);

        var config = task.ScraperConfig!;
        var queueDto = new QueueTaskDto
        {
            Id = run.Id.ToString(),
            ConfigId = config.Id.ToString(),
            ConfigName = config.Name,
            SearchTerms = task.SearchTerms.ToList(),
            Priority = 0,
            CreatedAt = run.RequestedAt,
            Status = "pending",
            InlineConfig = BuildInlineConfig(config),
        };

        try
        {
            await _notifier.SendReceiveTaskAsync(connectionId, queueDto, ct);
        }
        catch (Exception ex)
        {
            run.Status = RunItemStatus.Failed;
            run.ErrorMessage = $"Worker disconnected before task could be sent: {ex.Message}";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new(RunDispatchOutcome.SendFailed, run.Id, run.ErrorMessage);
        }

        run.Status = RunItemStatus.Sent;
        run.SentAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new(RunDispatchOutcome.Created, run.Id, null);
    }

    // Builds the flat ScraperConfig JSON the extension expects as inlineConfig.
    // Takes the stored configJson blob and injects "id" at the top level.
    private static JsonElement BuildInlineConfig(ScraperConfigEntity config)
    {
        var node = JsonNode.Parse(config.ConfigJson.RootElement.GetRawText())!.AsObject();
        node["id"] = config.Id.ToString();
        return JsonSerializer.SerializeToElement(node);
    }

    public async Task RecordProgressAsync(string connectionId, TaskProgressDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;

        if (run.Status == RunItemStatus.Sent || run.Status == RunItemStatus.Paused)
        {
            run.Status = RunItemStatus.Running;
            run.StartedAt ??= DateTimeOffset.UtcNow;
        }

        run.ProgressPercent = payload.Progress;
        run.CurrentTerm = payload.CurrentTerm;
        run.CurrentStep = payload.CurrentStep;
        run.Phase = payload.Phase;

        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(string connectionId, TaskCompleteDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;

        var resultJson = JsonSerializer.Serialize(payload.Result);
        run.ResultJsonb = JsonDocument.Parse(resultJson);
        run.Status = RunItemStatus.Completed;
        run.CompletedAt = payload.CompletedAt == default ? DateTimeOffset.UtcNow : payload.CompletedAt;
        run.ProgressPercent = 100;

        await _db.SaveChangesAsync(ct);
    }

    public async Task FailAsync(string connectionId, TaskErrorDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;

        run.Status = RunItemStatus.Failed;
        run.ErrorMessage = string.IsNullOrEmpty(payload.StepLabel) ? payload.Error : $"[{payload.StepLabel}] {payload.Error}";
        run.CompletedAt = payload.FailedAt == default ? DateTimeOffset.UtcNow : payload.FailedAt;

        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkPausedAsync(string connectionId, TaskPausedDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;

        run.Status = RunItemStatus.Paused;
        run.PauseReason = payload.Reason;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<RunItemDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<RunItem>()
            .AsNoTracking()
            .Include(r => r.Task)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return null;
        if (row.Task is null || row.Task.UserId != userId) return null;
        return _mapper.Map<RunItemDto>(row);
    }

    // D4: load run + verify caller owns the worker. Returns null on any mismatch — silent drop.
    private async Task<RunItem?> LoadAndAuthoriseAsync(string connectionId, string runIdStr, CancellationToken ct)
    {
        if (!Guid.TryParse(runIdStr, out var runId)) return null;
        var run = await _db.Set<RunItem>()
            .Include(r => r.Worker)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return null;
        if (run.Worker is null || run.Worker.CurrentConnection != connectionId) return null;
        return run;
    }
}
