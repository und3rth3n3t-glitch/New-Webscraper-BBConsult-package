using System.Reflection;
using BBWM.Core;
using BBWM.Core.Membership;
using BBWM.Core.Membership.DTO;
using BBWM.Core.ModuleLinker;
using BBWM.Core.Web;
using BBWM.Menu;
using BBWM.Menu.DTO;
using BBWM.WebScraper.Authentication;
using BBWM.WebScraper.Hubs;
using Microsoft.AspNetCore.SignalR;
using BBWM.WebScraper.Services.Expansion;
using BBWM.WebScraper.Services.Hubs;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Route = BBWM.Core.Web.Route;

namespace BBWM.WebScraper;

public class WebScraperModuleLinkage :
    IDbModelCreateModuleLinkage,
    IServicesModuleLinkage,
    ISignalRModuleLinkage,
    IRouteRolesModuleLinkage,
    IMenuModuleLinkage
{
    // Default false (production-safe). Hosts can opt in via appsettings: { "WebScraper": { "HubDetailedErrors": true } }
    private const int HubMaxMessageSizeBytes = 10 * 1024 * 1024;

    public void OnModelCreating(ModelBuilder builder)
        => builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Per-hub SignalR options. The host's services.AddSignalR() defaults
        // MaximumReceiveMessageSize to 32KB, which is far too small for the
        // TaskComplete payload sent by the extension at end of run (multi-MB:
        // table rows × iterations × per-iteration scrape output). Without this,
        // SignalR rejects the message at transport, kills the WebSocket, the
        // backend's OnDisconnectedAsync fires before TaskComplete can be processed,
        // and the result is lost.
        // Configure GLOBAL hub options. The typed Configure<HubOptions<ScraperHub>>
        // pattern only works when HubOptionsSetup<TScraperHub> has been registered,
        // which requires the host to call .AddHubOptions<TScraperHub>() on the SignalR
        // builder. As a drop-in module we can't do that from outside ConfigureServices,
        // so we configure the global HubOptions which feeds all hubs (10 MB is a sane
        // upper bound for any reasonable hub payload). Verified necessary: without this
        // the host's default 32KB limit kills the WebSocket on the multi-MB TaskComplete
        // payload, the run gets marked Failed, and the result is lost.
        services.Configure<HubOptions>(options =>
        {
            options.MaximumReceiveMessageSize = HubMaxMessageSizeBytes; // 10 MB — matches the original WebScrape.Server backend.
            // Default false (production-safe). Hosts can opt in via appsettings: { "WebScraper": { "HubDetailedErrors": true } }
            options.EnableDetailedErrors = configuration.GetValue<bool>("WebScraper:HubDetailedErrors", false);
        });

        // Auth: register a policy scheme that adapts to whichever host auth schemes are present.
        // BBWT3 hosts that use AddIdentity register "Identity.Application" (cookie); BBWM.JWT
        // registers "Bearer" only when Jwt:Key is configured. Hard-coding either name in
        // [Authorize(...)] attributes would tie the module to a particular host shape and
        // throw at request time if the named scheme isn't registered. Forwarding through a
        // policy scheme picks at runtime per request.
        services.AddAuthentication()
            .AddPolicyScheme(WebScraperAuthenticationDefaults.AuthenticationScheme, "WebScraper auth", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
                    var schemes = schemeProvider.GetAllSchemesAsync().GetAwaiter().GetResult();
                    var schemeNames = schemes.Select(s => s.Name).ToHashSet();

                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (authHeader is not null
                        && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        && schemeNames.Contains(WebScraperAuthenticationDefaults.BearerScheme))
                    {
                        return WebScraperAuthenticationDefaults.BearerScheme;
                    }

                    if (schemeNames.Contains(WebScraperAuthenticationDefaults.IdentityApplicationScheme)) return WebScraperAuthenticationDefaults.IdentityApplicationScheme;
                    if (schemeNames.Contains(WebScraperAuthenticationDefaults.CookiesScheme)) return WebScraperAuthenticationDefaults.CookiesScheme;
                    return WebScraperAuthenticationDefaults.IdentityApplicationScheme;  // host config error; let the framework throw
                };
            });

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

    // Routes are the UI paths the Angular SPA navigates to; these match the routerLink values
    // in main-menu.json and are what BBWM.Menu.MenuService.FilterOnlyAllowedMenuItems checks
    // against (NOT the api/* controller routes). Open-by-auth via AggregatedRoles.Authenticated
    // matches the testbed's "any logged-in user can use the scraper" stance — hosts that want
    // role-gated access swap to a permission per the recipe in INSTALL.md.
    public List<PageInfoDTO> GetRouteRoles(IServiceScope serviceScope) =>
    [
        new(Routes.Configs, AggregatedRoles.Authenticated),
        new(Routes.ConfigCreate, AggregatedRoles.Authenticated),
        new(Routes.ConfigEdit, AggregatedRoles.Authenticated),
        new(Routes.Tasks, AggregatedRoles.Authenticated),
        new(Routes.TaskCreate, AggregatedRoles.Authenticated),
        new(Routes.TaskEdit, AggregatedRoles.Authenticated),
        new(Routes.Batches, AggregatedRoles.Authenticated),
        new(Routes.BatchDetail, AggregatedRoles.Authenticated),
        new(Routes.Runs, AggregatedRoles.Authenticated),
        new(Routes.RunDetail, AggregatedRoles.Authenticated),
        new(Routes.Workers, AggregatedRoles.Authenticated),
    ];

    /// <summary>
    /// Registers the Web Scraper menu group on a fresh BBWT3 install (only fires when the
    /// host's menu data store is empty — see <c>BBWT.InitialData.MenuInitializerService</c>).
    /// Hosts whose menu was seeded before this module was installed must edit
    /// <c>data/menu/main-menu.json</c> manually to surface the entry; see INSTALL.md.
    /// </summary>
    public void CreateInitialMenuItems(List<MenuDTO> menu, MenuLinkageRootMenus rootMenus)
    {
        rootMenus.OperationalAdmin.Children.Add(new MenuDTO
        {
            Label = "Web Scraper",
            Icon = "language",
            RouterLink = Routes.Configs.Path,
            Children =
            [
                new MenuDTO { Label = "Configs",     RouterLink = Routes.Configs.Path },
                new MenuDTO { Label = "Tasks",       RouterLink = Routes.Tasks.Path   },
                new MenuDTO { Label = "Run batches", RouterLink = Routes.Batches.Path },
                new MenuDTO { Label = "Workers",     RouterLink = Routes.Workers.Path },
            ]
        });
    }
}
