using System.Text.Json;
using AutoMapper;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
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
    public void RunItem_ResultJsonb_MapsToResult()
    {
        var mapper = TestDb.CreateMapper();
        var json = """{"count":5}""";
        var run = new RunItem
        {
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            WorkerId = Guid.NewGuid(),
            Status = RunItemStatus.Completed,
            RequestedAt = DateTimeOffset.UtcNow,
            ResultJsonb = JsonDocument.Parse(json),
        };

        var dto = mapper.Map<RunItemDto>(run);

        Assert.NotNull(dto.Result);
        Assert.Equal(5, dto.Result!.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public void WorkerConnection_CurrentConnection_MapsToOnline()
    {
        var mapper = TestDb.CreateMapper();

        var online = new WorkerConnection { Id = Guid.NewGuid(), UserId = "u", Name = "W", CurrentConnection = "conn-1" };
        var offline = new WorkerConnection { Id = Guid.NewGuid(), UserId = "u", Name = "W2", CurrentConnection = null };

        var onlineDto = mapper.Map<WorkerDto>(online);
        var offlineDto = mapper.Map<WorkerDto>(offline);

        Assert.True(onlineDto.Online);
        Assert.False(offlineDto.Online);
    }
}
