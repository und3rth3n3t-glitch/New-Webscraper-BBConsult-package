using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public interface ITaskService
{
    Task<List<TaskDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<TaskDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<TaskDto?> CreateAsync(string userId, CreateTaskDto dto, CancellationToken ct = default);
}
