using BBWM.Core.Data;

namespace BBWM.WebScraper.Entities;

public class TaskEntity : IAuditableEntity<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid ScraperConfigId { get; set; }
    public string[] SearchTerms { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; }
    public ScraperConfigEntity? ScraperConfig { get; set; }
}
