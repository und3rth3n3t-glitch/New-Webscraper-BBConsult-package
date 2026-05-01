using BBWM.WebScraper.Services.Expansion;

namespace BBWM.WebScraper.Services.Interfaces;

public enum ExpansionOutcome
{
    Ok,
    NotFound,
    Forbidden,
    BatchEmpty,
    BatchTooLarge,
    NestedLoopUnsupported,
}

public record ExpansionPreview(
    ExpansionOutcome Outcome,
    int Count,
    List<ExpansionResult> Results,
    List<ExpansionWarning> Warnings,
    string? Error = null);

public interface IQueueExpansionService
{
    public const int BatchCap = 1000;

    Task<ExpansionPreview> ExpandAsync(string userId, Guid taskId, CancellationToken ct = default);
}
