using System.Text.Json.Serialization;

namespace BBWM.WebScraper.Enums;

// Wire format: camelCase strings ("loop"/"scrape") via JsonStringEnumConverter
// + camelCase naming policy applied at request-deserialization time.
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum BlockType
{
    Loop,
    Scrape,
}
