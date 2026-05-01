# SPEC-webscraper-module-v2.0

**Feature:** `BBWM.WebScraper` v2.0 — feature parity with the WebScrape testbed in `blueberry-v3`. Drop-in BBWT3 module with full block-tree task model, run batches with queue expansion, validation, sharing, sync semantics, and CSV export.
**This repo:** `New-Webscraper-BBConsult-package` (GitHub `und3rth3n3t-glitch/New-Webscraper-BBConsult-package`).
**Source repo (port origin):** `c:\Users\und3r\blueberry-v3\backend\src\WebScrape.{Data,Services,Server}\`. **Read directly from this path** — the older `c:\Users\und3r\webscrape\` folder is stale and must not be used.
**Validation rig:** `c:\Users\und3r\pharmacy-planet` (smoke environment only — never committed to).
**Implementing agent:** Sonnet.
**Replaces:** SPEC-webscraper-module-v1.1.md. v2.0 is a major version bump because every entity, DTO, service, and controller changes shape.

---

## Decisions locked from staged review

| Decision | Choice |
|---|---|
| **D1.c** | Etag is `Version: int` column with EF `IsConcurrencyToken()` + explicit `BeginTransactionAsync` wrapping load-check-mutate-save |
| **D2.a** | Single clean v2.0 Initial migration in each provider project (no porting of v3.1's 6-step history) |
| **D3.b** | `TaskEntity : IAuditableEntity<Guid>` (only) — no audit on configs, run items, batches, workers, or subscriptions |
| **D4.b** | New server→client hub message `BatchProgress` — server aggregates per-run progress and emits batch-level state to the batch owner's group |
| **D5.a** | Validator guards: max depth 16, max block count 256 — codes `MaxDepthExceeded`, `MaxBlocksExceeded` |
| **D5.b** | Subscribe rules: target config must exist + be `Shared=true` + caller-owned worker; idempotent on duplicate (refresh `LastPulledAt`) |
| **D5.c.a** | `GetSubscribers`: config-owner only |
| **D5.d.c** | Delete shared config: capture subscribers → cascade-delete → emit `ScraperConfigDeleted(configId)` to each subscriber's user group |
| **D5.e** | Concurrency: in-process `Version` check inside `BeginTransactionAsync`, EF concurrency token catches races at the SQL level (`DbUpdateConcurrencyException` → `PreconditionFailed` outcome) |

Carry from v1.1: `string UserId` length 450, `[Authorize(AuthenticationSchemes = "Cookies,Bearer")]`, all 3 DB providers, copy-into-host distribution, host-supplied AddSignalR/AddAntiforgery/AddAutoMapper conventions.

---

## Note on historical path references

This spec ports code from the blueberry-v3 testbed at `backend/src/WebScrape.{Data,Services,Server}/`. **References to `WebScrape.*` files throughout this spec are SOURCE paths** in that repo, not paths in this repo. Sonnet reads them and ports into `src/BBWM.WebScraper/` here. The `c:/Users/und3r/webscrape/` folder is stale; do not read it.

---

## Dev environment prerequisites

1. **`pharmacy-planet` checked out at `c:\Users\und3r\pharmacy-planet`** — the module's csproj has a relative ref `..\..\..\pharmacy-planet\modules\BBWM.Core\BBWM.Core.csproj`.
2. **`blueberry-v3` checked out at `c:\Users\und3r\blueberry-v3`** — Sonnet reads source files from `blueberry-v3/backend/src/WebScrape.*/` during the port.
3. **Local SQL Server** for smoke tests (Postgres/MySQL migration generation works without a live server; runtime smoke is SqlServer only).

---

## Project structure

```
src/BBWM.WebScraper/
  BBWM.WebScraper.csproj
  WebScraperModuleLinkage.cs
  INSTALL.md
  Entities/
    ScraperConfigEntity.cs
    ScraperConfigSubscription.cs
    TaskEntity.cs
    TaskBlock.cs
    RunItem.cs
    RunBatch.cs
    WorkerConnection.cs
  Enums/
    BlockType.cs
    BindingKind.cs
  EntityConfiguration/
    ScraperConfigEntityConfiguration.cs
    ScraperConfigSubscriptionConfiguration.cs
    TaskEntityConfiguration.cs
    TaskBlockConfiguration.cs
    RunItemConfiguration.cs
    RunBatchConfiguration.cs
    WorkerConnectionConfiguration.cs
  Dtos/
    ScraperConfigDto.cs                  (incl. CreateScraperConfigDto + ScraperConfigSubscriberDto)
    TaskDto.cs                           (incl. SaveTaskDto + DeleteTaskOutcome details)
    TaskBlockDto.cs                      (TaskBlockTreeDto + LoopBlockConfigDto + ScrapeBlockConfigDto + StepBindingDto)
    ValidationErrorDto.cs                (incl. ValidationCodes static class)
    RunItemDto.cs
    WorkerDto.cs
    HubPayloadDtos.cs                    (TaskProgress/Complete/Error/Paused — unchanged from v1.1)
    BatchProgressDto.cs                  (new for D4.b)
    QueueTaskDto.cs                      (extended with IterationLabel + IterationAssignments)
    RunBatchDto.cs                       (RunBatchListItemDto + RunBatchDetailDto + RunBatchListQueryDto + RunBatchExportResult)
    ExpansionDto.cs                      (ExpansionPreview + ExpansionResult + ExpansionOutcome)
    PagedResultDto.cs                    (generic <T>)
  Mapping/
    WebScraperAutoMapperProfile.cs
  Services/
    Expansion/
      IBlockExpander.cs
      ExpansionContext.cs
      ExpansionFrame.cs
      LoopBlockExpander.cs
      ScrapeBlockExpander.cs
    Interfaces/
      IScraperConfigService.cs           (incl. outcome enums + result records)
      ITaskService.cs                    (incl. SaveTaskOutcome + DeleteTaskOutcome + records)
      IRunService.cs                     (extended with List/Cancel/Export)
      IRunBatchService.cs                (incl. RunBatchOutcome + RunBatchDispatchResult + RunBatchExportOutcome)
      IWorkerService.cs
      IWorkerNotifier.cs                 (extended with SendBatchProgressAsync + SendConfigDeletedToUserAsync)
      ITaskValidator.cs
      IQueueExpansionService.cs
      IRunCsvExporter.cs
    Implementations/
      ScraperConfigService.cs
      TaskService.cs
      RunService.cs
      RunBatchService.cs
      WorkerService.cs
      TaskValidator.cs
      QueueExpansionService.cs
      RunCsvExporter.cs
    Hubs/
      ScraperHubWorkerNotifier.cs
  Hubs/
    ScraperHub.cs
  Controllers/
    ScraperConfigsController.cs
    TasksController.cs
    RunsController.cs
    RunBatchesController.cs
    WorkersController.cs

tests/BBWM.WebScraper.Tests/
  EntityConfiguration/ConfigurationsApplyTests.cs
  Mapping/AutoMapperProfileTests.cs
  Services/
    ScraperConfigServiceTests.cs       (extend)
    TaskServiceTests.cs                (rewrite)
    RunServiceTests.cs                 (extend)
    RunBatchServiceTests.cs            (new)
    WorkerServiceTests.cs              (carry from v1.1)
    TaskValidatorTests.cs              (new)
    QueueExpansionTests.cs             (new)
    RunCsvExporterTests.cs             (new)
  Hubs/BatchProgressHubTests.cs        (new)
  TestSupport/
    TestDb.cs                          (extend)
    TestWebScraperDbContext.cs         (carry)

specs/SPEC-webscraper-module-v2.0.md   (this file)
BBWM.WebScraper.sln
README.md  .gitignore
```

After creating files, register in solution:
```bash
dotnet sln BBWM.WebScraper.sln add src/BBWM.WebScraper/BBWM.WebScraper.csproj
dotnet sln BBWM.WebScraper.sln add tests/BBWM.WebScraper.Tests/BBWM.WebScraper.Tests.csproj
```
(Both already in the sln from v1.1; new files just sit inside the existing project trees.)

---

## 1. csproj — unchanged from v1.1

`src/BBWM.WebScraper/BBWM.WebScraper.csproj` carries from v1.1 unchanged. Same `<FrameworkReference Include="Microsoft.AspNetCore.App" />` + cross-repo `<ProjectReference>` to BBWM.Core (3 dots). No new package deps.

---

## 2. Enums

### 2.1 `Enums/BlockType.cs`

```csharp
namespace BBWM.WebScraper.Enums;

