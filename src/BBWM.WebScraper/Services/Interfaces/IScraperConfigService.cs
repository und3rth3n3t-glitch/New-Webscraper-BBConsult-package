using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum CreateScraperConfigOutcome { Created, Idempotent, Conflict }
public enum UpdateScraperConfigOutcome { Updated, NotFound, PreconditionFailed, PreconditionRequired }
public enum DeleteScraperConfigOutcome { Deleted, NotFound, Forbidden, Referenced }

public record CreateScraperConfigResult(CreateScraperConfigOutcome Outcome, ScraperConfigDto? Dto);
public record UpdateScraperConfigResult(UpdateScraperConfigOutcome Outcome, ScraperConfigDto? Dto, ScraperConfigDto? Current);
public record DeleteScraperConfigResult(DeleteScraperConfigOutcome Outcome, int ReferencingTaskCount);

public interface IScraperConfigService
{
    Task<List<ScraperConfigDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<List<ScraperConfigDto>> ListSharedAsync(string userId, CancellationToken ct = default);
    Task<ScraperConfigDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<CreateScraperConfigResult> CreateAsync(string userId, CreateScraperConfigDto dto, Guid? workerId, CancellationToken ct = default);
    Task<UpdateScraperConfigResult> UpdateAsync(string userId, Guid id, CreateScraperConfigDto dto, int? ifMatchVersion, Guid? workerId, CancellationToken ct = default);
    Task<DeleteScraperConfigResult> DeleteAsync(string userId, Guid id, CancellationToken ct = default);
    Task<List<ScraperConfigSubscriberDto>?> GetSubscribersAsync(string userId, Guid configId, CancellationToken ct = default);
    Task<bool> RecordSubscriptionAsync(string userId, Guid configId, Guid workerId, CancellationToken ct = default);
}
