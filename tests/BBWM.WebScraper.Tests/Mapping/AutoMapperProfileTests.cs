using System.Text.Json;
using AutoMapper;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Mapping;
using BBWM.WebScraper.Tests.TestSupport;

namespace BBWM.WebScraper.Tests.Mapping;

public class AutoMapperProfileTests
{
    [Fact]
    public void Profile_IsValid()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<WebScraperAutoMapperProfile>());
        config.AssertConfigurationIsValid();
    }

    [Fact]
    public void RunItem_ResultJsonb_NonNull_MapsToResult()
    {
        var mapper = TestDb.CreateMapper();
        var run = new RunItem
        {
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            WorkerId = Guid.NewGuid(),
            Status = RunItemStatus.Completed,
            RequestedAt = DateTimeOffset.UtcNow,
            ResultJsonb = JsonDocument.Parse("""{"key":"value"}"""),
        };

        var dto = mapper.Map<RunItemDto>(run);

        Assert.NotNull(dto.Result);
        Assert.Equal("value", dto.Result.Value.GetProperty("key").GetString());
    }

    [Fact]
    public void RunItem_ResultJsonb_Null_MapsToNullResult()
    {
        var mapper = TestDb.CreateMapper();
        var run = new RunItem
        {
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            WorkerId = Guid.NewGuid(),
            Status = RunItemStatus.Pending,
            RequestedAt = DateTimeOffset.UtcNow,
            ResultJsonb = null,
        };

        var dto = mapper.Map<RunItemDto>(run);

        Assert.Null(dto.Result);
    }

    [Fact]
    public void WorkerConnection_WithCurrentConnection_MapsOnlineTrue()
    {
        var mapper = TestDb.CreateMapper();
        var worker = new WorkerConnection
        {
            Id = Guid.NewGuid(), UserId = "user1", Name = "W",
            CurrentConnection = "conn-1",
        };

        var dto = mapper.Map<WorkerDto>(worker);

        Assert.True(dto.Online);
    }

    [Fact]
    public void WorkerConnection_WithoutCurrentConnection_MapsOnlineFalse()
    {
        var mapper = TestDb.CreateMapper();
        var worker = new WorkerConnection
        {
            Id = Guid.NewGuid(), UserId = "user1", Name = "W",
            CurrentConnection = null,
        };

        var dto = mapper.Map<WorkerDto>(worker);

        Assert.False(dto.Online);
    }
}
