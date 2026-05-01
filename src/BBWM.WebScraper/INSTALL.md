# BBWM.WebScraper — install into a BBWT3 host

This module is **drop-in**: copy the folder into `<host>/modules/BBWM.WebScraper/`, change one line in the csproj, register, and migrate. ~10 minutes for a host that already runs BBWT3.

## Prerequisites

The host must:
- Have `BBWM.Core` and `BBWM.Core.Web.CookieAuth` (or `BBWM.JWT`) referenced from `BBWT.Server.csproj`.
- Call `services.AddSignalR()` in `Startup.ConfigureServices` (BBWT3's standard setup at `BBWT.Server/Startup.cs:81` does this).
- Call `services.AddAntiforgery(...)` and register `AutoValidateAntiforgeryTokenAttribute` as a global MVC filter (BBWT3 standard at `Startup.cs:115` and `Startup.cs:205`).
- Call `services.AddAutoMapper(bbAssemblies)` to auto-discover module profiles (BBWT3 standard at `Startup.cs:276`).

If your host is BBWT3-derived, all of the above are present out of the box. If you're unsure, grep `BBWT.Server/Startup.cs` for those calls.

## Install steps

### 1. Copy the module folder

From your host's repo root, copy the module from this repo. The canonical source is `src/BBWM.WebScraper/` in the `New-Webscraper-BBConsult-package` repo:

```bash
# Adjust the source path to wherever you've cloned New-Webscraper-BBConsult-package:
cp -r <path-to-this-repo>/src/BBWM.WebScraper modules/BBWM.WebScraper
```

### 2. Fix the BBWM.Core reference

Open `modules/BBWM.WebScraper/BBWM.WebScraper.csproj`. Replace the `<ProjectReference>` line marked DEV with the within-solution sibling:

```xml
<ProjectReference Include="..\BBWM.Core\BBWM.Core.csproj" />
```

### 3. Register in solution and server

```bash
dotnet sln <host>.sln add modules/BBWM.WebScraper/BBWM.WebScraper.csproj
```

Add this line to `BBWT.Server/BBWT.Server.csproj` alongside the other module references:

```xml
<ProjectReference Include="..\..\modules\BBWM.WebScraper\BBWM.WebScraper.csproj" />
```

### 4. Generate migrations (per active provider)

For each DB provider your host supports, run from the repo root:

```bash
# SQL Server
dotnet ef migrations add WebScraperModule_Initial --project project/BBWT.Data.SqlServer --startup-project project/BBWT.Server

# PostgreSQL
dotnet ef migrations add WebScraperModule_Initial --project project/BBWT.Data.PostgreSql --startup-project project/BBWT.Server

# MySQL
dotnet ef migrations add WebScraperModule_Initial --project project/BBWT.Data.MySQL --startup-project project/BBWT.Server
```

Inspect each generated migration file. Confirm `CreateTable` blocks for `ScraperConfigs`, `Tasks`, `WorkerConnections`, `RunItems`, with `UserId` as `nvarchar(450)` (or provider equivalent) and JSON columns as the provider's text-blob type.

### 5. Apply the migrations

```bash
dotnet ef database update --project project/BBWT.Data.SqlServer --startup-project project/BBWT.Server
# (and again per provider configured for this host)
```

### 6. CORS allowlist for the extension

If the browser extension connects from a different origin than your host, add the extension's origin (e.g. `chrome-extension://<id>`) to the host's CORS configuration so SignalR's negotiate request can carry credentials.

In `BBWT.Server/Startup.cs`, find the `AddCors`/`UseCors` block and add the extension origin to the allowed origins list. Ensure `.AllowCredentials()` is set.

### 7. Boot and smoke

```bash
dotnet run --project project/BBWT.Server
```

The host log should show `WebScraperModuleLinkage` invoked by `ModuleLinker`. No exceptions on AutoMapper, DI, or EF model build.

Verify endpoints:
- `GET /api/scraper-configs` (logged in) → `200 []`
- `GET /api/scraper-configs` (logged out) → `401`
- SignalR negotiate at `/api/scraper-hub/negotiate` → `200`

## What you get

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/scraper-configs` | GET / POST / PUT | Manage scraper configs (per-user) |
| `/api/tasks` | GET / POST | Manage scrape tasks (per-user) |
| `/api/runs` | POST / GET | Dispatch and inspect runs |
| `/api/workers` | GET | List the user's connected workers |
| `/api/scraper-hub` | WebSocket | SignalR hub for the extension worker |

## Optional: gate access by permission (v1.1+)

By default any authenticated user has scraper access. To gate behind a permission visible in your role-management UI:

1. Add a permission constant `WebScraper.Use` to your host's `Permissions.cs`.
2. Register a policy in `BBWT.Server/Extensions/AuthorizationExtensions.cs` (see existing `Permissions.ScreeningDecisionsRead` policy as a template).
3. Add `IRouteRolesModule` for the four scraper routes with that permission.
4. Change the controllers' attributes from `[Authorize(AuthenticationSchemes = "Cookies,Bearer")]` to `[Authorize(Policy = "WebScraper.Use", AuthenticationSchemes = "Cookies,Bearer")]`.

## Uninstall

1. Run `dotnet ef migrations remove` per provider (or generate a "drop tables" migration).
2. Remove `<ProjectReference>` from `BBWT.Server.csproj`.
3. `dotnet sln <host>.sln remove modules/BBWM.WebScraper/BBWM.WebScraper.csproj`.
4. Delete `modules/BBWM.WebScraper/`.
