using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BBWM.WebScraper.Controllers;

[ApiController]
[Route("api/run-batches")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + ",Bearer")]
public class RunBatchesController : ControllerBase
{
    private readonly IRunBatchService _batches;

    public RunBatchesController(IRunBatchService batches)
    {
        _batches = batches;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] RunBatchListQueryDto query, CancellationToken ct)
        => Ok(await _batches.ListAsync(HttpContext.GetUserId(), query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _batches.GetAsync(HttpContext.GetUserId(), id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBatchDto body, CancellationToken ct)
    {
        var result = await _batches.CreateAndDispatchAsync(HttpContext.GetUserId(), body.TaskId, body.WorkerId, ct);
        return result.Outcome switch
        {
            RunBatchOutcome.Created => CreatedAtAction(nameof(Get), new { id = result.BatchId },
                new BatchDispatchResultDto
                {
                    BatchId = result.BatchId!.Value,
                    DispatchedCount = result.DispatchedCount,
                    FailedCount = result.FailedCount,
                }),
            RunBatchOutcome.NotFound => NotFound(new { error = result.Error }),
            RunBatchOutcome.Forbidden => Forbid(),
            RunBatchOutcome.WorkerOffline => Conflict(new { error = result.Error }),
            RunBatchOutcome.BatchEmpty => UnprocessableEntity(new { error = result.Error }),
            RunBatchOutcome.BatchTooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = result.Error }),
            RunBatchOutcome.NestedLoopUnsupported => UnprocessableEntity(new { error = result.Error }),
            _ => StatusCode(500),
        };
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "csv", CancellationToken ct = default)
    {
        var result = await _batches.ExportAsync(HttpContext.GetUserId(), id, format, ct);
        return result.Outcome switch
        {
            RunBatchExportOutcome.Ok => File(result.Bytes!, result.ContentType!, result.Filename),
            RunBatchExportOutcome.NotFound => NotFound(),
            RunBatchExportOutcome.Forbidden => Forbid(),
            RunBatchExportOutcome.BadFormat => BadRequest(new { error = "format must be 'csv' or 'json'" }),
            _ => StatusCode(500),
        };
    }
}