public enum BlockType
{
    Loop = 0,
    Scrape = 1,
}
```

### 2.2 `Enums/BindingKind.cs`

```csharp
namespace BBWM.WebScraper.Enums;

public enum BindingKind
{
    Literal = 0,
    LoopRef = 1,
    Unbound = 2,
}
```

---

## 3. Entities

### 3.1 `Entities/ScraperConfigEntity.cs` *(modify v1.1)*

```csharp
using System.Text.Json;

namespace BBWM.WebScraper.Entities;

public class ScraperConfigEntity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public JsonDocument ConfigJson { get; set; } = JsonDocument.Parse("{}");
    public int SchemaVersion { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    // v2.0 additions:
    public bool Shared { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? OriginClientId { get; set; }
    public int Version { get; set; } = 1;  // D1.c concurrency token; EF IsConcurrencyToken()
}
```

> **Not** `IAuditableEntity` — D3.b drops audit on configs.

### 3.2 `Entities/ScraperConfigSubscription.cs` *(new)*

```csharp
namespace BBWM.WebScraper.Entities;

public class ScraperConfigSubscription
{
    public Guid ScraperConfigId { get; set; }
    public Guid WorkerId { get; set; }
    public DateTimeOffset LastPulledAt { get; set; }
    public ScraperConfigEntity? ScraperConfig { get; set; }
    public WorkerConnection? Worker { get; set; }
}
```

### 3.3 `Entities/TaskEntity.cs` *(rewrite v1.1)*

```csharp
using BBWM.Core.Data;

namespace BBWM.WebScraper.Entities;

public class TaskEntity : IAuditableEntity<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<TaskBlock> Blocks { get; set; } = new List<TaskBlock>();
}
```

> Removed: `ScraperConfigId`, `ScraperConfig` nav, `SearchTerms[]`. All replaced by the block tree.

### 3.4 `Entities/TaskBlock.cs` *(new)*

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Data/Entities/TaskBlock.cs`. Change namespace `WebScrape.Data.Entities` → `BBWM.WebScraper.Entities`. Change `using WebScrape.Data.Enums;` → `using BBWM.WebScraper.Enums;`. Body otherwise unchanged.

### 3.5 `Entities/RunItem.cs` *(modify v1.1)*

```csharp
using System.Text.Json;

namespace BBWM.WebScraper.Entities;

public static class RunItemStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public class RunItem
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid WorkerId { get; set; }
    public Guid? BatchId { get; set; }              // v2.0: nullable FK to RunBatch
    public Guid ScraperConfigId { get; set; }       // v2.0: denormalized per migration M3
    public string Status { get; set; } = RunItemStatus.Pending;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public JsonDocument? ResultJsonb { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PauseReason { get; set; }
    public int? ProgressPercent { get; set; }
    public string? CurrentTerm { get; set; }
    public string? CurrentStep { get; set; }
    public string? Phase { get; set; }
    public string IterationLabel { get; set; } = string.Empty;     // v2.0
    public JsonDocument? IterationAssignments { get; set; }        // v2.0: dict<loopId, value>
    public TaskEntity? Task { get; set; }
    public WorkerConnection? Worker { get; set; }
    public RunBatch? Batch { get; set; }
}
```

> Status constants change from lowercase (v1.1) to PascalCase (matches v3.1 migration M2RunItemStatusEnum).

### 3.6 `Entities/RunBatch.cs` *(new)*

```csharp
using System.Text.Json;

namespace BBWM.WebScraper.Entities;

public class RunBatch
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid WorkerId { get; set; }
    public JsonDocument PopulateSnapshot { get; set; } = JsonDocument.Parse("{}");
    public DateTimeOffset CreatedAt { get; set; }
    public TaskEntity? Task { get; set; }
    public WorkerConnection? Worker { get; set; }
    public ICollection<RunItem> Items { get; set; } = new List<RunItem>();
}
```

### 3.7 `Entities/WorkerConnection.cs` *(unchanged from v1.1)*

Carry from v1.1 SPEC §5.3 unchanged.

---

## 4. Entity configurations

Convention: no `HasColumnType` on JSON columns (let EF infer per provider); `HasMaxLength(450)` on `UserId`; value converters for JSON columns.

### 4.1 `EntityConfiguration/ScraperConfigEntityConfiguration.cs`

```csharp
using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class ScraperConfigEntityConfiguration : IEntityTypeConfiguration<ScraperConfigEntity>
{
    private static readonly ValueConverter<JsonDocument, string> JsonConverter = new(
        v => v.RootElement.GetRawText(),
        v => JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<ScraperConfigEntity> e)
    {
        e.ToTable("ScraperConfigs");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        e.Property(x => x.Domain).IsRequired().HasMaxLength(256);
        e.Property(x => x.ConfigJson).HasConversion(JsonConverter).IsRequired();
        e.Property(x => x.SchemaVersion).HasDefaultValue(3);
        e.Property(x => x.OriginClientId).HasMaxLength(450);
        e.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1);
        e.HasIndex(x => x.UserId);
        e.HasIndex(x => new { x.UserId, x.Shared });  // sharing list query
    }
}
```

### 4.2 `EntityConfiguration/ScraperConfigSubscriptionConfiguration.cs`

```csharp
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BBWM.WebScraper.EntityConfiguration;

public class ScraperConfigSubscriptionConfiguration : IEntityTypeConfiguration<ScraperConfigSubscription>
{
    public void Configure(EntityTypeBuilder<ScraperConfigSubscription> e)
    {
        e.ToTable("ScraperConfigSubscriptions");
        e.HasKey(x => new { x.ScraperConfigId, x.WorkerId });
        e.HasOne(x => x.ScraperConfig).WithMany().HasForeignKey(x => x.ScraperConfigId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Worker).WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

### 4.3 `EntityConfiguration/TaskEntityConfiguration.cs`

```csharp
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BBWM.WebScraper.EntityConfiguration;

public class TaskEntityConfiguration : IEntityTypeConfiguration<TaskEntity>
{
    public void Configure(EntityTypeBuilder<TaskEntity> e)
    {
        e.ToTable("Tasks");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.Name).IsRequired().HasMaxLength(256);
        e.HasMany(x => x.Blocks).WithOne(b => b.Task!).HasForeignKey(b => b.TaskId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.UserId);
    }
}
```

### 4.4 `EntityConfiguration/TaskBlockConfiguration.cs`

```csharp
using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class TaskBlockConfiguration : IEntityTypeConfiguration<TaskBlock>
{
    private static readonly ValueConverter<JsonDocument, string> JsonConverter = new(
        v => v.RootElement.GetRawText(),
        v => JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<TaskBlock> e)
    {
        e.ToTable("TaskBlocks");
        e.HasKey(x => x.Id);
        e.Property(x => x.BlockType).HasConversion<int>();
        e.Property(x => x.ConfigJsonb).HasConversion(JsonConverter).IsRequired();
        e.HasOne(x => x.ParentBlock).WithMany(x => x.Children).HasForeignKey(x => x.ParentBlockId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.TaskId, x.ParentBlockId, x.OrderIndex });
    }
}
```

### 4.5 `EntityConfiguration/RunItemConfiguration.cs`

```csharp
using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class RunItemConfiguration : IEntityTypeConfiguration<RunItem>
{
    private static readonly ValueConverter<JsonDocument?, string?> NullableJsonConverter = new(
        v => v == null ? null : v.RootElement.GetRawText(),
        v => v == null ? null : JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<RunItem> e)
    {
        e.ToTable("RunItems");
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).IsRequired().HasMaxLength(32);
        e.Property(x => x.PauseReason).HasMaxLength(64);
        e.Property(x => x.CurrentTerm).HasMaxLength(512);
        e.Property(x => x.CurrentStep).HasMaxLength(256);
        e.Property(x => x.Phase).HasMaxLength(32);
        e.Property(x => x.IterationLabel).IsRequired().HasMaxLength(512);
        e.Property(x => x.ResultJsonb).HasConversion(NullableJsonConverter);
        e.Property(x => x.IterationAssignments).HasConversion(NullableJsonConverter);
        e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Worker).WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Batch).WithMany(b => b.Items).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.TaskId, x.RequestedAt });
        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.ScraperConfigId);
        e.HasIndex(x => x.BatchId);
    }
}
```

### 4.6 `EntityConfiguration/RunBatchConfiguration.cs`

```csharp
using System.Text.Json;
using BBWM.WebScraper.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BBWM.WebScraper.EntityConfiguration;

