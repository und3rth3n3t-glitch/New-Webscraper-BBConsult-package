using System.Text.Json;

namespace BBWM.WebScraper.Entities;

public class RunBatch
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid WorkerId { get; set; }
    // Frozen task tree + per-scrape resolved scraper_configs at populate time.
    public JsonDocument PopulateSnapshot { get; set; } = JsonDocument.Parse("{}");
    public DateTimeOffset CreatedAt { get; set; }
    public TaskEntity? Task { get; set; }
    public WorkerConnection? Worker { get; set; }
    public ICollection<RunItem> Items { get; set; } = new List<RunItem>();
}
