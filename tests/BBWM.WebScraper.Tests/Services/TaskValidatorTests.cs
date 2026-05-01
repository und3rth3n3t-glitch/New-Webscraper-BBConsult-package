using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.Services;

public class TaskValidatorTests
{
    private static TaskValidator CreateValidator(TestWebScraperDbContext db) => new TaskValidator(db);

    private static SaveTaskDto MakeValidTask(string name = "Valid Task") =>
        new SaveTaskDto { Name = name, Blocks = new List<TaskBlockTreeDto>() };

    private static TaskBlockTreeDto LoopBlock(Guid? id = null, Guid? parentId = null, string name = "Loop", int order = 0) =>
        new TaskBlockTreeDto
        {
            Id = id ?? Guid.NewGuid(),
            ParentBlockId = parentId,
            BlockType = BlockType.Loop,
            OrderIndex = order,
            Loop = new LoopBlockConfigDto { Name = name, Values = new List<string> { "a" } },
        };

    private static TaskBlockTreeDto ScrapeBlock(Guid configId, Guid? parentId = null, Guid? id = null,
        Dictionary<string, StepBindingDto>? bindings = null) =>
        new TaskBlockTreeDto
        {
            Id = id ?? Guid.NewGuid(),
            ParentBlockId = parentId,
            BlockType = BlockType.Scrape,
            OrderIndex = 0,
            Scrape = new ScrapeBlockConfigDto
            {
                ScraperConfigId = configId,
                StepBindings = bindings ?? new Dictionary<string, StepBindingDto>(),
            },
        };

