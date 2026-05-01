# BBWM.WebScraper

A drop-in BBWT3 module that adds a per-user web-scraping backend (configs, tasks, runs, workers) to any BBWT3 host. The matching browser extension connects to the host's SignalR hub and executes scraping flows on the user's behalf.

## Layout

```
src/BBWM.WebScraper/         The module (entities, services, controllers, hub, AutoMapper profile)
src/BBWM.WebScraper/INSTALL.md   How to install into a BBWT3 host
tests/BBWM.WebScraper.Tests/ xUnit tests against EF InMemory
specs/SPEC-webscraper-module-v1.1.md   Design rationale + per-file documentation
BBWM.WebScraper.sln          Solution
```

## Install into a BBWT3 host

See [`src/BBWM.WebScraper/INSTALL.md`](src/BBWM.WebScraper/INSTALL.md). Summary: copy the module folder into `<host>/modules/`, change one line in the csproj, register in the solution, generate provider migrations, boot.

## Build & test (this repo)

Requires `pharmacy-planet` checked out at `c:\Users\und3r\pharmacy-planet` (sibling path) — the module's csproj has a relative `<ProjectReference>` to `pharmacy-planet/modules/BBWM.Core/` for development.

```bash
dotnet build BBWM.WebScraper.sln
dotnet test tests/BBWM.WebScraper.Tests/
```

## Origin

Extracted from the WebScrape testbed in [`blueberry-v3`](https://github.com/und3rth3n3t-glitch/Webscraper-v3.1). The testbed remains intact in that repo as a standalone smoke environment with its own tests; this repo is the drop-in module.
