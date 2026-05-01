using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRunDto dto, CancellationToken ct)
    {
        var result = await _runs.CreateAndDispatchAsync(HttpContext.GetUserId(), dto.TaskId, dto.WorkerId, ct);
        return result.Outcome switch
        {
            RunDispatchOutcome.Created => CreatedAtAction(nameof(Get), new { id = result.RunItemId }, new { runItemId = result.RunItemId }),
            RunDispatchOutcome.NotFound => NotFound(new { error = result.Error }),
            RunDispatchOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.Error }),
            RunDispatchOutcome.WorkerOffline => Conflict(new { error = result.Error }),
            RunDispatchOutcome.SendFailed => StatusCode(StatusCodes.Status502BadGateway, new { runItemId = result.RunItemId, error = result.Error }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _runs.GetAsync(HttpContext.GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}
