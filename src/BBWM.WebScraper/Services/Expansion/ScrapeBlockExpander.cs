using System.Text.Json;
using System.Text.Json.Nodes;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;

namespace BBWM.WebScraper.Services.Expansion;

public class ScrapeBlockExpander : IBlockExpander
{
    private static readonly JsonSerializerOptions _camelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public BlockType Handles => BlockType.Scrape;

    public IEnumerable<ExpansionResult> Expand(TaskBlock block, ExpansionContext ctx, ExpansionFrame frame)
    {
        var (scraperConfigId, stepBindings) = ReadScrapeConfig(block);
        if (!ctx.ConfigsById.TryGetValue(scraperConfigId, out var config))
        {
            ctx.Warnings.Add(new ExpansionWarning(
                ExpansionWarningCodes.ConfigNotFoundAtPopulate,
                BlockId: block.Id,
                ScraperConfigId: scraperConfigId));
            yield break;
        }

        var node = JsonNode.Parse(config.ConfigJson.RootElement.GetRawText())!.AsObject();
        node["id"] = config.Id.ToString();

        var liveSetInputStepIds = new HashSet<string>();
        if (node["steps"] is JsonArray stepsArr)
        {
            foreach (var stepNode in stepsArr.OfType<JsonObject>())
            {
                if (stepNode["type"]?.GetValue<string>() != "setInput") continue;
                var stepId = stepNode["id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(stepId)) continue;
                liveSetInputStepIds.Add(stepId);

                if (!stepBindings.TryGetValue(stepId, out var binding))
                {
                    ctx.Warnings.Add(new ExpansionWarning(ExpansionWarningCodes.NewStepUnbound,
                        BlockId: block.Id, ScraperConfigId: config.Id, StepId: stepId));
                    continue;
                }

                switch (binding.Kind)
                {
                    case BindingKind.Literal:
                        if (stepNode["options"] is not JsonObject opts)
                        {
                            opts = new JsonObject();
                            stepNode["options"] = opts;
                        }
                        opts["literalValue"] = binding.Value ?? "";
                        break;

                    case BindingKind.LoopRef:
                        if (binding.Column is not null && binding.LoopBlockId.HasValue)
                        {
                            var assignmentKey = $"{binding.LoopBlockId.Value}:{binding.Column}";
                            if (frame.LoopAssignments.TryGetValue(assignmentKey, out var assignedValue))
                            {
                                if (stepNode["options"] is not JsonObject colOpts)
                                {
                                    colOpts = new JsonObject();
                                    stepNode["options"] = colOpts;
                                }
                                colOpts["literalValue"] = assignedValue;
                            }
                        }
                        else
                        {
                            // Single-column loopRef: remove stale literalValue; extension falls through to searchTerms[i].
                            if (stepNode["options"] is JsonObject loopOpts)
                                loopOpts.Remove("literalValue");
                        }
                        break;

                    case BindingKind.Unbound:
                    default:
                        ctx.Warnings.Add(new ExpansionWarning(ExpansionWarningCodes.BindingUnbound,
                            BlockId: block.Id, ScraperConfigId: config.Id, StepId: stepId));
                        break;
                }
            }
        }

        foreach (var stepId in stepBindings.Keys)
        {
            if (!liveSetInputStepIds.Contains(stepId))
                ctx.Warnings.Add(new ExpansionWarning(ExpansionWarningCodes.StepNoLongerExists,
                    BlockId: block.Id, ScraperConfigId: config.Id, StepId: stepId));
        }

        var patched = JsonSerializer.SerializeToElement(node);

        yield return new ExpansionResult(
            ScrapeBlockId: block.Id,
            ScraperConfigId: config.Id,
            ConfigName: config.Name,
            Assignments: new Dictionary<Guid, string>(),
            IterationLabel: "",
            PatchedConfigJson: patched,
            SearchTerms: new List<string>(frame.SearchTerms));
    }

    private static (Guid scraperConfigId, Dictionary<string, StepBindingDto> bindings) ReadScrapeConfig(TaskBlock block)
    {
        var root = block.ConfigJsonb.RootElement;
        var configIdStr = root.TryGetProperty("scraperConfigId", out var cid) && cid.ValueKind == JsonValueKind.String
            ? cid.GetString() ?? "" : "";
        if (!Guid.TryParse(configIdStr, out var configId))
            return (Guid.Empty, new());

        var bindings = new Dictionary<string, StepBindingDto>();
        if (root.TryGetProperty("stepBindings", out var b) && b.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in b.EnumerateObject())
            {
                // Defensive: drop payloads with no "kind" string. Without this guard, a malformed
                // payload would deserialise to BindingKind.Literal (default enum value 0), silently
                // changing semantics. This matches the pre-refactor behaviour of skipping the binding.
                if (!prop.Value.TryGetProperty("kind", out var k) || k.ValueKind != JsonValueKind.String)
                    continue;
                var binding = prop.Value.Deserialize<StepBindingDto>(_camelCaseJson);
                if (binding is null) continue;
                bindings[prop.Name] = binding;
            }
        }
        return (configId, bindings);
    }
}