public class RunBatchConfiguration : IEntityTypeConfiguration<RunBatch>
{
    private static readonly ValueConverter<JsonDocument, string> JsonConverter = new(
        v => v.RootElement.GetRawText(),
        v => JsonDocument.Parse(v, default(JsonDocumentOptions)));

    public void Configure(EntityTypeBuilder<RunBatch> e)
    {
        e.ToTable("RunBatches");
        e.HasKey(x => x.Id);
        e.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        e.Property(x => x.PopulateSnapshot).HasConversion(JsonConverter).IsRequired();
        e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Worker).WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}
```

### 4.7 `EntityConfiguration/WorkerConnectionConfiguration.cs` *(unchanged from v1.1)*

Carry from v1.1 SPEC §6.3 unchanged.

---

## 5. DTOs

### 5.1 `Dtos/ScraperConfigDto.cs`

```csharp
using System.Text.Json;

namespace BBWM.WebScraper.Dtos;

public class ScraperConfigDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public JsonElement ConfigJson { get; set; }
    public int SchemaVersion { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Shared { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? OriginClientId { get; set; }
    public string? OriginWorkerName { get; set; }       // mapped via WorkerConnection lookup
    public int Version { get; set; }                    // for If-Match
}

public class CreateScraperConfigDto
{
    public Guid? SuggestedId { get; set; }              // idempotency key
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public JsonElement ConfigJson { get; set; }
    public int SchemaVersion { get; set; } = 3;
    public bool Shared { get; set; }
}

public class ScraperConfigSubscriberDto
{
    public Guid WorkerId { get; set; }
    public string WorkerName { get; set; } = "";
    public DateTimeOffset LastPulledAt { get; set; }
}
```

### 5.2 `Dtos/TaskDto.cs`

```csharp
namespace BBWM.WebScraper.Dtos;

public class TaskDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public List<TaskBlockTreeDto> Blocks { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}

public class SaveTaskDto
{
    public string Name { get; set; } = "";
    public List<TaskBlockTreeDto> Blocks { get; set; } = new();
}
```

### 5.3 `Dtos/TaskBlockDto.cs`

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Data/Dto/TaskBlockDto.cs`. Change namespace `WebScrape.Data.Dto` → `BBWM.WebScraper.Dtos`; `using WebScrape.Data.Enums;` → `using BBWM.WebScraper.Enums;`. Body unchanged.

The file contains: `TaskBlockTreeDto`, `LoopBlockConfigDto`, `ScrapeBlockConfigDto`, `StepBindingDto`.

### 5.4 `Dtos/ValidationErrorDto.cs`

```csharp
namespace BBWM.WebScraper.Dtos;

public class ValidationErrorDto
{
    public string Code { get; set; } = "";
    public Guid? BlockId { get; set; }
    public Guid? LoopBlockId { get; set; }
    public Guid? ScraperConfigId { get; set; }
    public string? StepId { get; set; }
    public string? Message { get; set; }
}

public static class ValidationCodes
{
    public const string MissingTaskName = "MissingTaskName";
    public const string DuplicateBlockId = "DuplicateBlockId";
    public const string InvalidParentReference = "InvalidParentReference";
    public const string InvalidBlockConfig = "InvalidBlockConfig";
    public const string MissingLoopName = "MissingLoopName";
    public const string TreeCycle = "TreeCycle";
    public const string ConfigNotOwned = "ConfigNotOwned";
    public const string BindingLiteralMissingValue = "BindingLiteralMissingValue";
    public const string LoopRefMissing = "LoopRefMissing";
    public const string LoopRefNotLoop = "LoopRefNotLoop";
    public const string LoopRefNonAncestor = "LoopRefNonAncestor";
    public const string LoopColumnNotFound = "LoopColumnNotFound";
    // D5.a additions:
    public const string MaxDepthExceeded = "MaxDepthExceeded";
    public const string MaxBlocksExceeded = "MaxBlocksExceeded";
}
```

### 5.5 `Dtos/HubPayloadDtos.cs` *(unchanged from v1.1)*

Carry verbatim from v1.1 SPEC §7.

### 5.6 `Dtos/QueueTaskDto.cs` *(extend v1.1)*

```csharp
using System.Text.Json;

namespace BBWM.WebScraper.Dtos;

public class QueueTaskDto
{
    public string Id { get; set; } = "";
    public string ConfigId { get; set; } = "";
    public string ConfigName { get; set; } = "";
    public List<string> SearchTerms { get; set; } = new();
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = "pending";
    public JsonElement? InlineConfig { get; set; }
    public string IterationLabel { get; set; } = "";                       // v2.0
    public Dictionary<string, string> IterationAssignments { get; set; } = new();  // v2.0
}
```

### 5.7 `Dtos/RunItemDto.cs`

```csharp
using System.Text.Json;

namespace BBWM.WebScraper.Dtos;

public class RunItemDto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid WorkerId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid ScraperConfigId { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public JsonElement? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PauseReason { get; set; }
    public int? ProgressPercent { get; set; }
    public string? CurrentTerm { get; set; }
    public string? CurrentStep { get; set; }
    public string? Phase { get; set; }
    public string IterationLabel { get; set; } = "";
}
```

> **No CreateRunDto** — runs are created through `POST /api/run-batches`.

### 5.8 `Dtos/WorkerDto.cs` *(unchanged from v1.1)*

Carry verbatim.

### 5.9 `Dtos/BatchProgressDto.cs` *(new — D4.b)*

```csharp
namespace BBWM.WebScraper.Dtos;

public class BatchProgressDto
{
    public Guid BatchId { get; set; }
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }       // includes Cancelled
    public int Running { get; set; }      // includes Sent + Running + Paused
    public int Pending { get; set; }
    public int OverallPercent { get; set; }   // (Completed + Failed) * 100 / Total
}
```

### 5.10 `Dtos/RunBatchDto.cs`

```csharp
namespace BBWM.WebScraper.Dtos;

public class RunBatchListItemDto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string TaskName { get; set; } = "";
    public Guid WorkerId { get; set; }
    public string WorkerName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int TotalItems { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCount { get; set; }
}

public class RunBatchDetailDto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string TaskName { get; set; } = "";
    public Guid WorkerId { get; set; }
    public string WorkerName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<RunItemDto> RunItems { get; set; } = new();
}

public class RunBatchListQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? TaskId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class CreateRunBatchDto
{
    public Guid TaskId { get; set; }
    public Guid WorkerId { get; set; }
}
```

### 5.11 `Dtos/ExpansionDto.cs`

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Data/Dto/ExpansionDto.cs` (or wherever `ExpansionPreview`, `ExpansionResult`, `ExpansionOutcome` live in the testbed — check `WebScrape.Services/Interfaces/IQueueExpansionService.cs` and `WebScrape.Services/Expansion/IBlockExpander.cs` for the record definitions, port to `BBWM.WebScraper.Dtos`).

Required types: `enum ExpansionOutcome { Ok, NotFound, Forbidden, BatchEmpty, BatchTooLarge, NestedLoopUnsupported }`; `record ExpansionPreview(ExpansionOutcome Outcome, int Count, List<ExpansionResult> Results, List<string> Warnings, string? Error = null)`; `record ExpansionResult(Guid ScrapeBlockId, Guid ScraperConfigId, string ConfigName, Dictionary<Guid, string> Assignments, string IterationLabel, JsonElement PatchedConfigJson, List<string> SearchTerms)`.

### 5.12 `Dtos/PagedResultDto.cs`

```csharp
namespace BBWM.WebScraper.Dtos;

public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
```

---

## 6. AutoMapper profile — `Mapping/WebScraperAutoMapperProfile.cs`

```csharp
using System.Text.Json;
using AutoMapper;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;

namespace BBWM.WebScraper.Mapping;

public class WebScraperAutoMapperProfile : Profile
{
    public WebScraperAutoMapperProfile()
    {
        // ScraperConfig
        CreateMap<ScraperConfigEntity, ScraperConfigDto>()
            .ForMember(d => d.ConfigJson, o => o.MapFrom(s => s.ConfigJson.RootElement))
            .ForMember(d => d.OriginWorkerName, o => o.Ignore());  // resolved at service layer
        CreateMap<CreateScraperConfigDto, ScraperConfigEntity>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.UserId, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.LastSyncedAt, o => o.Ignore())
            .ForMember(d => d.OriginClientId, o => o.Ignore())
            .ForMember(d => d.Version, o => o.Ignore())
            .ForMember(d => d.ConfigJson, o => o.MapFrom(s => JsonDocument.Parse(s.ConfigJson.GetRawText(), default(JsonDocumentOptions))));
        CreateMap<ScraperConfigSubscription, ScraperConfigSubscriberDto>()
            .ForMember(d => d.WorkerName, o => o.MapFrom(s => s.Worker != null ? s.Worker.Name : ""));

        // Task — block tree projection at service layer (CreateMap covers shape only)
        CreateMap<TaskEntity, TaskDto>()
            .ForMember(d => d.Blocks, o => o.Ignore());  // built explicitly in TaskService.GetAsync

        // Worker
        CreateMap<WorkerConnection, WorkerDto>()
            .ForMember(d => d.Online, o => o.MapFrom(s => s.CurrentConnection != null));

        // RunItem
        CreateMap<RunItem, RunItemDto>()
            .ForMember(d => d.Result, o => o.MapFrom(s => s.ResultJsonb != null ? s.ResultJsonb.RootElement : (JsonElement?)null));
    }
}
```

> The `TaskBlock → TaskBlockTreeDto` projection is non-trivial (deserialize ConfigJsonb based on BlockType). Done in `TaskService.GetAsync` and `ListAsync`, not via AutoMapper.

---

## 7. Service interfaces

### 7.1 `Services/Interfaces/IScraperConfigService.cs`

```csharp
using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum CreateScraperConfigOutcome { Created, Idempotent, Conflict }
public enum UpdateScraperConfigOutcome { Updated, NotFound, PreconditionFailed, PreconditionRequired }
public enum DeleteScraperConfigOutcome { Deleted, NotFound, Forbidden, Referenced }

