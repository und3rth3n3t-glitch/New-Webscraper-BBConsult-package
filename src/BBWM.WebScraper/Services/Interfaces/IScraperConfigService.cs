using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public interface IScraperConfigService
{
    Task<List<ScraperConfigDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<ScraperConfigDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<ScraperConfigDto> CreateAsync(string userId, CreateScraperConfigDto dto, CancellationToken ct = default);
    Task<ScraperConfigDto?> UpdateAsync(string userId, Guid id, CreateScraperConfigDto dto, CancellationToken ct = default);
}
