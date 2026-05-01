using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;

namespace BBWM.WebScraper.Services.Interfaces;

public interface IWorkerService
{
    Task<WorkerConnection> RegisterAsync(string userId, string clientName, string extensionVersion, string connectionId, CancellationToken ct = default);
    Task HandleDisconnectAsync(string connectionId, CancellationToken ct = default);
    Task<List<WorkerDto>> ListAsync(string userId, CancellationToken ct = default);
}
