using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum SaveTaskOutcome { Created, Updated, NotFound, Forbidden, ValidationFailed }
public enum DeleteTaskOutcome { Deleted, NotFound, Forbidden }

public record SaveTaskResult(SaveTaskOutcome Outcome, TaskDto? Task, List<ValidationErrorDto> Errors);

public interface ITaskService
{
    Task<List<TaskDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<TaskDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<SaveTaskResult> SaveAsync(string userId, Guid? taskId, SaveTaskDto dto, CancellationToken ct = default);
    Task<DeleteTaskOutcome> DeleteAsync(string userId, Guid taskId, CancellationToken ct = default);
}
