using BBWM.WebScraper.Entities;

namespace BBWM.WebScraper.Services.Interfaces;

public interface IRunCsvExporter
{
    bool IsTabular(RunItem run);
    byte[] ExportRun(RunItem run, ScraperConfigEntity? liveConfig, RunBatch? batch);
    byte[] ExportBatch(RunBatch batch, IReadOnlyList<RunItem> items, ScraperConfigEntity? liveConfig);
}
