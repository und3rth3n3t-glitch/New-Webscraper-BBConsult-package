using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BBWM.WebScraper.Controllers;

[ApiController]
[Route("api/runs")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + ",Bearer")]
public class RunsController : ControllerBase
{
    private readonly IRunService _runs;

    public RunsController(IRunService runs)
    {
        _runs = runs;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] RunListQueryDto query, CancellationToken ct)
        => Ok(await _runs.ListAsync(HttpContext.GetUserId(), query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _runs.GetAsync(HttpContext.GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var outcome = await _runs.CancelAsync(HttpContext.GetUserId(), id, ct);
        return outcome switch
        {
            CancelRunOutcome.Cancelled => NoContent(),
            CancelRunOutcome.NotFound => NotFound(),
            CancelRunOutcome.Forbidden => Forbid(),
            CancelRunOutcome.NotCancellable => Conflict(new { error = "Run is in a terminal state" }),
            _ => StatusCode(500),
        };
    }

    [HttpGet("{id:guid}/csv")]
    public async Task<IActionResult> ExportCsv(Guid id, CancellationToken ct)
    {
        var bytes = await _runs.ExportCsvAsync(HttpContext.GetUserId(), id, ct);
        if (bytes is null) return NotFound();
        return File(bytes, "text/csv", $"run-{id}.csv");
    }
}