public record CreateScraperConfigResult(CreateScraperConfigOutcome Outcome, ScraperConfigDto Dto);
public record UpdateScraperConfigResult(UpdateScraperConfigOutcome Outcome, ScraperConfigDto? Dto, ScraperConfigDto? Current);
public record DeleteScraperConfigResult(DeleteScraperConfigOutcome Outcome, int ReferencingTaskCount);

public interface IScraperConfigService
{
    Task<List<ScraperConfigDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<List<ScraperConfigDto>> ListSharedAsync(string userId, CancellationToken ct = default);
    Task<ScraperConfigDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<CreateScraperConfigResult> CreateAsync(string userId, CreateScraperConfigDto dto, Guid? workerId, CancellationToken ct = default);
    Task<UpdateScraperConfigResult> UpdateAsync(string userId, Guid id, CreateScraperConfigDto dto, int? ifMatchVersion, Guid? workerId, CancellationToken ct = default);
    Task<DeleteScraperConfigResult> DeleteAsync(string userId, Guid id, CancellationToken ct = default);
    Task<List<ScraperConfigSubscriberDto>?> GetSubscribersAsync(string userId, Guid configId, CancellationToken ct = default);
    Task<bool> RecordSubscriptionAsync(string userId, Guid configId, Guid workerId, CancellationToken ct = default);
}
```

### 7.2 `Services/Interfaces/ITaskService.cs`

```csharp
using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum SaveTaskOutcome { Created, Updated, NotFound, Forbidden, ValidationFailed }
public enum DeleteTaskOutcome { Deleted, NotFound, Forbidden }

public record SaveTaskResult(SaveTaskOutcome Outcome, TaskDto? Task, List<ValidationErrorDto> Errors);

public interface ITaskService
{
    Task<List<TaskDto>> ListAsync(string userId, CancellationToken ct = default);
    Task<TaskDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<SaveTaskResult> SaveAsync(string userId, Guid? taskId, SaveTaskDto dto, CancellationToken ct = default);
    Task<DeleteTaskOutcome> DeleteAsync(string userId, Guid taskId, CancellationToken ct = default);
}
```

### 7.3 `Services/Interfaces/IRunService.cs`

```csharp
using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum CancelRunOutcome { Cancelled, NotFound, Forbidden, NotCancellable }

public interface IRunService
{
    Task RecordProgressAsync(string connectionId, TaskProgressDto payload, CancellationToken ct = default);
    Task CompleteAsync(string connectionId, TaskCompleteDto payload, CancellationToken ct = default);
    Task FailAsync(string connectionId, TaskErrorDto payload, CancellationToken ct = default);
    Task MarkPausedAsync(string connectionId, TaskPausedDto payload, CancellationToken ct = default);
    Task<RunItemDto?> GetAsync(string userId, Guid id, CancellationToken ct = default);
    Task<List<RunItemDto>> ListAsync(string userId, Guid? batchId, Guid? taskId, CancellationToken ct = default);
    Task<CancelRunOutcome> CancelAsync(string userId, Guid runId, CancellationToken ct = default);
    Task<byte[]?> ExportCsvAsync(string userId, Guid runId, CancellationToken ct = default);
}
```

### 7.4 `Services/Interfaces/IRunBatchService.cs`

```csharp
using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public enum RunBatchOutcome { Created, NotFound, Forbidden, WorkerOffline, BatchEmpty, BatchTooLarge, NestedLoopUnsupported }
public enum RunBatchExportOutcome { Ok, NotFound, Forbidden, BadFormat }

public record RunBatchDispatchResult(RunBatchOutcome Outcome, Guid? BatchId, int DispatchedCount, int FailedCount, string? Error);
public record RunBatchExportResult(RunBatchExportOutcome Outcome, byte[]? Bytes, string? FileName, string? ContentType);

public interface IRunBatchService
{
    Task<RunBatchDispatchResult> CreateAndDispatchAsync(string userId, Guid taskId, Guid workerId, CancellationToken ct = default);
    Task<RunBatchDetailDto?> GetAsync(string userId, Guid batchId, CancellationToken ct = default);
    Task<PagedResultDto<RunBatchListItemDto>> ListAsync(string userId, RunBatchListQueryDto query, CancellationToken ct = default);
    Task<RunBatchExportResult> ExportAsync(string userId, Guid batchId, string format, CancellationToken ct = default);
}
```

### 7.5 `Services/Interfaces/IWorkerService.cs` *(unchanged from v1.1)*

Carry from v1.1 SPEC §9.4 unchanged.

### 7.6 `Services/Interfaces/IWorkerNotifier.cs` *(extend v1.1)*

```csharp
using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public interface IWorkerNotifier
{
    Task SendReceiveTaskAsync(string connectionId, QueueTaskDto task, CancellationToken ct = default);
    Task SendCancelTaskAsync(string connectionId, string taskId, CancellationToken ct = default);
    Task SendResumeAfterPauseAsync(string connectionId, string taskId, CancellationToken ct = default);
    // v2.0 additions:
    Task SendBatchProgressToUserAsync(string userId, BatchProgressDto progress, CancellationToken ct = default);   // D4.b
    Task SendConfigDeletedToUserAsync(string userId, Guid configId, CancellationToken ct = default);               // D5.d.c
}
```

### 7.7 `Services/Interfaces/ITaskValidator.cs`

```csharp
using BBWM.WebScraper.Dtos;

namespace BBWM.WebScraper.Services.Interfaces;

public interface ITaskValidator
{
    Task<List<ValidationErrorDto>> ValidateAsync(string userId, SaveTaskDto dto, CancellationToken ct = default);
}
```

### 7.8 `Services/Interfaces/IQueueExpansionService.cs`

Port the interface signature from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Services/Interfaces/IQueueExpansionService.cs`. Change `Guid userId` → `string userId`. Keep the `BatchCap = 1000` constant. Returns `ExpansionPreview` (defined in `Dtos/ExpansionDto.cs`).

### 7.9 `Services/Interfaces/IRunCsvExporter.cs`

```csharp
using BBWM.WebScraper.Entities;

namespace BBWM.WebScraper.Services.Interfaces;

public interface IRunCsvExporter
{
    bool IsTabular(RunItem run);
    byte[] ExportRun(RunItem run, ScraperConfigEntity? liveConfig, RunBatch? batch);
    byte[] ExportBatch(RunBatch batch, IReadOnlyList<RunItem> items, ScraperConfigEntity? liveConfig);
}
```

---

## 8. Service implementations

### 8.1 `Services/Implementations/ScraperConfigService.cs`

Provide complete code below. Key novel pieces: D1.c transactional Update, D5.a idempotency on Create, D5.d.c notify-on-delete-of-shared.

