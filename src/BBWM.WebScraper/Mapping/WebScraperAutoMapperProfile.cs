using System.Text.Json;
using AutoMapper;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;

namespace BBWM.WebScraper.Mapping;

public class WebScraperAutoMapperProfile : Profile
{
    public WebScraperAutoMapperProfile()
    {
        CreateMap<ScraperConfigEntity, ScraperConfigDto>()
            .ForMember(d => d.ConfigJson, o => o.MapFrom(s => s.ConfigJson.RootElement))
            .ForMember(d => d.Shared, o => o.Ignore())
            .ForMember(d => d.LastSyncedAt, o => o.Ignore())
            .ForMember(d => d.OriginClientId, o => o.Ignore())
            .ForMember(d => d.OriginWorkerName, o => o.Ignore());

        CreateMap<CreateScraperConfigDto, ScraperConfigEntity>()
            .ForMember(d => d.ConfigJson, o => o.MapFrom(s => JsonDocument.Parse(s.ConfigJson.GetRawText(), default(JsonDocumentOptions))))
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.UserId, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore());

        CreateMap<TaskEntity, TaskDto>()
            .ForMember(d => d.ScraperConfigName, o => o.MapFrom(s => s.ScraperConfig != null ? s.ScraperConfig.Name : ""));

        CreateMap<WorkerConnection, WorkerDto>()
            .ForMember(d => d.Online, o => o.MapFrom(s => s.CurrentConnection != null));

        CreateMap<RunItem, RunItemDto>()
            .ForMember(d => d.Result, o => o.MapFrom(s => s.ResultJsonb != null ? s.ResultJsonb.RootElement : (JsonElement?)null));
    }
}
