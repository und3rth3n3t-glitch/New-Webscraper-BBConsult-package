using System.Text.Json;
using System.Text.Json.Serialization;

namespace BBWM.WebScraper.Enums;

// Subclass of JsonStringEnumConverter that bakes in camelCase naming + allows ints.
// Used as [JsonConverter(typeof(CamelCaseStringEnumConverter))] on module enums so
// the wire format is "loop"/"scrape"/"literal"/"loopRef" regardless of the host's
// global JsonSerializerOptions configuration. Module-local — no host change required.
public class CamelCaseStringEnumConverter : JsonStringEnumConverter
{
    public CamelCaseStringEnumConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: true) { }
}