```csharp
using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class ScraperConfigService : IScraperConfigService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly IWorkerNotifier _notifier;

    public ScraperConfigService(IDbContext db, IMapper mapper, IWorkerNotifier notifier)
    {
        _db = db;
        _mapper = mapper;
        _notifier = notifier;
    }

    public async Task<List<ScraperConfigDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var rows = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return await ProjectAsync(rows, ct);
    }

    public async Task<List<ScraperConfigDto>> ListSharedAsync(string userId, CancellationToken ct = default)
    {
        var rows = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.Shared)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return await ProjectAsync(rows, ct);
    }

    public async Task<ScraperConfigDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (row is null) return null;
        return (await ProjectAsync(new[] { row }, ct)).First();
    }

    public async Task<CreateScraperConfigResult> CreateAsync(string userId, CreateScraperConfigDto dto, Guid? workerId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // D5.a idempotency check
        if (dto.SuggestedId.HasValue)
        {
            var existing = await _db.Set<ScraperConfigEntity>()
                .FirstOrDefaultAsync(c => c.Id == dto.SuggestedId.Value && c.UserId == userId, ct);
            if (existing is not null)
            {
                var sameName = existing.Name == dto.Name;
                var sameDomain = existing.Domain == dto.Domain;
                var sameJson = existing.ConfigJson.RootElement.GetRawText() == dto.ConfigJson.GetRawText();
                if (sameName && sameDomain && sameJson)
                    return new(CreateScraperConfigOutcome.Idempotent, (await ProjectAsync(new[] { existing }, ct)).First());
                return new(CreateScraperConfigOutcome.Conflict, (await ProjectAsync(new[] { existing }, ct)).First());
            }
        }

        var entity = new ScraperConfigEntity
        {
            Id = dto.SuggestedId ?? Guid.NewGuid(),
            UserId = userId,
            Name = dto.Name,
            Domain = dto.Domain,
            ConfigJson = JsonDocument.Parse(dto.ConfigJson.GetRawText()),
            SchemaVersion = dto.SchemaVersion <= 0 ? 3 : dto.SchemaVersion,
            Shared = dto.Shared,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
        if (workerId.HasValue)
        {
            entity.OriginClientId = workerId.Value.ToString();
            entity.LastSyncedAt = now;
        }
        _db.Set<ScraperConfigEntity>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return new(CreateScraperConfigOutcome.Created, (await ProjectAsync(new[] { entity }, ct)).First());
    }

    public async Task<UpdateScraperConfigResult> UpdateAsync(string userId, Guid id, CreateScraperConfigDto dto, int? ifMatchVersion, Guid? workerId, CancellationToken ct = default)
    {
        // D1.c: explicit transaction wraps load → check → mutate → save.
        // EF concurrency token (Version with IsConcurrencyToken) catches races at the SQL level.
        using var tx = await _db.Database.BeginTransactionAsync(ct);
        var entity = await _db.Set<ScraperConfigEntity>()
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (entity is null) return new(UpdateScraperConfigOutcome.NotFound, null, null);

        // Shared configs require If-Match (D5 carry from v3.1 — defensive).
        if (entity.Shared && ifMatchVersion is null)
            return new(UpdateScraperConfigOutcome.PreconditionRequired, null, (await ProjectAsync(new[] { entity }, ct)).First());

        if (ifMatchVersion is not null && entity.Version != ifMatchVersion.Value)
            return new(UpdateScraperConfigOutcome.PreconditionFailed, null, (await ProjectAsync(new[] { entity }, ct)).First());

        var now = DateTimeOffset.UtcNow;
        entity.Name = dto.Name;
        entity.Domain = dto.Domain;
        entity.ConfigJson = JsonDocument.Parse(dto.ConfigJson.GetRawText());
        if (dto.SchemaVersion > 0) entity.SchemaVersion = dto.SchemaVersion;
        entity.Shared = dto.Shared;
        entity.UpdatedAt = now;
        entity.Version++;
        if (workerId.HasValue)
        {
            entity.OriginClientId = workerId.Value.ToString();
            entity.LastSyncedAt = now;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer beat us — surface as PreconditionFailed.
            return new(UpdateScraperConfigOutcome.PreconditionFailed, null, null);
        }
        await tx.CommitAsync(ct);
        return new(UpdateScraperConfigOutcome.Updated, (await ProjectAsync(new[] { entity }, ct)).First(), null);
    }

    public async Task<DeleteScraperConfigResult> DeleteAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Set<ScraperConfigEntity>()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null) return new(DeleteScraperConfigOutcome.NotFound, 0);
        if (entity.UserId != userId) return new(DeleteScraperConfigOutcome.Forbidden, 0);

        // Reference check — any TaskBlock of kind Scrape pointing at this configId?
        // ConfigJsonb has shape { "scraperConfigId": "<guid>", ... } for Scrape blocks.
        var configIdString = entity.Id.ToString();
        var referencingCount = await _db.Set<TaskBlock>()
            .Where(b => b.BlockType == Enums.BlockType.Scrape)
            .CountAsync(b => EF.Functions.Like(
                b.ConfigJsonb.RootElement.GetRawText(),
                $"%{configIdString}%"), ct);
        // ↑ This is a coarse SQL-side filter; refine with in-memory parse if needed for FP-resistance.
        // For v2.0 we accept the LIKE as a pragmatic provider-portable check.
        if (referencingCount > 0)
            return new(DeleteScraperConfigOutcome.Referenced, referencingCount);

        // D5.d.c: capture subscribers BEFORE deletion (cascade will wipe the join rows).
        var subscriberWorkerIds = await _db.Set<ScraperConfigSubscription>()
            .Where(s => s.ScraperConfigId == id)
            .Select(s => s.WorkerId)
            .ToListAsync(ct);

        var subscriberUserIds = await _db.Set<WorkerConnection>()
            .Where(w => subscriberWorkerIds.Contains(w.Id))
            .Select(w => w.UserId)
            .Distinct()
            .ToListAsync(ct);

        _db.Set<ScraperConfigEntity>().Remove(entity);
        await _db.SaveChangesAsync(ct);

        // Notify each affected user's group (offline users miss it; that's documented).
        foreach (var subscriberUserId in subscriberUserIds)
        {
            try { await _notifier.SendConfigDeletedToUserAsync(subscriberUserId, id, ct); }
            catch { /* best-effort notification */ }
        }
        return new(DeleteScraperConfigOutcome.Deleted, 0);
    }

    public async Task<List<ScraperConfigSubscriberDto>?> GetSubscribersAsync(string userId, Guid configId, CancellationToken ct = default)
    {
        // D5.c.a: owner-only.
        var owns = await _db.Set<ScraperConfigEntity>()
            .AnyAsync(c => c.Id == configId && c.UserId == userId, ct);
        if (!owns) return null;

        var subs = await _db.Set<ScraperConfigSubscription>()
            .AsNoTracking()
            .Include(s => s.Worker)
            .Where(s => s.ScraperConfigId == configId)
            .ToListAsync(ct);
        return _mapper.Map<List<ScraperConfigSubscriberDto>>(subs);
    }

    public async Task<bool> RecordSubscriptionAsync(string userId, Guid configId, Guid workerId, CancellationToken ct = default)
    {
        // D5.b: target shared, worker is caller's, idempotent.
        var config = await _db.Set<ScraperConfigEntity>()
            .FirstOrDefaultAsync(c => c.Id == configId, ct);
        if (config is null || !config.Shared) return false;

        var worker = await _db.Set<WorkerConnection>()
            .FirstOrDefaultAsync(w => w.Id == workerId, ct);
        if (worker is null || worker.UserId != userId) return false;

        var existing = await _db.Set<ScraperConfigSubscription>()
            .FirstOrDefaultAsync(s => s.ScraperConfigId == configId && s.WorkerId == workerId, ct);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            _db.Set<ScraperConfigSubscription>().Add(new ScraperConfigSubscription
            {
                ScraperConfigId = configId,
                WorkerId = workerId,
                LastPulledAt = now,
            });
        }
        else
        {
            existing.LastPulledAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // Shared projection: maps entities to DTOs and resolves OriginWorkerName via WorkerConnection lookup.
    private async Task<List<ScraperConfigDto>> ProjectAsync(IEnumerable<ScraperConfigEntity> rows, CancellationToken ct)
    {
        var rowList = rows.ToList();
        var clientIds = rowList
            .Select(r => r.OriginClientId)
            .Where(s => s is not null)
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
        var workerNames = clientIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Set<WorkerConnection>()
                .Where(w => clientIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var result = new List<ScraperConfigDto>(rowList.Count);
        foreach (var r in rowList)
        {
            var dto = _mapper.Map<ScraperConfigDto>(r);
            if (r.OriginClientId is not null && Guid.TryParse(r.OriginClientId, out var wid)
                && workerNames.TryGetValue(wid, out var name))
                dto.OriginWorkerName = name;
            result.Add(dto);
        }
        return result;
    }
}
```

### 8.2 `Services/Implementations/TaskService.cs`

