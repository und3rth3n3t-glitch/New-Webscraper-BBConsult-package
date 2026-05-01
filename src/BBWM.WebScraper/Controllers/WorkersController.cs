using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BBWM.WebScraper.Controllers;

[ApiController]
[Route("api/workers")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + ",Bearer")]
public class WorkersController : ControllerBase
{
    private readonly IWorkerService _workers;

    public WorkersController(IWorkerService workers)
    {
        _workers = workers;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _workers.ListAsync(HttpContext.GetUserId(), ct));
}
