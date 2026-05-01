using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class TaskService : ITaskService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly ITaskValidator _validator;

    public TaskService(IDbContext db, IMapper mapper, ITaskValidator validator)
    {
        _db = db;
        _mapper = mapper;
        _validator = validator;
    }

    public async Task<List<TaskDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var tasks = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.Blocks)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return tasks.Select(BuildDto).ToList();
    }

    public async Task<TaskDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var task = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.Blocks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        return task is null ? null : BuildDto(task);
    }

    public async Task<SaveTaskResult> SaveAsync(string userId, Guid? taskId, SaveTaskDto dto, CancellationToken ct = default)
    {
        var errors = await _validator.ValidateAsync(userId, dto, ct);
        if (errors.Count > 0)
            return new(SaveTaskOutcome.ValidationFailed, null, errors);

        TaskEntity task;
        bool isNew;
        if (taskId.HasValue)
        {
            var existing = await _db.Set<TaskEntity>()
                .Include(t => t.Blocks)
                .FirstOrDefaultAsync(t => t.Id == taskId.Value, ct);
            if (existing is null) return new(SaveTaskOutcome.NotFound, null, new());
            if (existing.UserId != userId) return new(SaveTaskOutcome.Forbidden, null, new());
            // Delete-then-insert blocks (simplest atomic strategy; client supplies stable IDs).
            _db.Set<TaskBlock>().RemoveRange(existing.Blocks);
            existing.Name = dto.Name;
            task = existing;
            isNew = false;
        }
        else
        {
            task = new TaskEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = dto.Name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.Set<TaskEntity>().Add(task);
            isNew = true;
        }

        foreach (var b in dto.Blocks)
        {
            _db.Set<TaskBlock>().Add(new TaskBlock
            {
                Id = b.Id,
                TaskId = task.Id,
                ParentBlockId = b.ParentBlockId,
                BlockType = b.BlockType,
                OrderIndex = b.OrderIndex,
                ConfigJsonb = SerializeBlockConfig(b),
            });
        }
        await _db.SaveChangesAsync(ct);

        // Re-fetch for consistent ordering and tracking-clean output.
        var saved = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.Blocks)
            .FirstAsync(t => t.Id == task.Id, ct);
        return new(isNew ? SaveTaskOutcome.Created : SaveTaskOutcome.Updated, BuildDto(saved), new());
    }

    public async Task<DeleteTaskOutcome> DeleteAsync(string userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await _db.Set<TaskEntity>().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return DeleteTaskOutcome.NotFound;
        if (task.UserId != userId) return DeleteTaskOutcome.Forbidden;
        _db.Set<TaskEntity>().Remove(task);
        await _db.SaveChangesAsync(ct);
        return DeleteTaskOutcome.Deleted;
    }

    private static JsonDocument SerializeBlockConfig(TaskBlockTreeDto b)
    {
        return b.BlockType switch
        {
            BlockType.Loop => JsonSerializer.SerializeToDocument(b.Loop ?? new LoopBlockConfigDto()),
            BlockType.Scrape => JsonSerializer.SerializeToDocument(b.Scrape ?? new ScrapeBlockConfigDto()),
            _ => JsonDocument.Parse("{}"),
        };
    }

    private static TaskDto BuildDto(TaskEntity task)
    {
        var blocks = task.Blocks.Select(b => new TaskBlockTreeDto
        {
            Id = b.Id,
            ParentBlockId = b.ParentBlockId,
            BlockType = b.BlockType,
            OrderIndex = b.OrderIndex,
            Loop = b.BlockType == BlockType.Loop
                ? JsonSerializer.Deserialize<LoopBlockConfigDto>(b.ConfigJsonb.RootElement.GetRawText())
                : null,
            Scrape = b.BlockType == BlockType.Scrape
                ? JsonSerializer.Deserialize<ScrapeBlockConfigDto>(b.ConfigJsonb.RootElement.GetRawText())
                : null,
        }).OrderBy(b => b.OrderIndex).ToList();

        // SearchTerms (legacy compat): union of all loop values for display purposes.
        var searchTerms = task.Blocks
            .Where(b => b.BlockType == BlockType.Loop)
            .SelectMany(b =>
            {
                var loop = JsonSerializer.Deserialize<LoopBlockConfigDto>(b.ConfigJsonb.RootElement.GetRawText());
                return loop?.Values ?? new List<string>();
            })
            .Distinct()
            .ToArray();

        return new TaskDto
        {
            Id = task.Id,
            Name = task.Name,
            CreatedAt = task.CreatedAt,
            SearchTerms = searchTerms,
            Blocks = blocks,
        };
    }
}