```csharp
using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class TaskService : ITaskService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly ITaskValidator _validator;

    public TaskService(IDbContext db, IMapper mapper, ITaskValidator validator)
    {
        _db = db;
        _mapper = mapper;
        _validator = validator;
    }

    public async Task<List<TaskDto>> ListAsync(string userId, CancellationToken ct = default)
    {
        var tasks = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.Blocks)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return tasks.Select(BuildDto).ToList();
    }

    public async Task<TaskDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var task = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.Blocks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);
        return task is null ? null : BuildDto(task);
    }

    public async Task<SaveTaskResult> SaveAsync(string userId, Guid? taskId, SaveTaskDto dto, CancellationToken ct = default)
    {
        var errors = await _validator.ValidateAsync(userId, dto, ct);
        if (errors.Count > 0)
            return new(SaveTaskOutcome.ValidationFailed, null, errors);

        TaskEntity task;
        bool isNew;
        if (taskId.HasValue)
        {
            task = await _db.Set<TaskEntity>()
                .Include(t => t.Blocks)
                .FirstOrDefaultAsync(t => t.Id == taskId.Value, ct)
                ?? throw new InvalidOperationException();
            if (task is null) return new(SaveTaskOutcome.NotFound, null, new());
            if (task.UserId != userId) return new(SaveTaskOutcome.Forbidden, null, new());
            // Delete-then-insert blocks (simplest atomic strategy).
            _db.Set<TaskBlock>().RemoveRange(task.Blocks);
            task.Name = dto.Name;
            isNew = false;
        }
        else
        {
            task = new TaskEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = dto.Name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.Set<TaskEntity>().Add(task);
            isNew = true;
        }

        foreach (var b in dto.Blocks)
        {
            var configJson = SerializeBlockConfig(b);
            _db.Set<TaskBlock>().Add(new TaskBlock
            {
                Id = b.Id,
                TaskId = task.Id,
                ParentBlockId = b.ParentBlockId,
                BlockType = b.BlockType,
                OrderIndex = b.OrderIndex,
                ConfigJsonb = configJson,
            });
        }
        await _db.SaveChangesAsync(ct);

        // Re-fetch to project DTO with consistent order.
        var saved = await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Include(t => t.Blocks)
            .FirstAsync(t => t.Id == task.Id, ct);
        return new(isNew ? SaveTaskOutcome.Created : SaveTaskOutcome.Updated, BuildDto(saved), new());
    }

    public async Task<DeleteTaskOutcome> DeleteAsync(string userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await _db.Set<TaskEntity>().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return DeleteTaskOutcome.NotFound;
        if (task.UserId != userId) return DeleteTaskOutcome.Forbidden;
        _db.Set<TaskEntity>().Remove(task);
        await _db.SaveChangesAsync(ct);
        return DeleteTaskOutcome.Deleted;
    }

    private static JsonDocument SerializeBlockConfig(TaskBlockTreeDto b)
    {
        return b.BlockType switch
        {
            BlockType.Loop => JsonSerializer.SerializeToDocument(b.Loop ?? new LoopBlockConfigDto()),
            BlockType.Scrape => JsonSerializer.SerializeToDocument(b.Scrape ?? new ScrapeBlockConfigDto()),
            _ => JsonDocument.Parse("{}"),
        };
    }

    private static TaskDto BuildDto(TaskEntity task)
    {
        var blocks = task.Blocks.Select(b => new TaskBlockTreeDto
        {
            Id = b.Id,
            ParentBlockId = b.ParentBlockId,
            BlockType = b.BlockType,
            OrderIndex = b.OrderIndex,
            Loop = b.BlockType == BlockType.Loop
                ? JsonSerializer.Deserialize<LoopBlockConfigDto>(b.ConfigJsonb.RootElement.GetRawText())
                : null,
            Scrape = b.BlockType == BlockType.Scrape
                ? JsonSerializer.Deserialize<ScrapeBlockConfigDto>(b.ConfigJsonb.RootElement.GetRawText())
                : null,
        }).OrderBy(b => b.OrderIndex).ToList();
        return new TaskDto
        {
            Id = task.Id,
            Name = task.Name,
            CreatedAt = task.CreatedAt,
            Blocks = blocks,
        };
    }
}
```

### 8.3 `Services/Implementations/RunService.cs`

Carry v1.1's progress methods (with D4 connection-binding) verbatim. **Add** List, Cancel, Export, plus D4.b BatchProgress aggregation in each progress-recording method.

```csharp
using System.Text.Json;
using AutoMapper;
using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class RunService : IRunService
{
    private readonly IDbContext _db;
    private readonly IMapper _mapper;
    private readonly IWorkerNotifier _notifier;
    private readonly IRunCsvExporter _csv;

    public RunService(IDbContext db, IMapper mapper, IWorkerNotifier notifier, IRunCsvExporter csv)
    {
        _db = db;
        _mapper = mapper;
        _notifier = notifier;
        _csv = csv;
    }

    public async Task RecordProgressAsync(string connectionId, TaskProgressDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;
        if (run.Status == RunItemStatus.Sent || run.Status == RunItemStatus.Paused)
        {
            run.Status = RunItemStatus.Running;
            run.StartedAt ??= DateTimeOffset.UtcNow;
        }
        run.ProgressPercent = payload.Progress;
        run.CurrentTerm = payload.CurrentTerm;
        run.CurrentStep = payload.CurrentStep;
        run.Phase = payload.Phase;
        await _db.SaveChangesAsync(ct);
        await EmitBatchProgressAsync(run, ct);
    }

    public async Task CompleteAsync(string connectionId, TaskCompleteDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;
        var resultJson = JsonSerializer.Serialize(payload.Result);
        run.ResultJsonb = JsonDocument.Parse(resultJson);
        run.Status = RunItemStatus.Completed;
        run.CompletedAt = payload.CompletedAt == default ? DateTimeOffset.UtcNow : payload.CompletedAt;
        run.ProgressPercent = 100;
        await _db.SaveChangesAsync(ct);
        await EmitBatchProgressAsync(run, ct);
    }

    public async Task FailAsync(string connectionId, TaskErrorDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;
        run.Status = RunItemStatus.Failed;
        run.ErrorMessage = string.IsNullOrEmpty(payload.StepLabel) ? payload.Error : $"[{payload.StepLabel}] {payload.Error}";
        run.CompletedAt = payload.FailedAt == default ? DateTimeOffset.UtcNow : payload.FailedAt;
        await _db.SaveChangesAsync(ct);
        await EmitBatchProgressAsync(run, ct);
    }

    public async Task MarkPausedAsync(string connectionId, TaskPausedDto payload, CancellationToken ct = default)
    {
        var run = await LoadAndAuthoriseAsync(connectionId, payload.TaskId, ct);
        if (run is null) return;
        run.Status = RunItemStatus.Paused;
        run.PauseReason = payload.Reason;
        await _db.SaveChangesAsync(ct);
        await EmitBatchProgressAsync(run, ct);
    }

    public async Task<RunItemDto?> GetAsync(string userId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<RunItem>()
            .AsNoTracking()
            .Include(r => r.Task)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null || row.Task is null || row.Task.UserId != userId) return null;
        return _mapper.Map<RunItemDto>(row);
    }

    public async Task<List<RunItemDto>> ListAsync(string userId, Guid? batchId, Guid? taskId, CancellationToken ct = default)
    {
        var q = _db.Set<RunItem>()
            .AsNoTracking()
            .Include(r => r.Task)
            .Where(r => r.Task!.UserId == userId);
        if (batchId.HasValue) q = q.Where(r => r.BatchId == batchId.Value);
        if (taskId.HasValue) q = q.Where(r => r.TaskId == taskId.Value);
        var rows = await q.OrderBy(r => r.RequestedAt).ToListAsync(ct);
        return _mapper.Map<List<RunItemDto>>(rows);
    }

    public async Task<CancelRunOutcome> CancelAsync(string userId, Guid runId, CancellationToken ct = default)
    {
        var run = await _db.Set<RunItem>()
            .Include(r => r.Task)
            .Include(r => r.Worker)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Task is null) return CancelRunOutcome.NotFound;
        if (run.Task.UserId != userId) return CancelRunOutcome.Forbidden;
        if (run.Status is RunItemStatus.Completed or RunItemStatus.Failed or RunItemStatus.Cancelled)
            return CancelRunOutcome.NotCancellable;
        run.Status = RunItemStatus.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (run.Worker?.CurrentConnection is not null)
        {
            try { await _notifier.SendCancelTaskAsync(run.Worker.CurrentConnection, run.Id.ToString(), ct); }
            catch { /* best-effort */ }
        }
        await EmitBatchProgressAsync(run, ct);
        return CancelRunOutcome.Cancelled;
    }

    public async Task<byte[]?> ExportCsvAsync(string userId, Guid runId, CancellationToken ct = default)
    {
        var run = await _db.Set<RunItem>()
            .AsNoTracking()
            .Include(r => r.Task)
            .Include(r => r.Batch)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Task is null || run.Task.UserId != userId) return null;
        var liveConfig = await _db.Set<ScraperConfigEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == run.ScraperConfigId, ct);
        return _csv.ExportRun(run, liveConfig, run.Batch);
    }

    private async Task<RunItem?> LoadAndAuthoriseAsync(string connectionId, string runIdStr, CancellationToken ct)
    {
        if (!Guid.TryParse(runIdStr, out var runId)) return null;
        var run = await _db.Set<RunItem>()
            .Include(r => r.Worker)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Worker is null || run.Worker.CurrentConnection != connectionId) return null;
        return run;
    }

    // D4.b: aggregate batch state and emit BatchProgress to the batch owner's group.
    private async Task EmitBatchProgressAsync(RunItem run, CancellationToken ct)
    {
        if (run.BatchId is null) return;
        var batchId = run.BatchId.Value;
        var batch = await _db.Set<RunBatch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return;

        var counts = await _db.Set<RunItem>()
            .AsNoTracking()
            .Where(r => r.BatchId == batchId)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Completed = g.Count(r => r.Status == RunItemStatus.Completed),
                Failed = g.Count(r => r.Status == RunItemStatus.Failed || r.Status == RunItemStatus.Cancelled),
                Running = g.Count(r => r.Status == RunItemStatus.Sent || r.Status == RunItemStatus.Running || r.Status == RunItemStatus.Paused),
                Pending = g.Count(r => r.Status == RunItemStatus.Pending),
            })
            .FirstOrDefaultAsync(ct);
        if (counts is null) return;

        var dto = new BatchProgressDto
        {
            BatchId = batchId,
            Total = counts.Total,
            Completed = counts.Completed,
            Failed = counts.Failed,
            Running = counts.Running,
            Pending = counts.Pending,
            OverallPercent = counts.Total == 0 ? 0 : ((counts.Completed + counts.Failed) * 100 / counts.Total),
        };
        try { await _notifier.SendBatchProgressToUserAsync(batch.UserId, dto, ct); }
        catch { /* best-effort */ }
    }
}
```

