using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class WorkerService : IWorkerService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;

    public WorkerService(IDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    // Dedup key: (UserId, Name). Two simultaneous registrations from the same user
    // with the same name share a row (enforced by unique index in WorkerConnectionConfiguration).
    public async Task<WorkerConnection> RegisterAsync(string userId, string clientName, string extensionVersion, string connectionId, CancellationToken ct = default)
    {
        var resolvedName = string.IsNullOrWhiteSpace(clientName) ? "My Browser" : clientName;
        var worker = await _db.Set<WorkerConnection>()
            .FirstOrDefaultAsync(w => w.UserId == userId && w.Name == resolvedName, ct);
        var now = DateTimeOffset.UtcNow;

        if (worker is null)
        {
            worker = new WorkerConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = resolvedName,
            };
            _db.Set<WorkerConnection>().Add(worker);
        }

        worker.CurrentConnection = connectionId;
        worker.ExtensionVersion = extensionVersion;
        worker.LastConnectedAt = now;
        worker.LastSeenAt = now;

        await _db.SaveChangesAsync(ct);
        return worker;
    }

    public async Task HandleDisconnectAsync(string connectionId, CancellationToken ct = default)
    {
        var worker = await _db.Set<WorkerConnection>()
            .FirstOrDefaultAsync(w => w.CurrentConnection == connectionId, ct);
        if (worker is null) return;

        var now = DateTimeOffset.UtcNow;
        worker.CurrentConnection = null;
        worker.LastSeenAt = now;

        var inFlightStatuses = new[] { RunItemStatus.Sent, RunItemStatus.Running, RunItemStatus.Paused };
        var inFlight = await _db.Set<RunItem>()
            .Where(r => r.WorkerId == worker.Id && inFlightStatuses.Contains(r.Status))
            .ToListAsync(ct);

        foreach (var run in inFlight)
        {
            run.Status = RunItemStatus.Failed;
            run.ErrorMessage = "Worker disconnected";
            run.CompletedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<WorkerDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var workers = await _db.Set<WorkerConnection>()
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
        return _mapper.Map<List<WorkerDto>>(workers);
    }
}
