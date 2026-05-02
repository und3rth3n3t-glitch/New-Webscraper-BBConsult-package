using System.Text.Json.Serialization;

namespace BBWM.WebScraper.Enums;

// Used inside ScrapeBlockConfig.stepBindings JSONB payloads.
// Wire format: camelCase strings ("literal"/"loopRef"/"unbound") via JsonStringEnumConverter.
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum BindingKind
{
    Literal,
    LoopRef,
    Unbound,
}
