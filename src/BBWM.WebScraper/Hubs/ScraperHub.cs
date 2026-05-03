using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Authentication;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BBWM.WebScraper.Hubs;

[Authorize(AuthenticationSchemes = WebScraperAuthenticationDefaults.AuthenticationScheme)]
public class ScraperHub : Hub
{
    private readonly IWorkerService _workers;
    private readonly IRunService _runs;
    private readonly ILogger<ScraperHub> _logger;

    public ScraperHub(IWorkerService workers, IRunService runs, ILogger<ScraperHub> logger)
    {
        _workers = workers;
        _runs = runs;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.GetHttpContext()?.GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ScraperHubGroups.UserGroup(userId));
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            await _workers.HandleDisconnectAsync(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up disconnected worker");
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task RegisterWorker(string clientId, string extensionVersion)
    {
        var userId = RequireUserId();
        return _workers.RegisterAsync(userId, clientId, extensionVersion, Context.ConnectionId);
    }

    public Task TaskProgress(TaskProgressDto payload) => _runs.RecordProgressAsync(Context.ConnectionId, payload);
    public Task TaskComplete(TaskCompleteDto payload) => _runs.CompleteAsync(Context.ConnectionId, payload);
    public Task TaskError(TaskErrorDto payload) => _runs.FailAsync(Context.ConnectionId, payload);
    public Task TaskPaused(TaskPausedDto payload) => _runs.MarkPausedAsync(Context.ConnectionId, payload);

    private string RequireUserId()
    {
        var userId = Context.GetHttpContext()?.GetUserId();
        if (string.IsNullOrEmpty(userId)) throw new HubException("Missing user claim");
        return userId;
    }
}
