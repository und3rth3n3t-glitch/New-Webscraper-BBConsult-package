using BBWM.Core.Data;

namespace BBWM.WebScraper.Entities;

public class TaskEntity : IAuditableEntity<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<TaskBlock> Blocks { get; set; } = new List<TaskBlock>();
}
