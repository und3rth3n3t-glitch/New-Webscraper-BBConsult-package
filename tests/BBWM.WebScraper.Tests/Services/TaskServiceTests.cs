using System.Text.Json;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Implementations;
using BBWM.WebScraper.Services.Interfaces;
using BBWM.WebScraper.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BBWM.WebScraper.Tests.Services;

public class TaskServiceTests
{
    private static (TaskService svc, Mock<ITaskValidator> validatorMock) CreateService(TestWebScraperDbContext db)
    {
        var validatorMock = new Mock<ITaskValidator>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<SaveTaskDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationErrorDto>());
        var svc = new TaskService(db, validatorMock.Object);
        return (svc, validatorMock);
    }

    private static SaveTaskDto MakeSimpleTask(string name = "My Task")
    {
        var loopId = Guid.NewGuid();
        var configId = Guid.NewGuid();
        var scrapeId = Guid.NewGuid();
        return new SaveTaskDto
        {
            Name = name,
            Blocks = new List<TaskBlockTreeDto>
            {
                new()
                {
                    Id = loopId,
                    BlockType = BlockType.Loop,
                    OrderIndex = 0,
                    Loop = new LoopBlockConfigDto { Name = "Terms", Values = new List<string> { "a", "b" } },
                },
                new()
                {
                    Id = scrapeId,
                    ParentBlockId = loopId,
                    BlockType = BlockType.Scrape,
                    OrderIndex = 0,
                    Scrape = new ScrapeBlockConfigDto { ScraperConfigId = configId, StepBindings = new() },
                },
            },
        };
    }

    // --- SaveAsync Create ---

    [Fact]
    public async Task SaveAsync_Create_RoundTripsLoopAndScrapeBlocks()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var dto = MakeSimpleTask("Task1");

        var result = await svc.SaveAsync("user1", null, dto);

        Assert.Equal(SaveTaskOutcome.Created, result.Outcome);
        Assert.Equal(2, result.Task!.Blocks.Count);

        var loop = result.Task.Blocks.Single(b => b.BlockType == BlockType.Loop);
        Assert.Equal("Terms", loop.Loop!.Name);
        Assert.Equal(new List<string> { "a", "b" }, loop.Loop.Values);

        var scrape = result.Task.Blocks.Single(b => b.BlockType == BlockType.Scrape);
        Assert.NotNull(scrape.Scrape);
    }

    [Fact]
    public async Task SaveAsync_Update_ReplacesBlocks()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);

        var created = await svc.SaveAsync("user1", null, MakeSimpleTask("Original"));
        var taskId = created.Task!.Id;

        var updateDto = new SaveTaskDto
        {
            Name = "Updated",
            Blocks = new List<TaskBlockTreeDto>
            {
                new() { Id = Guid.NewGuid(), BlockType = BlockType.Loop, OrderIndex = 0, Loop = new LoopBlockConfigDto { Name = "NewLoop", Values = new() } },
            },
        };
        var updated = await svc.SaveAsync("user1", taskId, updateDto);

        Assert.Equal(SaveTaskOutcome.Updated, updated.Outcome);
        Assert.Equal("Updated", updated.Task!.Name);
        Assert.Single(updated.Task.Blocks);
        Assert.Equal("NewLoop", updated.Task.Blocks[0].Loop!.Name);
    }

    [Fact]
    public async Task SaveAsync_WithValidatorErrors_ReturnsValidationFailed()
    {
        using var db = TestDb.CreateInMemory();
        var validatorMock = new Mock<ITaskValidator>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<SaveTaskDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationErrorDto> { new() { Code = ValidationCodes.MissingTaskName } });
        var svc = new TaskService(db, validatorMock.Object);

        var result = await svc.SaveAsync("user1", null, new SaveTaskDto { Name = "" });

        Assert.Equal(SaveTaskOutcome.ValidationFailed, result.Outcome);
        Assert.Null(result.Task);
        Assert.Contains(result.Errors, e => e.Code == ValidationCodes.MissingTaskName);
    }

    [Fact]
    public async Task SaveAsync_Update_NonOwner_ReturnsForbidden()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var created = await svc.SaveAsync("user1", null, MakeSimpleTask());

        var result = await svc.SaveAsync("user2", created.Task!.Id, new SaveTaskDto { Name = "Attempt" });

        Assert.Equal(SaveTaskOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task DeleteAsync_CascadeRemovesBlocks()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var created = await svc.SaveAsync("user1", null, MakeSimpleTask());
        var taskId = created.Task!.Id;

        var outcome = await svc.DeleteAsync("user1", taskId);

        Assert.Equal(DeleteTaskOutcome.Deleted, outcome);
        Assert.Equal(0, await db.Set<TaskEntity>().CountAsync());
        Assert.Equal(0, await db.Set<BBWM.WebScraper.Entities.TaskBlock>().CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_NonOwner_ReturnsForbidden()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var created = await svc.SaveAsync("user1", null, MakeSimpleTask());

        var outcome = await svc.DeleteAsync("user2", created.Task!.Id);

        Assert.Equal(DeleteTaskOutcome.Forbidden, outcome);
    }

    [Fact]
    public async Task GetAsync_ExcludesOtherUsersTasks()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var created = await svc.SaveAsync("user1", null, MakeSimpleTask());

        var result = await svc.GetAsync("user2", created.Task!.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_ExcludesOtherUsersTasks_OrderedDescByCreatedAt()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        await svc.SaveAsync("user1", null, MakeSimpleTask("A"));
        await svc.SaveAsync("user1", null, MakeSimpleTask("B"));
        await svc.SaveAsync("user2", null, MakeSimpleTask("C"));

        var list = await svc.ListAsync("user1");

        Assert.Equal(2, list.Count);
        Assert.DoesNotContain(list, t => t.Name == "C");
        // Most recently created should appear first
        Assert.True(list[0].CreatedAt >= list[1].CreatedAt);
    }

    [Fact]
    public async Task SaveAsync_SearchTerms_IsUnionOfLoopValues()
    {
        using var db = TestDb.CreateInMemory();
        var (svc, _) = CreateService(db);
        var dto = new SaveTaskDto
        {
            Name = "T",
            Blocks = new List<TaskBlockTreeDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    BlockType = BlockType.Loop,
                    OrderIndex = 0,
                    Loop = new LoopBlockConfigDto { Name = "L1", Values = new List<string> { "x", "y" } },
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    BlockType = BlockType.Loop,
                    OrderIndex = 1,
                    Loop = new LoopBlockConfigDto { Name = "L2", Values = new List<string> { "y", "z" } },
                },
            },
        };

        var result = await svc.SaveAsync("user1", null, dto);

        Assert.NotNull(result.Task);
        Assert.Contains("x", result.Task.SearchTerms);
        Assert.Contains("y", result.Task.SearchTerms);
        Assert.Contains("z", result.Task.SearchTerms);
        // Union → no duplicates
        Assert.Equal(3, result.Task.SearchTerms.Length);
    }
}
