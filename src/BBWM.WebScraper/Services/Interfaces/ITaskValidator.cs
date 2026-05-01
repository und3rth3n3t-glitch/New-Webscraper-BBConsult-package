using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public interface ITaskValidator
{
    Task<List<ValidationErrorDto>> ValidateAsync(string userId, SaveTaskDto dto, CancellationToken ct = default);
}
