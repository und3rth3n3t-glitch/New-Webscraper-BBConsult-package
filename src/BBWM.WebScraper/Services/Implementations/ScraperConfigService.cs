using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class ScraperConfigService : IScraperConfigService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;

    public ScraperConfigService(IDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<List<ScraperConfigDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var rows = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return _mapper.Map<List<ScraperConfigDto>>(rows);
    }

    public async Task<ScraperConfigDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        return row is null ? null : _mapper.Map<ScraperConfigDto>(row);
    }

    public async Task<ScraperConfigDto> CreateAsync(string userId, CreateScraperConfigDto dto, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Domain = dto.Domain,
            ConfigJson = JsonDocument.Parse(dto.ConfigJson.GetRawText()),
            SchemaVersion = dto.SchemaVersion <= 0 ? 3 : dto.SchemaVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Set<ScraperConfigEntity>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return _mapper.Map<ScraperConfigDto>(entity);
    }

    public async Task<ScraperConfigDto?> UpdateAsync(string userId, Guid id, CreateScraperConfigDto dto, CancellationToken ct = default)
    {
        var entity = await _db.Set<ScraperConfigEntity>()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (entity is null) return null;

        entity.Name = dto.Name;
        entity.Domain = dto.Domain;
        entity.ConfigJson = JsonDocument.Parse(dto.ConfigJson.GetRawText());
        if (dto.SchemaVersion > 0) entity.SchemaVersion = dto.SchemaVersion;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return _mapper.Map<ScraperConfigDto>(entity);
    }
}
