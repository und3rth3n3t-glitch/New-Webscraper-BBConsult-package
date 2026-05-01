using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class RunService : IRunService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly IWorkerNotifier _notifier;
    private readonly IRunCsvExporter _csv;

    public RunService(IDbContext db, IMapper mapper, IWorkerNotifier notifier, IRunCsvExporter csv)
    {
        _db = db;
        _mapper = mapper;
        _notifier = notifier;
        _csv = csv;
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
        await EmitBatchProgressAsync(run, ct);
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
        await EmitBatchProgressAsync(run, ct);
    }

    public async Task FailAsync(string connectionId, TaskErrorDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;

        run.Status = RunItemStatus.Failed;
        run.ErrorMessage = string.IsNullOrEmpty(payload.StepLabel) ? payload.Error : $"[{payload.StepLabel}] {payload.Error}";
        run.CompletedAt = payload.FailedAt == default ? DateTimeOffset.UtcNow : payload.FailedAt;

        await _db.SaveChangesAsync(ct);
        await EmitBatchProgressAsync(run, ct);
    }

    public async Task MarkPausedAsync(string connectionId, TaskPausedDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;

        run.Status = RunItemStatus.Paused;
        run.PauseReason = payload.Reason;

        await _db.SaveChangesAsync(ct);
        await EmitBatchProgressAsync(run, ct);
    }

    public async Task<RunItemDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<RunItem>()
            .AsNoTracking()
            .Include(r => r.Task)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null || row.Task is null || row.Task.UserId != userId) return null;
        return _mapper.Map<RunItemDto>(row);
    }

    public async Task<PagedResultDto<RunListItemDto>> ListAsync(string userId, RunListQueryDto query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch { < 1 => 1, > 100 => 100, var n => n };

        var q = _db.Set<RunItem>()
            .AsNoTracking()
            .Include(r => r.Task)
            .Include(r => r.Worker)
            .Where(r => r.Task!.UserId == userId);

        if (query.TaskId.HasValue)   q = q.Where(r => r.TaskId == query.TaskId.Value);
        if (query.WorkerId.HasValue) q = q.Where(r => r.WorkerId == query.WorkerId.Value);
        if (query.BatchId.HasValue)  q = q.Where(r => r.BatchId == query.BatchId.Value);
        if (query.Status.HasValue)   q = q.Where(r => r.Status == query.Status.Value);
        if (query.From.HasValue)     q = q.Where(r => r.RequestedAt >= query.From.Value);
        if (query.To.HasValue)       q = q.Where(r => r.RequestedAt <= query.To.Value);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new RunListItemDto
        {
            Id = r.Id,
            TaskId = r.TaskId,
            TaskName = r.Task?.Name ?? "",
            WorkerId = r.WorkerId,
            WorkerName = r.Worker?.Name ?? "",
            BatchId = r.BatchId,
            Status = r.Status,
            RequestedAt = r.RequestedAt,
            CompletedAt = r.CompletedAt,
            IterationLabel = r.IterationLabel,
            ProgressPercent = r.ProgressPercent,
        }).ToList();

        return new PagedResultDto<RunListItemDto>
        {
            Items = items, Total = total, Page = page, PageSize = pageSize,
        };
    }

    public async Task<CancelRunOutcome> CancelAsync(string userId, Guid runId, CancellationToken ct = default)
    {
        var run = await _db.Set<RunItem>()
            .Include(r => r.Task)
            .Include(r => r.Worker)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Task is null) return CancelRunOutcome.NotFound;
        if (run.Task.UserId != userId) return CancelRunOutcome.Forbidden;
        if (run.Status is RunItemStatus.Completed or RunItemStatus.Failed or RunItemStatus.Cancelled)
            return CancelRunOutcome.NotCancellable;

        run.Status = RunItemStatus.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (run.Worker?.CurrentConnection is not null)
        {
            try { await _notifier.SendCancelTaskAsync(run.Worker.CurrentConnection, run.Id.ToString(), ct); }
            catch { /* best-effort */ }
        }
        await EmitBatchProgressAsync(run, ct);
        return CancelRunOutcome.Cancelled;
    }

    public async Task<byte[]?> ExportCsvAsync(string userId, Guid runId, CancellationToken ct = default)
    {
        var run = await _db.Set<RunItem>()
            .AsNoTracking()
            .Include(r => r.Task)
            .Include(r => r.Batch)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Task is null || run.Task.UserId != userId) return null;
        var liveConfig = run.ScraperConfigId.HasValue
            ? await _db.Set<ScraperConfigEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == run.ScraperConfigId.Value, ct)
            : null;
        return _csv.ExportRun(run, liveConfig, run.Batch);
    }

    // D4 carry: load run + verify caller's connection matches run's worker. Silent-drop on mismatch.
    private async Task<RunItem?> LoadAndAuthoriseAsync(string connectionId, string runIdStr, CancellationToken ct)
    {
        if (!Guid.TryParse(runIdStr, out var runId)) return null;
        var run = await _db.Set<RunItem>()
            .Include(r => r.Worker)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Worker is null || run.Worker.CurrentConnection != connectionId) return null;
        return run;
    }

    // D4.b — aggregate batch state and emit BatchProgress to the batch owner's group.
    private async Task EmitBatchProgressAsync(RunItem run, CancellationToken ct)
    {
        if (run.BatchId is null) return;
        var batchId = run.BatchId.Value;
        var batch = await _db.Set<RunBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return;

        var counts = await _db.Set<RunItem>()
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Completed = g.Count(r => r.Status == RunItemStatus.Completed),
                Failed = g.Count(r => r.Status == RunItemStatus.Failed || r.Status == RunItemStatus.Cancelled),
                Running = g.Count(r => r.Status == RunItemStatus.Sent || r.Status == RunItemStatus.Running || r.Status == RunItemStatus.Paused),
                Pending = g.Count(r => r.Status == RunItemStatus.Pending),
            })
            .FirstOrDefaultAsync(ct);
        if (counts is null) return;

        var dto = new BatchProgressDto
        {
            BatchId = batchId,
            Total = counts.Total,
            Completed = counts.Completed,
            Failed = counts.Failed,
            Running = counts.Running,
            Pending = counts.Pending,
            OverallPercent = counts.Total == 0 ? 0 : ((counts.Completed + counts.Failed) * 100 / counts.Total),
        };
        try { await _notifier.SendBatchProgressToUserAsync(batch.UserId, dto, ct); }
        catch { /* best-effort */ }
    }
}
