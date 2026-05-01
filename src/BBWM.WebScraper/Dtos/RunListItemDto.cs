using BBWM.WebScraper.Enums;

namespace BBWM.WebScraper.Dtos;

public class RunListItemDto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string TaskName { get; set; } = "";
    public Guid WorkerId { get; set; }
    public string WorkerName { get; set; } = "";
    public Guid? BatchId { get; set; }
    public RunItemStatus Status { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? IterationLabel { get; set; }
    public int? ProgressPercent { get; set; }
}

public class RunListQueryDto
{
    public Guid? TaskId { get; set; }
    public Guid? WorkerId { get; set; }
    public Guid? BatchId { get; set; }
    public RunItemStatus? Status { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
