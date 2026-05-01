using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BBWM.WebScraper.Controllers;

[ApiController]
[Route("api/tasks")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + ",Bearer")]
public class TasksController : ControllerBase
{
    private readonly ITaskService _tasks;
    private readonly IQueueExpansionService _expander;

    public TasksController(ITaskService tasks, IQueueExpansionService expander)
    {
        _tasks = tasks;
        _expander = expander;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _tasks.ListAsync(HttpContext.GetUserId(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _tasks.GetAsync(HttpContext.GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveTaskDto dto, CancellationToken ct)
    {
        var result = await _tasks.SaveAsync(HttpContext.GetUserId(), null, dto, ct);
        return result.Outcome switch
        {
            SaveTaskOutcome.Created => CreatedAtAction(nameof(Get), new { id = result.Task!.Id }, result.Task),
            SaveTaskOutcome.ValidationFailed => UnprocessableEntity(new { errors = result.Errors }),
            _ => StatusCode(500),
        };
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveTaskDto dto, CancellationToken ct)
    {
        var result = await _tasks.SaveAsync(HttpContext.GetUserId(), id, dto, ct);
        return result.Outcome switch
        {
            SaveTaskOutcome.Updated => Ok(result.Task),
            SaveTaskOutcome.NotFound => NotFound(),
            SaveTaskOutcome.Forbidden => Forbid(),
            SaveTaskOutcome.ValidationFailed => UnprocessableEntity(new { errors = result.Errors }),
            _ => StatusCode(500),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var outcome = await _tasks.DeleteAsync(HttpContext.GetUserId(), id, ct);
        return outcome switch
        {
            DeleteTaskOutcome.Deleted => NoContent(),
            DeleteTaskOutcome.NotFound => NotFound(),
            DeleteTaskOutcome.Forbidden => Forbid(),
            _ => StatusCode(500),
        };
    }

    [HttpGet("{id:guid}/expand")]
    public async Task<IActionResult> Expand(Guid id, CancellationToken ct)
    {
        var preview = await _expander.ExpandAsync(HttpContext.GetUserId(), id, ct);
        return Ok(preview);
    }
}
