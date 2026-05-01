using System.Text.Json;

namespace BBWM.WebScraper.Entities;

public class ScraperConfigEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public JsonDocument ConfigJson { get; set; } = JsonDocument.Parse("{}");
    public int SchemaVersion { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    // v2.0 sync/sharing fields:
    public bool Shared { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? OriginClientId { get; set; }
    // D1.c concurrency token — IsConcurrencyToken() in entity config; incremented on every successful Update.
    public int Version { get; set; } = 1;
    public ICollection<ScraperConfigSubscription> Subscriptions { get; set; } = new List<ScraperConfigSubscription>();
}