### 8.4 `Services/Implementations/RunBatchService.cs`

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Services/Implementations/RunBatchService.cs` with these changes:
- Replace `WebScrapeDbContext _db` with `IDbContext _db`; use `_db.Set<T>()` instead of named DbSet properties.
- Change `Guid userId` → `string userId` everywhere.
- Update namespaces: `WebScrape.*` → `BBWM.WebScraper.*`.
- Keep the entire `CreateAndDispatchAsync` flow (worker check → expansion → snapshot build → batch+items insert → per-item dispatch → status updates).
- Keep `GetAsync`, `ListAsync`, `ExportAsync` unchanged in shape.

The v3.1 source already matches our `IRunBatchService` shape + outcome enums (we mirrored them). Mechanical port.

### 8.5 `Services/Implementations/WorkerService.cs` *(unchanged from v1.1)*

Carry from v1.1 SPEC §10.4 unchanged.

### 8.6 `Services/Implementations/TaskValidator.cs`

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Services/Implementations/TaskValidator.cs` with these changes:
- `WebScrapeDbContext _db` → `IDbContext _db`; `_db.ScraperConfigs` → `_db.Set<ScraperConfigEntity>()`.
- `Guid userId` → `string userId`.
- Namespaces.
- **D5.a additions**: prepend two new checks to `ValidateAsync`:

```csharp
// D5.a: structural guards (early exit before per-block work).
if (dto.Blocks.Count > 256)
{
    errors.Add(new ValidationErrorDto { Code = ValidationCodes.MaxBlocksExceeded });
    return errors;  // early — deeper checks would be expensive on a malformed payload
}

// Compute max depth from parent chain. Reject if > 16.
var byIdForDepth = dto.Blocks.ToDictionary(b => b.Id, b => b);
foreach (var b in dto.Blocks)
{
    int depth = 0;
    var cursor = b.ParentBlockId;
    var seen = new HashSet<Guid> { b.Id };
    while (cursor.HasValue && byIdForDepth.TryGetValue(cursor.Value, out var parent))
    {
        if (!seen.Add(cursor.Value)) break;  // cycle — caught by Pass 2 below
        depth++;
        if (depth > 16)
        {
            errors.Add(new ValidationErrorDto { Code = ValidationCodes.MaxDepthExceeded, BlockId = b.Id });
            return errors;
        }
        cursor = parent.ParentBlockId;
    }
}
```

Place these guards **before** Pass 1 (id uniqueness) so they short-circuit malformed input.

### 8.7 `Services/Implementations/QueueExpansionService.cs`

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Services/Implementations/QueueExpansionService.cs`. Same db→IDbContext + userId string + namespace changes.

### 8.8 `Services/Expansion/{IBlockExpander,ExpansionContext,ExpansionFrame,LoopBlockExpander,ScrapeBlockExpander}.cs`

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Services/Expansion/`. Same namespace adjustments. No logic changes.

### 8.9 `Services/Implementations/RunCsvExporter.cs`

Port verbatim from `c:/Users/und3r/blueberry-v3/backend/src/WebScrape.Services/Implementations/RunCsvExporter.cs`. Namespace changes only.

### 8.10 `Services/Hubs/ScraperHubWorkerNotifier.cs`

```csharp
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Hubs;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BBWM.WebScraper.Services.Hubs;

public class ScraperHubWorkerNotifier : IWorkerNotifier
{
    private readonly IHubContext<ScraperHub> _hub;
    public ScraperHubWorkerNotifier(IHubContext<ScraperHub> hub) => _hub = hub;

    public Task SendReceiveTaskAsync(string connectionId, QueueTaskDto task, CancellationToken ct = default)
        => _hub.Clients.Client(connectionId).SendAsync("ReceiveTask", task, ct);

    public Task SendCancelTaskAsync(string connectionId, string taskId, CancellationToken ct = default)
        => _hub.Clients.Client(connectionId).SendAsync("CancelTask", taskId, ct);

    public Task SendResumeAfterPauseAsync(string connectionId, string taskId, CancellationToken ct = default)
        => _hub.Clients.Client(connectionId).SendAsync("ResumeAfterPause", taskId, ct);

    public Task SendBatchProgressToUserAsync(string userId, BatchProgressDto progress, CancellationToken ct = default)
        => _hub.Clients.Group($"user:{userId}").SendAsync("BatchProgress", progress, ct);

    public Task SendConfigDeletedToUserAsync(string userId, Guid configId, CancellationToken ct = default)
        => _hub.Clients.Group($"user:{userId}").SendAsync("ScraperConfigDeleted", configId.ToString(), ct);
}
```

---

## 9. Hub — `Hubs/ScraperHub.cs` *(unchanged from v1.1)*

Carry from v1.1 SPEC §12. Same 5 server methods (`RegisterWorker`, `TaskProgress`, `TaskComplete`, `TaskError`, `TaskPaused`); same `[Authorize(AuthenticationSchemes = ...)]`; same `OnConnectedAsync` group join. The new client-bound messages (`BatchProgress`, `ScraperConfigDeleted`) are emitted by `ScraperHubWorkerNotifier` via group sends — no hub method changes needed.

---

## 10. Controllers

### 10.1 `Controllers/ScraperConfigsController.cs`

```csharp
using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BBWM.WebScraper.Controllers;

[ApiController]
[Route("api/scraper-configs")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + ",Bearer")]
public class ScraperConfigsController : ControllerBase
{
    private readonly IScraperConfigService _configs;
    public ScraperConfigsController(IScraperConfigService configs) => _configs = configs;

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
            CreateScraperConfigOutcome.Created => CreatedAtAction(nameof(Get), new { id = result.Dto.Id }, result.Dto),
            CreateScraperConfigOutcome.Idempotent => Ok(result.Dto),
            CreateScraperConfigOutcome.Conflict => Conflict(result.Dto),
            _ => StatusCode(500),
        };
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateScraperConfigDto dto, [FromQuery] Guid? workerId, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
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
            UpdateScraperConfigOutcome.PreconditionRequired => StatusCode(StatusCodes.Status428PreconditionRequired, new { current = result.Current, error = "If-Match header required for shared configs" }),
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
            DeleteScraperConfigOutcome.Referenced => Conflict(new { referencingTaskCount = result.ReferencingTaskCount, error = "Config is referenced by tasks" }),
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
```

