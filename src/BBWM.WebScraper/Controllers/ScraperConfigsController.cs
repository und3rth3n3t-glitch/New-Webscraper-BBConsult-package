using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BBWM.WebScraper.Controllers;

[ApiController]
[Route("api/scraper-configs")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + ",Bearer")]
public class ScraperConfigsController : ControllerBase
{
    private readonly IScraperConfigService _configs;

    public ScraperConfigsController(IScraperConfigService configs)
    {
        _configs = configs;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _configs.ListAsync(HttpContext.GetUserId(), ct));

    [HttpGet("shared")]
    public async Task<IActionResult> ListShared(CancellationToken ct)
        => Ok(await _configs.ListSharedAsync(HttpContext.GetUserId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _configs.GetAsync(HttpContext.GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateScraperConfigDto dto, [FromQuery] Guid? workerId, CancellationToken ct)
    {
        var result = await _configs.CreateAsync(HttpContext.GetUserId(), dto, workerId, ct);
        return result.Outcome switch
        {
            CreateScraperConfigOutcome.Created => CreatedAtAction(nameof(Get), new { id = result.Dto!.Id }, result.Dto),
            CreateScraperConfigOutcome.Idempotent => Ok(result.Dto),
            CreateScraperConfigOutcome.Conflict => Conflict(result.Dto),
            _ => StatusCode(500),
        };
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] CreateScraperConfigDto dto,
        [FromQuery] Guid? workerId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken ct)
    {
        int? ifMatchVersion = null;
        if (!string.IsNullOrEmpty(ifMatch))
        {
            var trimmed = ifMatch.Trim('"');
            if (!int.TryParse(trimmed, out var v)) return BadRequest(new { error = "Malformed If-Match header" });
            ifMatchVersion = v;
        }
        var result = await _configs.UpdateAsync(HttpContext.GetUserId(), id, dto, ifMatchVersion, workerId, ct);
        return result.Outcome switch
        {
            UpdateScraperConfigOutcome.Updated => Ok(result.Dto),
            UpdateScraperConfigOutcome.NotFound => NotFound(),
            UpdateScraperConfigOutcome.PreconditionFailed => StatusCode(StatusCodes.Status412PreconditionFailed, new { current = result.Current }),
            UpdateScraperConfigOutcome.PreconditionRequired => StatusCode(StatusCodes.Status428PreconditionRequired,
                new { current = result.Current, error = "If-Match header required for shared configs" }),
            _ => StatusCode(500),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _configs.DeleteAsync(HttpContext.GetUserId(), id, ct);
        return result.Outcome switch
        {
            DeleteScraperConfigOutcome.Deleted => NoContent(),
            DeleteScraperConfigOutcome.NotFound => NotFound(),
            DeleteScraperConfigOutcome.Forbidden => Forbid(),
            DeleteScraperConfigOutcome.Referenced => Conflict(new
            {
                referencingTaskCount = result.ReferencingTaskCount,
                error = "Config is referenced by tasks"
            }),
            _ => StatusCode(500),
        };
    }

    [HttpGet("{id:guid}/subscribers")]
    public async Task<IActionResult> GetSubscribers(Guid id, CancellationToken ct)
    {
        var subs = await _configs.GetSubscribersAsync(HttpContext.GetUserId(), id, ct);
        return subs is null ? Forbid() : Ok(subs);
    }

    [HttpPost("{id:guid}/subscriptions")]
    public async Task<IActionResult> Subscribe(Guid id, [FromBody] SubscribeBody body, CancellationToken ct)
    {
        var ok = await _configs.RecordSubscriptionAsync(HttpContext.GetUserId(), id, body.WorkerId, ct);
        return ok ? Ok() : Forbid();
    }

    public class SubscribeBody { public Guid WorkerId { get; set; } }
}
