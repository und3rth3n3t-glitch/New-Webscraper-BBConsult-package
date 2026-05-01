namespace BBWM.WebScraper.Dtos;

public class ValidationErrorDto
{
    public string Code { get; set; } = "";
    public Guid? BlockId { get; set; }
    public Guid? LoopBlockId { get; set; }
    public Guid? ScraperConfigId { get; set; }
    public string? StepId { get; set; }
    public string? Message { get; set; }
}

public static class ValidationCodes
{
    public const string MissingTaskName = "MissingTaskName";
    public const string DuplicateBlockId = "DuplicateBlockId";
    public const string InvalidParentReference = "InvalidParentReference";
    public const string InvalidBlockConfig = "InvalidBlockConfig";
    public const string MissingLoopName = "MissingLoopName";
    public const string TreeCycle = "TreeCycle";
    public const string ConfigNotOwned = "ConfigNotOwned";
    public const string BindingLiteralMissingValue = "BindingLiteralMissingValue";
    public const string LoopRefMissing = "LoopRefMissing";
    public const string LoopRefNotLoop = "LoopRefNotLoop";
    public const string LoopRefNonAncestor = "LoopRefNonAncestor";
    public const string LoopColumnNotFound = "LoopColumnNotFound";
    // D5.a guards:
    public const string MaxDepthExceeded = "MaxDepthExceeded";
    public const string MaxBlocksExceeded = "MaxBlocksExceeded";
}
