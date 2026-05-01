namespace BBWM.WebScraper.Entities;

public class WorkerConnection
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CurrentConnection { get; set; }
    public string? ExtensionVersion { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}
