using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BBWM.WebScraper.Services.Implementations;

public class RunBatchService : IRunBatchService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly IQueueExpansionService _expander;
    private readonly IWorkerNotifier _notifier;
    private readonly IRunCsvExporter _csv;
    private readonly ILogger<RunBatchService> _log;

    // Matches the camelCase pattern used elsewhere in the module (TaskService, RunService) so
    // JSON exported here aligns with what the rest of the system writes.
    private static readonly JsonSerializerOptions _camelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public RunBatchService(
        IDbContext db,
        IMapper mapper,
        IQueueExpansionService expander,
        IWorkerNotifier notifier,
        IRunCsvExporter csv,
        ILogger<RunBatchService> log)
    {
        _db = db;
        _mapper = mapper;
        _expander = expander;
        _notifier = notifier;
        _csv = csv;
        _log = log;
    }

    public async Task<RunBatchDispatchResult> CreateAndDispatchAsync(string userId, Guid taskId, Guid workerId, CancellationToken ct = default)
    {
        var worker = await _db.Set<WorkerConnection>().FirstOrDefaultAsync(w => w.Id == workerId, ct);
        if (worker is null) return new(RunBatchOutcome.NotFound, null, 0, 0, "Worker not found");
        if (worker.UserId != userId) return new(RunBatchOutcome.Forbidden, null, 0, 0, "Worker does not belong to user");
        if (string.IsNullOrEmpty(worker.CurrentConnection)) return new(RunBatchOutcome.WorkerOffline, null, 0, 0, "Worker is offline");

        var preview = await _expander.ExpandAsync(userId, taskId, ct);
        switch (preview.Outcome)
        {
            case ExpansionOutcome.NotFound: return new(RunBatchOutcome.NotFound, null, 0, 0, preview.Error);
            case ExpansionOutcome.Forbidden: return new(RunBatchOutcome.Forbidden, null, 0, 0, preview.Error);
            case ExpansionOutcome.BatchEmpty: return new(RunBatchOutcome.BatchEmpty, null, 0, 0, preview.Error);
            case ExpansionOutcome.BatchTooLarge: return new(RunBatchOutcome.BatchTooLarge, null, 0, 0, preview.Error);
            case ExpansionOutcome.NestedLoopUnsupported: return new(RunBatchOutcome.NestedLoopUnsupported, null, 0, 0, preview.Error);
        }

        var task = await _db.Set<TaskEntity>().Include(t => t.Blocks).FirstAsync(t => t.Id == taskId, ct);
        var configIds = preview.Results.Select(r => r.ScraperConfigId).Distinct().ToList();
        var configs = await _db.Set<ScraperConfigEntity>().Where(c => configIds.Contains(c.Id)).ToListAsync(ct);
        var sharedIds = configs.Where(c => c.Shared).Select(c => c.Id).ToHashSet();

        var snapshot = JsonSerializer.SerializeToDocument(new
        {
            expandedAt = DateTimeOffset.UtcNow,
            treeSnapshot = task.Blocks.Select(b => new
            {
                id = b.Id,
                taskId = b.TaskId,
                parentBlockId = b.ParentBlockId,
                blockType = b.BlockType.ToString(),
                orderIndex = b.OrderIndex,
                config = b.ConfigJsonb.RootElement,
            }),
            configSnapshots = configs.ToDictionary(
                c => c.Id.ToString(),
                c => c.ConfigJson.RootElement),
        });

        var batchId = Guid.NewGuid();
        var batch = new RunBatch
        {
            Id = batchId,
            TaskId = task.Id,
            UserId = userId,
            WorkerId = worker.Id,
            PopulateSnapshot = snapshot,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Set<RunBatch>().Add(batch);

        var runItems = new List<RunItem>(preview.Results.Count);
        foreach (var r in preview.Results)
        {
            var assignmentsJson = JsonSerializer.SerializeToDocument(
                r.Assignments.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
            var run = new RunItem
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                WorkerId = worker.Id,
                BatchId = batchId,
                ScraperConfigId = r.ScraperConfigId,
                Status = RunItemStatus.Pending,
                RequestedAt = DateTimeOffset.UtcNow,
                IterationLabel = r.IterationLabel,
                IterationAssignments = assignmentsJson,
            };
            _db.Set<RunItem>().Add(run);
            runItems.Add(run);
        }
        await _db.SaveChangesAsync(ct);

        var connectionId = worker.CurrentConnection!;
        int dispatched = 0, failed = 0;
        for (int i = 0; i < preview.Results.Count; i++)
        {
            var r = preview.Results[i];
            var run = runItems[i];

            var queueDto = new QueueTaskDto
            {
                Id = run.Id.ToString(),
                ConfigId = r.ScraperConfigId.ToString(),
                ConfigName = r.ConfigName,
                SearchTerms = r.SearchTerms,
                Priority = 0,
                CreatedAt = run.RequestedAt,
                InlineConfig = sharedIds.Contains(r.ScraperConfigId) ? null : r.PatchedConfigJson,
                IterationLabel = r.IterationLabel,
                IterationAssignments = r.Assignments.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            };

            try
            {
                await _notifier.SendReceiveTaskAsync(connectionId, queueDto, ct);
                run.Status = RunItemStatus.Sent;
                run.SentAt = DateTimeOffset.UtcNow;
                dispatched++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Per-item dispatch failed for run {RunId} in batch {BatchId}", run.Id, batchId);
                run.Status = RunItemStatus.Failed;
                run.ErrorMessage = $"Worker disconnected before task could be sent: {ex.Message}";
                run.CompletedAt = DateTimeOffset.UtcNow;
                failed++;
            }
        }
        await _db.SaveChangesAsync(ct);

        return new RunBatchDispatchResult(RunBatchOutcome.Created, batchId, dispatched, failed, null);
    }

    public async Task<RunBatchDetailDto?> GetAsync(string userId, Guid batchId, CancellationToken ct = default)
    {
        var batch = await _db.Set<RunBatch>()
            .AsNoTracking()
            .Include(b => b.Task)
            .Include(b => b.Worker)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || batch.UserId != userId) return null;

        var runItems = await _db.Set<RunItem>()
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);

        return new RunBatchDetailDto
        {
            Id = batch.Id,
            TaskId = batch.TaskId,
            TaskName = batch.Task?.Name ?? "",
            WorkerId = batch.WorkerId,
            WorkerName = batch.Worker?.Name ?? "",
            CreatedAt = batch.CreatedAt,
            RunItems = _mapper.Map<List<RunItemDto>>(runItems),
        };
    }

    public async Task<PagedResultDto<RunBatchListItemDto>> ListAsync(string userId, RunBatchListQueryDto query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize switch { < 1 => 1, > 100 => 100, var n => n };

        var q = _db.Set<RunBatch>()
            .AsNoTracking()
            .Include(b => b.Task)
            .Include(b => b.Worker)
            .Where(b => b.UserId == userId);

        if (query.TaskId.HasValue) q = q.Where(b => b.TaskId == query.TaskId.Value);
        if (query.From.HasValue)   q = q.Where(b => b.CreatedAt >= query.From.Value);
        if (query.To.HasValue)     q = q.Where(b => b.CreatedAt <= query.To.Value);

        var total = await q.CountAsync(ct);
        var batches = await q
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var batchIds = batches.Select(b => b.Id).ToList();
        var aggregates = batchIds.Count == 0
            ? new List<BatchAggregate>()
            : await _db.Set<RunItem>()
                .AsNoTracking()
                .Where(r => r.BatchId != null && batchIds.Contains(r.BatchId!.Value))
                .GroupBy(r => r.BatchId!.Value)
                .Select(g => new BatchAggregate(
                    g.Key,
                    g.Count(),
                    g.Count(r => r.Status == RunItemStatus.Completed),
                    g.Count(r => r.Status == RunItemStatus.Failed || r.Status == RunItemStatus.Cancelled),
                    g.Count(r => r.Status == RunItemStatus.Pending || r.Status == RunItemStatus.Sent
                              || r.Status == RunItemStatus.Running || r.Status == RunItemStatus.Paused)))
                .ToListAsync(ct);
        var aggMap = aggregates.ToDictionary(a => a.BatchId);

        var items = batches.Select(b =>
        {
            aggMap.TryGetValue(b.Id, out var a);
            return new RunBatchListItemDto
            {
                Id = b.Id,
                TaskId = b.TaskId,
                TaskName = b.Task?.Name ?? "",
                WorkerId = b.WorkerId,
                WorkerName = b.Worker?.Name ?? "",
                CreatedAt = b.CreatedAt,
                TotalItems     = a is null ? 0 : a.Total,
                CompletedCount = a is null ? 0 : a.Completed,
                FailedCount    = a is null ? 0 : a.Failed,
                PendingCount   = a is null ? 0 : a.Pending,
            };
        }).ToList();

        return new PagedResultDto<RunBatchListItemDto>
        {
            Items = items, Total = total, Page = page, PageSize = pageSize,
        };
    }

    public async Task<RunBatchExportResult> ExportAsync(string userId, Guid batchId, string format, CancellationToken ct = default)
    {
        var fmt = (format ?? "").ToLowerInvariant();
        if (fmt != "json" && fmt != "csv")
            return new RunBatchExportResult(RunBatchExportOutcome.BadFormat, null, null, null);

        var batch = await _db.Set<RunBatch>()
            .AsNoTracking()
            .Include(b => b.Task)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return new RunBatchExportResult(RunBatchExportOutcome.NotFound, null, null, null);
        if (batch.UserId != userId) return new RunBatchExportResult(RunBatchExportOutcome.Forbidden, null, null, null);

        var items = await _db.Set<RunItem>()
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);

        if (fmt == "json")
        {
            var envelope = new
            {
                batchId = batch.Id,
                items = items.Select(run => new
                {
                    runId = run.Id,
                    iterationLabel = run.IterationLabel,
                    status = run.Status.ToString().ToLowerInvariant(),
                    // ResultJsonb?.RootElement gives JsonElement? — System.Text.Json serialises it
                    // as raw embedded JSON (not stringified), and writes `null` when it's null.
                    result = run.ResultJsonb?.RootElement,
                }),
            };
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, _camelCaseJson);
            return new RunBatchExportResult(RunBatchExportOutcome.Ok, jsonBytes, $"batch-{batch.Id}.json", "application/json");
        }

        var csvBytes = _csv.ExportBatch(batch, items, null);
        return new RunBatchExportResult(RunBatchExportOutcome.Ok, csvBytes, $"batch-{batch.Id}.csv", "text/csv");
    }

    private sealed record BatchAggregate(Guid BatchId, int Total, int Completed, int Failed, int Pending);
}
