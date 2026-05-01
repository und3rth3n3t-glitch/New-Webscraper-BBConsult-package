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
            .ForMember(d => d.OriginWorkerName, o => o.Ignore());  // resolved at service layer

        CreateMap<WorkerConnection, WorkerDto>()
            .ForMember(d => d.Online, o => o.MapFrom(s => s.CurrentConnection != null));

        CreateMap<RunItem, RunItemDto>()
            .ForMember(d => d.Result, o => o.MapFrom(s => s.ResultJsonb != null ? s.ResultJsonb.RootElement : (JsonElement?)null));

        // TaskEntity → TaskDto: handled directly in TaskService.BuildDto (block tree projection
        // requires per-block ConfigJsonb deserialization, not expressible as a clean AutoMapper rule).
    }
}
