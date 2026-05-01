namespace BBWM.WebScraper.Dtos;

// D4.b — server→client hub event aggregating batch state.
// Emitted to the batch owner's group `user:{userId}` after each per-run progress event.
public class BatchProgressDto
{
    public Guid BatchId { get; set; }
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }       // includes Cancelled
    public int Running { get; set; }      // Sent + Running + Paused
    public int Pending { get; set; }
    public int OverallPercent { get; set; }   // (Completed + Failed) * 100 / Total
}
