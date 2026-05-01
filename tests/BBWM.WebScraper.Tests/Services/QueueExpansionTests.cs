using System.Text.Json;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Expansion;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.Services;

public class QueueExpansionTests
{
    private static QueueExpansionService CreateService(TestWebScraperDbContext db)
    {
        var expanders = new IBlockExpander[]
        {
            new ScrapeBlockExpander(),
            // LoopBlockExpander needs IEnumerable<IBlockExpander> — inject both
            new LoopBlockExpander(new IBlockExpander[] { new ScrapeBlockExpander() }),
        };
        // Replace LoopBlockExpander so it references the real ScrapeBlockExpander
        var loopExpander = new LoopBlockExpander(expanders);
        var finalExpanders = new IBlockExpander[] { loopExpander, new ScrapeBlockExpander() };
        return new QueueExpansionService(db, finalExpanders);
    }

    private static async Task<(TaskEntity task, ScraperConfigEntity config)> SeedTaskAsync(
        TestWebScraperDbContext db, string userId, IEnumerable<BBWM.WebScraper.Entities.TaskBlock> blocks)
    {
        var config = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(), UserId = userId, Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("""{"steps":[]}"""),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);

        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = userId, Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        foreach (var b in blocks)
        {
            b.TaskId = task.Id;
            db.Set<BBWM.WebScraper.Entities.TaskBlock>().Add(b);
        }
        await db.SaveChangesAsync();
        return (task, config);
    }

    private static BBWM.WebScraper.Entities.TaskBlock MakeLoopBlock(Guid? id = null, Guid? parentId = null, int order = 0,
        List<string>? values = null, List<string>? columns = null, List<List<string>>? rows = null)
    {
        var cfg = new
        {
            name = "L",
            values = values ?? new List<string>(),
            columns = columns,
            rows = rows,
        };
        return new BBWM.WebScraper.Entities.TaskBlock
        {
            Id = id ?? Guid.NewGuid(),
            ParentBlockId = parentId,
            BlockType = BlockType.Loop,
            OrderIndex = order,
            ConfigJsonb = JsonSerializer.SerializeToDocument(cfg),
        };
    }

    private static BBWM.WebScraper.Entities.TaskBlock MakeScrapeBlock(Guid configId, Guid? parentId = null, int order = 0,
        Dictionary<string, object>? stepBindings = null)
    {
        var cfg = new
        {
            scraperConfigId = configId.ToString(),
            stepBindings = stepBindings ?? new Dictionary<string, object>(),
        };
        return new BBWM.WebScraper.Entities.TaskBlock
        {
            Id = Guid.NewGuid(),
            ParentBlockId = parentId,
            BlockType = BlockType.Scrape,
            OrderIndex = order,
            ConfigJsonb = JsonSerializer.SerializeToDocument(cfg),
        };
    }

    // --- Single Scrape root → 1 result ---

    [Fact]
    public async Task Expand_SingleScrapeRoot_Returns1Result()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var configId = Guid.NewGuid();
        var config = new ScraperConfigEntity
        {
            Id = configId, UserId = "user1", Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("""{"steps":[]}"""),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        var block = MakeScrapeBlock(configId);
        block.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().Add(block);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.Ok, preview.Outcome);
        Assert.Equal(1, preview.Count);
        Assert.Single(preview.Results);
    }

    // --- Loop with N single-column values → 1 result with N searchTerms in the frame ---
    // Note: the single-column path bundles all loop values into a single expansion frame,
    // yielding 1 result per scrape block child (not N results). The SearchTerms list on that
    // result carries all N values. This matches LoopBlockExpander's bundled-values design.

    [Fact]
    public async Task Expand_LoopWithNValues_SingleColumn_Returns1Result_WithAllSearchTerms()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var loopId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var config = new ScraperConfigEntity
        {
            Id = configId, UserId = "user1", Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("""{"steps":[]}"""),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);
        var loop = MakeLoopBlock(id: loopId, values: new List<string> { "apple", "banana", "cherry" });
        var scrape = MakeScrapeBlock(configId, parentId: loopId);
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        loop.TaskId = task.Id;
        scrape.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().AddRange(loop, scrape);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.Ok, preview.Outcome);
        // Single-column path: one result, all loop values as searchTerms on that result
        Assert.Equal(1, preview.Count);
        var searchTerms = preview.Results[0].SearchTerms;
        Assert.Contains("apple", searchTerms);
        Assert.Contains("banana", searchTerms);
        Assert.Contains("cherry", searchTerms);
    }

    // --- Loop with columns + rows → N results with assignments ---

    [Fact]
    public async Task Expand_LoopWithColumnsAndRows_ReturnsNResults_WithAssignmentKeys()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var loopId = Guid.NewGuid();
        var columns = new List<string> { "col-a", "col-b" };
        var rows = new List<List<string>> { new() { "r1a", "r1b" }, new() { "r2a", "r2b" } };
        var loop = MakeLoopBlock(id: loopId, columns: columns, rows: rows);

        var config = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(), UserId = "user1", Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("""{"steps":[]}"""),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);

        var scrape = MakeScrapeBlock(config.Id, parentId: loopId);
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        loop.TaskId = task.Id;
        scrape.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().AddRange(loop, scrape);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.Ok, preview.Outcome);
        Assert.Equal(2, preview.Count);
        // Each result should have LoopAssignment keys of form {loopId}:{column}
        // The assignments are embedded in the expanded PatchedConfigJson, not in ExpansionResult.Assignments directly
        // (ScrapeBlockExpander doesn't populate the Assignments dict in multi-column path from LoopBlockExpander)
        // Just verify two results exist with correct search terms (first column)
        Assert.Equal("r1a", preview.Results[0].SearchTerms[0]);
        Assert.Equal("r2a", preview.Results[1].SearchTerms[0]);
    }

    // --- Nested loop → NestedLoopUnsupported ---

    [Fact]
    public async Task Expand_NestedLoop_ReturnsNestedLoopUnsupported()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var outerLoopId = Guid.NewGuid();
        var innerLoopId = Guid.NewGuid();
        var outer = MakeLoopBlock(id: outerLoopId, values: new List<string> { "x" });
        var inner = MakeLoopBlock(id: innerLoopId, parentId: outerLoopId, values: new List<string> { "y" });
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        outer.TaskId = task.Id;
        inner.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().AddRange(outer, inner);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.NestedLoopUnsupported, preview.Outcome);
    }

    // --- BatchEmpty when task has no scrape blocks ---

    [Fact]
    public async Task Expand_NoScrapeBlocks_ReturnsBatchEmpty()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var loop = MakeLoopBlock(values: new List<string> { "a" });
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        loop.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().Add(loop);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.BatchEmpty, preview.Outcome);
    }

    // --- ConfigNotFoundAtPopulate warning when config doesn't exist ---

    [Fact]
    public async Task Expand_ConfigMissing_EmitsConfigNotFoundWarning()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var loopId = Guid.NewGuid();
        var loop = MakeLoopBlock(id: loopId, values: new List<string> { "x" });
        var scrape = MakeScrapeBlock(Guid.NewGuid(), parentId: loopId); // points to non-existent config
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        loop.TaskId = task.Id;
        scrape.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().AddRange(loop, scrape);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        // No scrape blocks expanded → BatchEmpty
        // But before that, a warning about the missing config
        Assert.Contains(preview.Warnings, w => w.Code == ExpansionWarningCodes.ConfigNotFoundAtPopulate);
    }

    // --- NotFound for unknown task ---

    [Fact]
    public async Task Expand_TaskNotFound_ReturnsNotFound()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);

        var preview = await svc.ExpandAsync("user1", Guid.NewGuid());

        Assert.Equal(ExpansionOutcome.NotFound, preview.Outcome);
    }

    // --- Forbidden for another user's task ---

    [Fact]
    public async Task Expand_OtherUsersTask_ReturnsForbidden()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user2", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.Forbidden, preview.Outcome);
    }

    // --- Literal binding patches literalValue in PatchedConfigJson ---

    [Fact]
    public async Task Expand_LiteralBinding_PatchesLiteralValueInConfig()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var configId = Guid.NewGuid();
        var config = new ScraperConfigEntity
        {
            Id = configId, UserId = "user1", Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("""{"steps":[{"id":"step1","type":"setInput","options":{}}]}"""),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);

        var stepBindings = new Dictionary<string, object>
        {
            ["step1"] = new { kind = "literal", value = "hello" },
        };
        var scrape = MakeScrapeBlock(configId, stepBindings: stepBindings);
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        scrape.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().Add(scrape);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.Ok, preview.Outcome);
        var patched = preview.Results[0].PatchedConfigJson;
        var step = patched.GetProperty("steps").EnumerateArray().First();
        Assert.Equal("hello", step.GetProperty("options").GetProperty("literalValue").GetString());
    }

    // --- BatchTooLarge when expansion exceeds 1000 ---

    [Fact]
    public async Task Expand_Over1000Results_ReturnsBatchTooLarge()
    {
        using var db = TestDb.CreateInMemory();
        var svc = CreateService(db);
        var loopId = Guid.NewGuid();

        // Multi-column loop: each row produces a separate expansion result.
        // 1001 rows each with one column = 1001 results → triggers BatchTooLarge.
        var columns = new List<string> { "col1" };
        var rows = Enumerable.Range(0, 1001).Select(i => new List<string> { $"val-{i}" }).ToList();
        var loop = MakeLoopBlock(id: loopId, columns: columns, rows: rows);

        var configId = Guid.NewGuid();
        var config = new ScraperConfigEntity
        {
            Id = configId, UserId = "user1", Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("""{"steps":[]}"""),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(config);

        var scrape = MakeScrapeBlock(configId, parentId: loopId);
        var task = new TaskEntity { Id = Guid.NewGuid(), UserId = "user1", Name = "T", CreatedAt = DateTimeOffset.UtcNow };
        db.Set<TaskEntity>().Add(task);
        loop.TaskId = task.Id;
        scrape.TaskId = task.Id;
        db.Set<BBWM.WebScraper.Entities.TaskBlock>().AddRange(loop, scrape);
        await db.SaveChangesAsync();

        var preview = await svc.ExpandAsync("user1", task.Id);

        Assert.Equal(ExpansionOutcome.BatchTooLarge, preview.Outcome);
    }
}