### 10.2 `Controllers/TasksController.cs`

```csharp
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
```

### 10.3 `Controllers/RunsController.cs`

```csharp
using BBWM.Core.Web.Extensions;
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
    public RunsController(IRunService runs) => _runs = runs;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? batchId, [FromQuery] Guid? taskId, CancellationToken ct)
        => Ok(await _runs.ListAsync(HttpContext.GetUserId(), batchId, taskId, ct));

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
```

### 10.4 `Controllers/RunBatchesController.cs`

```csharp
using BBWM.Core.Web.Extensions;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BBWM.WebScraper.Controllers;

[ApiController]
[Route("api/run-batches")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + ",Bearer")]
public class RunBatchesController : ControllerBase
{
    private readonly IRunBatchService _batches;
    public RunBatchesController(IRunBatchService batches) => _batches = batches;

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
    public async Task<IActionResult> Create([FromBody] CreateRunBatchDto body, CancellationToken ct)
    {
        var result = await _batches.CreateAndDispatchAsync(HttpContext.GetUserId(), body.TaskId, body.WorkerId, ct);
        return result.Outcome switch
        {
            RunBatchOutcome.Created => CreatedAtAction(nameof(Get), new { id = result.BatchId },
                new { batchId = result.BatchId, dispatchedCount = result.DispatchedCount, failedCount = result.FailedCount }),
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
            RunBatchExportOutcome.Ok => File(result.Bytes!, result.ContentType!, result.FileName),
            RunBatchExportOutcome.NotFound => NotFound(),
            RunBatchExportOutcome.Forbidden => Forbid(),
            RunBatchExportOutcome.BadFormat => BadRequest(new { error = "format must be 'csv' or 'json'" }),
            _ => StatusCode(500),
        };
    }
}
```

### 10.5 `Controllers/WorkersController.cs` *(unchanged from v1.1)*

Carry from v1.1 SPEC §13.4 unchanged.

---

## 11. `WebScraperModuleLinkage.cs`

```csharp
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
        services.AddScoped<IScraperConfigService, ScraperConfigService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IRunService, RunService>();
        services.AddScoped<IRunBatchService, RunBatchService>();
        services.AddScoped<IWorkerService, WorkerService>();
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
```

---

## 12. INSTALL.md updates

Edit the existing `src/BBWM.WebScraper/INSTALL.md` to reflect v2.0:

1. **§"Prerequisites"**: unchanged.
2. **§"Install steps step 4 (migrations)"**: change "WebScraperModule_Initial" → "WebScraperModule_Initial_v2"; mention that the schema includes 7 tables (`ScraperConfigs`, `ScraperConfigSubscriptions`, `Tasks`, `TaskBlocks`, `RunItems`, `RunBatches`, `WorkerConnections`).
3. **§"What you get"**: replace endpoint list with:
   - `/api/scraper-configs` GET/POST/PUT/DELETE; `/shared` GET; `/{id}/subscribers` GET; `/{id}/subscriptions` POST
   - `/api/tasks` GET/POST/PUT/DELETE; `/{id}/expand` GET
   - `/api/runs` GET/{id}/{cancel}/{csv}
   - `/api/run-batches` GET/POST/{id}/{export}
   - `/api/workers` GET
   - `/api/scraper-hub` SignalR — server methods + `BatchProgress` + `ScraperConfigDeleted` events
4. **New §"Concurrency"**: explain that updates to scraper configs require an `If-Match: <version>` header; the version comes from `Version` in the GET response and is incremented on each successful update.
5. **New §"Sharing"**: shared configs propagate to other users' subscribers; deleting a shared config cascades subscription rows and emits `ScraperConfigDeleted(configId)` on each subscriber's user group; UIs should handle this event.
6. **§"Uninstall"**: same revert order.

---

## 13. Test project changes (`tests/BBWM.WebScraper.Tests/`)

### 13.1 Carry tests from v1.1

These continue working with field/type changes for the new shapes:
- `Services/WorkerServiceTests.cs` — unchanged behaviour; just adapt to `string UserId`.
- `Mapping/AutoMapperProfileTests.cs` — keep `Profile_IsValid` test; add new mapping tests for the v2.0 maps.
- `EntityConfiguration/ConfigurationsApplyTests.cs` — extend assertion to all 7 entities.
- `TestSupport/TestWebScraperDbContext.cs` — unchanged.
- `TestSupport/TestDb.cs` — unchanged.

### 13.2 Rewrite

- `Services/RunServiceTests.cs` — keep v1.1's connection-binding tests; add tests for `ListAsync`, `CancelAsync` (each outcome), `ExportCsvAsync`, and **D4.b BatchProgress aggregation** (Mock `IWorkerNotifier`; assert `SendBatchProgressToUserAsync` called with correct counts after each progress event).
- `Services/ScraperConfigServiceTests.cs` — keep v1.1's CRUD basics; add tests for: idempotency match → `Idempotent`, idempotency mismatch → `Conflict`, If-Match version match → `Updated` + Version increments, If-Match mismatch → `PreconditionFailed`, missing If-Match on shared config → `PreconditionRequired`, Delete with no subscribers → notifier NOT called, **Delete with subscribers → notifier called once per affected user**, Delete with task references → `Referenced`, ListShared filtering, GetSubscribers owner-only, RecordSubscription rules.
- `Services/TaskServiceTests.cs` — replace v1.1's Create test with: SaveAsync (Create) round-trips block tree, SaveAsync (Update) replaces blocks, SaveAsync with validator errors → `ValidationFailed` outcome with errors list, DeleteAsync.

### 13.3 New test classes

- `Services/TaskValidatorTests.cs` — one test per `ValidationCodes` value (14 codes × happy + sad = ~22 tests). Use real validator + InMemory db with fixture configs.
- `Services/QueueExpansionTests.cs` — single Scrape leaf; Loop with N values; nested Loop rejection; LoopRef binding patches config; Literal binding; Unbound binding; expansion > 1000 → BatchTooLarge; ownership rejection; iteration label format.
- `Services/RunBatchServiceTests.cs` — one test per `RunBatchOutcome`; PopulateSnapshot serialised correctly; per-item `ReceiveTask` sent; partial-failure rollback (transaction); Get returns aggregate.
- `Services/RunCsvExporterTests.cs` — tabular vs legacy formats; per-run vs per-batch; UTF-8 byte output; missing PopulateSnapshot fallback; null/empty result.
- `Hubs/BatchProgressHubTests.cs` — test that progress events trigger `SendBatchProgressToUserAsync` with correct aggregate counts and `OverallPercent`.

### 13.4 Test csproj `tests/BBWM.WebScraper.Tests/BBWM.WebScraper.Tests.csproj` *(unchanged from v1.1)*

Same package refs, same project ref. Sonnet doesn't need to modify this.

---

## 14. Verification

### 14.1 Build

```bash
dotnet build BBWM.WebScraper.sln
```
0 errors. Inherited NU1903 from BBWM.Core (AutoMapper 12) is acceptable.

### 14.2 Unit tests

```bash
dotnet test tests/BBWM.WebScraper.Tests/
```
~106 tests pass.

### 14.3 Manual smoke against pharmacy-planet rig

Steps E3.1-E3.18 from the staged review. After completion, **revert pharmacy-planet** with `git restore project/` and `git clean -fd modules/BBWM.WebScraper/` so its tree returns to the pre-test state (only the 3 pre-existing unrelated changes).

---

## 15. What is NOT in this spec (deferred)

- **Permission policy** `WebScraper.Use` — v2.1.
- **Pharmacy-planet UI screens** — sibling sub-spec in pharmacy-planet repo.
- **NuGet packaging** — copy-into-host until host #3.
- **Cached batch counters on `RunBatch`** — D4.b aggregation runs simple per-event query for v2.0; promote to denormalized counters in v2.0.x if profiling demands.
- **Streaming CSV export** — in-memory StringBuilder for v2.0; defer for very large batches.
- **User deletion cascade** — application-layer cleanup deferred.
- **Webscrape testbed retirement** — testbed in `blueberry-v3/backend/` stays alive; this spec does not touch it.
- **Postgres / MySQL live runtime tests** — only migration generation exercised.
- **v1.1 → v2.0 migration path** — v1.1 hasn't shipped to a host; no upgrade migration needed.
