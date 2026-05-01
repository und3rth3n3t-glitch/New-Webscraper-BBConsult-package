using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class ScraperConfigService : IScraperConfigService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly IWorkerNotifier _notifier;

    public ScraperConfigService(IDbContext db, IMapper mapper, IWorkerNotifier notifier)
    {
        _db = db;
        _mapper = mapper;
        _notifier = notifier;
    }

    public async Task<List<ScraperConfigDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var rows = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return await ProjectAsync(rows, ct);
    }

    public async Task<List<ScraperConfigDto>> ListSharedAsync(string userId, CancellationToken ct = default)
    {
        var rows = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.Shared)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return await ProjectAsync(rows, ct);
    }

    public async Task<ScraperConfigDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (row is null) return null;
        return (await ProjectAsync(new[] { row }, ct)).First();
    }

    public async Task<CreateScraperConfigResult> CreateAsync(string userId, CreateScraperConfigDto dto, Guid? workerId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // D5.a-style idempotency: if SuggestedId hits an existing row owned by user, compare body.
        if (dto.SuggestedId.HasValue)
        {
            var existing = await _db.Set<ScraperConfigEntity>()
                .FirstOrDefaultAsync(c => c.Id == dto.SuggestedId.Value && c.UserId == userId, ct);
            if (existing is not null)
            {
                var sameName = existing.Name == dto.Name;
                var sameDomain = existing.Domain == dto.Domain;
                var sameJson = existing.ConfigJson.RootElement.GetRawText() == dto.ConfigJson.GetRawText();
                var existingDto = (await ProjectAsync(new[] { existing }, ct)).First();
                return new(sameName && sameDomain && sameJson
                    ? CreateScraperConfigOutcome.Idempotent
                    : CreateScraperConfigOutcome.Conflict, existingDto);
            }
        }

        var entity = new ScraperConfigEntity
        {
            Id = dto.SuggestedId ?? Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Domain = dto.Domain,
            ConfigJson = JsonDocument.Parse(dto.ConfigJson.GetRawText()),
            SchemaVersion = dto.SchemaVersion <= 0 ? 3 : dto.SchemaVersion,
            Shared = dto.Shared,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        if (workerId.HasValue)
        {
            entity.OriginClientId = workerId.Value.ToString();
            entity.LastSyncedAt = now;
        }
        _db.Set<ScraperConfigEntity>().Add(entity);
        await _db.SaveChangesAsync(ct);

        var createdDto = (await ProjectAsync(new[] { entity }, ct)).First();
        return new(CreateScraperConfigOutcome.Created, createdDto);
    }

    public async Task<UpdateScraperConfigResult> UpdateAsync(string userId, Guid id, CreateScraperConfigDto dto, int? ifMatchVersion, Guid? workerId, CancellationToken ct = default)
    {
        // D1.c: explicit transaction wraps load → check → mutate → save.
        // EF concurrency token (Version with IsConcurrencyToken) catches races at SQL level.
        using var tx = await _db.Database.BeginTransactionAsync(ct);
        var entity = await _db.Set<ScraperConfigEntity>()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (entity is null) return new(UpdateScraperConfigOutcome.NotFound, null, null);

        // Shared configs require If-Match (defensive — prevents blind overwrites of a config others may sync).
        if (entity.Shared && ifMatchVersion is null)
        {
            var current = (await ProjectAsync(new[] { entity }, ct)).First();
            return new(UpdateScraperConfigOutcome.PreconditionRequired, null, current);
        }

        if (ifMatchVersion is not null && entity.Version != ifMatchVersion.Value)
        {
            var current = (await ProjectAsync(new[] { entity }, ct)).First();
            return new(UpdateScraperConfigOutcome.PreconditionFailed, null, current);
        }

        var now = DateTimeOffset.UtcNow;
        entity.Name = dto.Name;
        entity.Domain = dto.Domain;
        entity.ConfigJson = JsonDocument.Parse(dto.ConfigJson.GetRawText());
        if (dto.SchemaVersion > 0) entity.SchemaVersion = dto.SchemaVersion;
        entity.Shared = dto.Shared;
        entity.UpdatedAt = now;
        entity.Version++;
        if (workerId.HasValue)
        {
            entity.OriginClientId = workerId.Value.ToString();
            entity.LastSyncedAt = now;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer beat us — surface as PreconditionFailed.
            return new(UpdateScraperConfigOutcome.PreconditionFailed, null, null);
        }
        await tx.CommitAsync(ct);

        var updated = (await ProjectAsync(new[] { entity }, ct)).First();
        return new(UpdateScraperConfigOutcome.Updated, updated, null);
    }

    public async Task<DeleteScraperConfigResult> DeleteAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Set<ScraperConfigEntity>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return new(DeleteScraperConfigOutcome.NotFound, 0);
        if (entity.UserId != userId) return new(DeleteScraperConfigOutcome.Forbidden, 0);

        // Reference check — any TaskBlock of kind Scrape pointing at this configId via ConfigJsonb?
        // Coarse provider-portable check via raw-text LIKE over the JSON column.
        var configIdString = entity.Id.ToString();
        var referencingCount = await _db.Set<TaskBlock>()
            .Where(b => b.BlockType == BlockType.Scrape)
            .CountAsync(b => EF.Functions.Like(
                b.ConfigJsonb.RootElement.GetRawText(),
                $"%{configIdString}%"), ct);
        if (referencingCount > 0)
            return new(DeleteScraperConfigOutcome.Referenced, referencingCount);

        // D5.d.c: capture subscribers BEFORE delete (FK cascade wipes the subscription rows).
        var subscriberWorkerIds = await _db.Set<ScraperConfigSubscription>()
            .Where(s => s.ScraperConfigId == id)
            .Select(s => s.WorkerId)
            .ToListAsync(ct);

        var subscriberUserIds = subscriberWorkerIds.Count == 0
            ? new List<string>()
            : await _db.Set<WorkerConnection>()
                .Where(w => subscriberWorkerIds.Contains(w.Id))
                .Select(w => w.UserId)
                .Distinct()
                .ToListAsync(ct);

        _db.Set<ScraperConfigEntity>().Remove(entity);
        await _db.SaveChangesAsync(ct);

        // Notify each affected user's group. Best-effort — offline users miss it; documented.
        foreach (var subscriberUserId in subscriberUserIds)
        {
            try { await _notifier.SendConfigDeletedToUserAsync(subscriberUserId, id, ct); }
            catch { /* swallow — notification is best-effort */ }
        }

        return new(DeleteScraperConfigOutcome.Deleted, 0);
    }

    public async Task<List<ScraperConfigSubscriberDto>?> GetSubscribersAsync(string userId, Guid configId, CancellationToken ct = default)
    {
        // D5.c.a — owner-only.
        var owns = await _db.Set<ScraperConfigEntity>()
            .AnyAsync(c => c.Id == configId && c.UserId == userId, ct);
        if (!owns) return null;

        var subs = await _db.Set<ScraperConfigSubscription>()
            .AsNoTracking()
            .Include(s => s.Worker)
            .Where(s => s.ScraperConfigId == configId)
            .ToListAsync(ct);

        return subs.Select(s => new ScraperConfigSubscriberDto
        {
            Id = s.WorkerId,
            Name = s.Worker?.Name ?? "",
            Online = s.Worker?.CurrentConnection != null,
            LastPulledAt = s.LastPulledAt,
        }).ToList();
    }

    public async Task<bool> RecordSubscriptionAsync(string userId, Guid configId, Guid workerId, CancellationToken ct = default)
    {
        // D5.b: target shared, worker is caller's, idempotent.
        var config = await _db.Set<ScraperConfigEntity>()
            .FirstOrDefaultAsync(c => c.Id == configId, ct);
        if (config is null || !config.Shared) return false;

        var worker = await _db.Set<WorkerConnection>()
            .FirstOrDefaultAsync(w => w.Id == workerId, ct);
        if (worker is null || worker.UserId != userId) return false;

        var existing = await _db.Set<ScraperConfigSubscription>()
            .FirstOrDefaultAsync(s => s.ScraperConfigId == configId && s.WorkerId == workerId, ct);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            _db.Set<ScraperConfigSubscription>().Add(new ScraperConfigSubscription
            {
                ScraperConfigId = configId,
                WorkerId = workerId,
                LastPulledAt = now,
            });
        }
        else
        {
            existing.LastPulledAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // Shared projection: maps entities to DTOs and resolves OriginWorkerName via WorkerConnection lookup.
    private async Task<List<ScraperConfigDto>> ProjectAsync(IEnumerable<ScraperConfigEntity> rows, CancellationToken ct)
    {
        var rowList = rows.ToList();
        var clientIds = rowList
            .Select(r => r.OriginClientId)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
        var workerNames = clientIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Set<WorkerConnection>()
                .Where(w => clientIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var result = new List<ScraperConfigDto>(rowList.Count);
        foreach (var r in rowList)
        {
            var dto = _mapper.Map<ScraperConfigDto>(r);
            if (!string.IsNullOrEmpty(r.OriginClientId) && Guid.TryParse(r.OriginClientId, out var wid)
                && workerNames.TryGetValue(wid, out var name))
                dto.OriginWorkerName = name;
            result.Add(dto);
        }
        return result;
    }
}
