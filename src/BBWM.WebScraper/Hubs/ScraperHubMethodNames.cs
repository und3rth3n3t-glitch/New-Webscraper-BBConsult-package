namespace BBWM.WebScraper.Hubs;

/// <summary>
/// String constants for SignalR hub method names. These must mirror the TypeScript
/// constants in the extension's `src/offscreen/hubMethodNames.ts` (planned in a
/// later refactor). A rename here without a matching change there silently breaks
/// the wire protocol.
/// </summary>
public static class ScraperHubMethodNames
{
    // Server methods invoked by the extension. The C# method names on `ScraperHub`
    // already match these by `nameof`; constants kept here for cross-referencing
    // from anywhere that needs to refer to them by string.
    public const string RegisterWorker     = "RegisterWorker";
    public const string TaskProgress       = "TaskProgress";
    public const string TaskComplete       = "TaskComplete";
    public const string TaskError          = "TaskError";
    public const string TaskPaused         = "TaskPaused";

    // Client events broadcast from the server to extension(s).
    public const string ReceiveTask            = "ReceiveTask";
    public const string CancelTask             = "CancelTask";
    public const string ResumeAfterPause       = "ResumeAfterPause";
    public const string BatchProgress          = "BatchProgress";
    public const string ScraperConfigUpdated   = "ScraperConfigUpdated";
    public const string ScraperConfigDeleted   = "ScraperConfigDeleted";
}

/// <summary>
/// Helpers for SignalR group names used across <see cref="ScraperHub"/> and
/// <see cref="BBWM.WebScraper.Services.Hubs.ScraperHubWorkerNotifier"/>.
/// Centralising the format string ensures both sites always produce the same key.
/// </summary>
public static class ScraperHubGroups
{
    /// <summary>Returns the SignalR group name for a given user (e.g. <c>"user:abc123"</c>).</summary>
    public static string UserGroup(string userId) => $"user:{userId}";
}
