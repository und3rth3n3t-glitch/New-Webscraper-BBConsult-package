using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class TaskService : ITaskService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;

    public TaskService(IDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<List<TaskDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var rows = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.ScraperConfig)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return _mapper.Map<List<TaskDto>>(rows);
    }

    public async Task<TaskDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.ScraperConfig)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        return row is null ? null : _mapper.Map<TaskDto>(row);
    }

    public async Task<TaskDto?> CreateAsync(string userId, CreateTaskDto dto, CancellationToken ct = default)
    {
        var configExists = await _db.Set<ScraperConfigEntity>()
            .AnyAsync(c => c.Id == dto.ScraperConfigId && c.UserId == userId, ct);
        if (!configExists) return null;

        var entity = new TaskEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            ScraperConfigId = dto.ScraperConfigId,
            SearchTerms = dto.SearchTerms ?? Array.Empty<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Set<TaskEntity>().Add(entity);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(userId, entity.Id, ct);
    }
}
