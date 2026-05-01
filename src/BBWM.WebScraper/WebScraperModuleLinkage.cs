using System.Reflection;
using BBWM.Core.ModuleLinker;
using BBWM.WebScraper.Hubs;
using BBWM.WebScraper.Services.Expansion;
using BBWM.WebScraper.Services.Hubs;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BBWM.WebScraper;

public class WebScraperModuleLinkage :
    IDbModelCreateModuleLinkage,
    IServicesModuleLinkage,
    ISignalRModuleLinkage
{
    public void OnModelCreating(ModelBuilder builder)
        => builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Domain services
        services.AddScoped<IScraperConfigService, ScraperConfigService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IRunService, RunService>();
        services.AddScoped<IRunBatchService, RunBatchService>();
        services.AddScoped<IWorkerService, WorkerService>();

        // Infrastructure
        services.AddScoped<IWorkerNotifier, ScraperHubWorkerNotifier>();
        services.AddScoped<ITaskValidator, TaskValidator>();
        services.AddScoped<IQueueExpansionService, QueueExpansionService>();
        services.AddScoped<IRunCsvExporter, RunCsvExporter>();

        // Block expanders — registered as a collection; QueueExpansionService consumes IEnumerable<IBlockExpander>.
        services.AddScoped<IBlockExpander, LoopBlockExpander>();
        services.AddScoped<IBlockExpander, ScrapeBlockExpander>();

        // AutoMapper profile auto-discovered by host's AddAutoMapper(bbAssemblies) — do NOT register here.
    }

    public void MapHubs(IEndpointRouteBuilder routes)
        => routes.MapHub<ScraperHub>("/api/scraper-hub");
}