    private static async Task<ScraperConfigEntity> SeedConfigAsync(TestWebScraperDbContext db, string userId)
    {
        var cfg = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(), UserId = userId, Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("{}"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Set<ScraperConfigEntity>().Add(cfg);
        await db.SaveChangesAsync();
        return cfg;
    }

    // --- Happy paths ---

    [Fact]
    public async Task Validate_ValidTask_NoErrors()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "user1");
        var validator = CreateValidator(db);
        var loopId = Guid.NewGuid();
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: loopId, name: "L1"),
                ScrapeBlock(cfg.Id, parentId: loopId),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Empty(errors);
    }

    // --- MissingTaskName ---

    [Fact]
    public async Task Validate_BlankName_ReturnsMissingTaskName()
    {
        using var db = TestDb.CreateInMemory();
        var validator = CreateValidator(db);
        var dto = MakeValidTask("");

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.MissingTaskName);
    }

    // --- DuplicateBlockId ---

    [Fact]
    public async Task Validate_DuplicateBlockId_ReturnsDuplicateBlockId()
    {
        using var db = TestDb.CreateInMemory();
        var validator = CreateValidator(db);
        var id = Guid.NewGuid();
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: id, name: "L1"),
                LoopBlock(id: id, name: "L2"),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.DuplicateBlockId);
    }

    // --- InvalidParentReference ---

    [Fact]
    public async Task Validate_ParentRefDoesNotExist_ReturnsInvalidParentReference()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "user1");
        var validator = CreateValidator(db);
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                ScrapeBlock(cfg.Id, parentId: Guid.NewGuid()),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.InvalidParentReference);
    }

    // --- MissingLoopName ---

    [Fact]
    public async Task Validate_LoopMissingName_ReturnsMissingLoopName()
    {
        using var db = TestDb.CreateInMemory();
        var validator = CreateValidator(db);
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(name: ""),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.MissingLoopName);
    }

    // --- ConfigNotOwned ---

    [Fact]
    public async Task Validate_ScrapeBlock_ConfigOwnedByOtherUser_ReturnsConfigNotOwned()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "other-user"); // owned by other-user
        var validator = CreateValidator(db);
        var loopId = Guid.NewGuid();
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: loopId, name: "L"),
                ScrapeBlock(cfg.Id, parentId: loopId),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.ConfigNotOwned);
    }

    // --- BindingLiteralMissingValue ---

    [Fact]
    public async Task Validate_LiteralBinding_MissingValue_ReturnsBindingLiteralMissingValue()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "user1");
        var validator = CreateValidator(db);
        var loopId = Guid.NewGuid();
        var bindings = new Dictionary<string, StepBindingDto>
        {
            ["step1"] = new StepBindingDto { Kind = BindingKind.Literal, Value = null },
        };
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: loopId, name: "L"),
                ScrapeBlock(cfg.Id, parentId: loopId, bindings: bindings),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.BindingLiteralMissingValue);
    }

    // --- LoopRefMissing ---

    [Fact]
    public async Task Validate_LoopRef_ToNonExistentBlock_ReturnsLoopRefMissing()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "user1");
        var validator = CreateValidator(db);
        var loopId = Guid.NewGuid();
        var nonExistentLoopId = Guid.NewGuid();
        var bindings = new Dictionary<string, StepBindingDto>
        {
            ["step1"] = new StepBindingDto { Kind = BindingKind.LoopRef, LoopBlockId = nonExistentLoopId },
        };
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: loopId, name: "L"),
                ScrapeBlock(cfg.Id, parentId: loopId, bindings: bindings),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.LoopRefMissing);
    }

    // --- LoopRefNotLoop ---

    [Fact]
    public async Task Validate_LoopRef_ToScrapeBlock_ReturnsLoopRefNotLoop()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "user1");
        var validator = CreateValidator(db);
        var loopId = Guid.NewGuid();
        var anotherScrapeId = Guid.NewGuid();
        // ScrapeBlock2 refs ScrapeBlock1's id as if it were a loop
        var bindings = new Dictionary<string, StepBindingDto>
        {
            ["step1"] = new StepBindingDto { Kind = BindingKind.LoopRef, LoopBlockId = anotherScrapeId },
        };
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: loopId, name: "L"),
                ScrapeBlock(cfg.Id, id: anotherScrapeId, parentId: loopId),
                ScrapeBlock(cfg.Id, parentId: loopId, bindings: bindings),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.LoopRefNotLoop);
    }

    // --- LoopRefNonAncestor ---

    [Fact]
    public async Task Validate_LoopRef_ToNonAncestorLoop_ReturnsLoopRefNonAncestor()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "user1");
        var validator = CreateValidator(db);
        var loop1Id = Guid.NewGuid();
        var loop2Id = Guid.NewGuid();
        // ScrapeBlock is under loop1, but binds to loop2 which is a sibling, not ancestor
        var bindings = new Dictionary<string, StepBindingDto>
        {
            ["step1"] = new StepBindingDto { Kind = BindingKind.LoopRef, LoopBlockId = loop2Id },
        };
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: loop1Id, name: "L1"),
                LoopBlock(id: loop2Id, name: "L2"),
                ScrapeBlock(cfg.Id, parentId: loop1Id, bindings: bindings),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.LoopRefNonAncestor);
    }

    // --- LoopColumnNotFound ---

    [Fact]
    public async Task Validate_LoopRef_ColumnNotInLoop_ReturnsLoopColumnNotFound()
    {
        using var db = TestDb.CreateInMemory();
        var cfg = await SeedConfigAsync(db, "user1");
        var validator = CreateValidator(db);
        var loopId = Guid.NewGuid();
        var bindings = new Dictionary<string, StepBindingDto>
        {
            ["step1"] = new StepBindingDto { Kind = BindingKind.LoopRef, LoopBlockId = loopId, Column = "nonexistent-col" },
        };
        var loopBlock = new TaskBlockTreeDto
        {
            Id = loopId,
            BlockType = BlockType.Loop,
            OrderIndex = 0,
            Loop = new LoopBlockConfigDto { Name = "L", Values = new(), Columns = new List<string> { "col-a" } },
        };
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                loopBlock,
                ScrapeBlock(cfg.Id, parentId: loopId, bindings: bindings),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.LoopColumnNotFound);
    }

    // --- D5.a: MaxDepthExceeded (chain deep enough that depth > 16) ---

    [Fact]
    public async Task Validate_DepthExceeds16_ReturnsMaxDepthExceeded()
    {
        using var db = TestDb.CreateInMemory();
        var validator = CreateValidator(db);

        // MaxDepth = 16. A chain of 18 blocks: block[17] has 17 ancestors, so depth == 17 > 16.
        // The spec says "depth > 16" fires for the 17th ancestor step.
        var blocks = new List<TaskBlockTreeDto>();
        Guid? parentId = null;
        for (int i = 0; i < 18; i++)
        {
            var id = Guid.NewGuid();
            blocks.Add(new TaskBlockTreeDto
            {
                Id = id,
                ParentBlockId = parentId,
                BlockType = BlockType.Loop,
                OrderIndex = i,
                Loop = new LoopBlockConfigDto { Name = $"L{i}", Values = new List<string> { "x" } },
            });
            parentId = id;
        }
        var dto = new SaveTaskDto { Name = "T", Blocks = blocks };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.MaxDepthExceeded);
    }

    // --- D5.a: MaxBlocksExceeded (257 blocks) ---

    [Fact]
    public async Task Validate_BlocksExceed256_ReturnsMaxBlocksExceeded()
    {
        using var db = TestDb.CreateInMemory();
        var validator = CreateValidator(db);

        var blocks = Enumerable.Range(0, 257)
            .Select(i => LoopBlock(name: $"L{i}", order: i))
            .ToList();
        var dto = new SaveTaskDto { Name = "T", Blocks = blocks };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.MaxBlocksExceeded);
        // Early exit — only one error returned
        Assert.Single(errors);
    }

    // --- TreeCycle ---

    [Fact]
    public async Task Validate_CycleDetected_ReturnsTreeCycle()
    {
        using var db = TestDb.CreateInMemory();
        var validator = CreateValidator(db);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                LoopBlock(id: id1, parentId: id2, name: "L1"),
                LoopBlock(id: id2, parentId: id1, name: "L2"),
            },
        };

        var errors = await validator.ValidateAsync("user1", dto);

        Assert.Contains(errors, e => e.Code == ValidationCodes.TreeCycle);
    }
}
