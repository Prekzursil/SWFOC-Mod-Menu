using System.Xml.Linq;
using System.Text.Json;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;

namespace SwfocTrainer.Flow.Services;

public sealed class StoryPlotFlowExtractor
{
    private static readonly string[] TacticalFeatureIds =
    [
        "freeze_timer",
        "toggle_fog_reveal",
        "toggle_ai",
        "set_unit_cap",
        "toggle_instant_build_patch"
    ];

    private static readonly string[] GalacticFeatureIds =
    [
        "set_credits",
        "toggle_ai"
    ];

    private readonly string[] _scriptAttributeCandidates =
    [
        "Script",
        "LuaScript",
        "EventScript",
        "StoryScript",
        "ScriptName"
    ];

    private readonly HashSet<string> _ignoredEventAttributeNames =
    [
        "Name"
    ];

    public FlowIndexReport Extract(string xmlContent, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return FlowIndexReport.Empty;
        }

        var diagnostics = new List<string>();
        XDocument document;
        try
        {
            document = XDocument.Parse(xmlContent, LoadOptions.None);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Invalid XML '{sourceFile}': {ex.Message}");
            return new FlowIndexReport(Array.Empty<FlowPlotRecord>(), diagnostics);
        }

        var plotElements = document
            .Descendants()
            .Where(node => node.Name.LocalName.Equals("Plot", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var plots = new List<FlowPlotRecord>();
        if (plotElements.Length == 0)
        {
            var syntheticPlotId = Path.GetFileNameWithoutExtension(sourceFile);
            var syntheticEvents = ExtractEvents(document.Root, sourceFile);
            plots.Add(new FlowPlotRecord(syntheticPlotId, sourceFile, syntheticEvents));
            return new FlowIndexReport(plots, diagnostics);
        }

        foreach (var plotElement in plotElements)
        {
            var plotId = ReadFirstNonEmptyAttribute(plotElement, "Name", "Id", "ID")
                ?? Path.GetFileNameWithoutExtension(sourceFile);

            var events = ExtractEvents(plotElement, sourceFile);
            plots.Add(new FlowPlotRecord(plotId, sourceFile, events));
        }

        return new FlowIndexReport(plots, diagnostics);
    }

    public FlowCapabilityLinkReport BuildCapabilityLinkReport(
        FlowIndexReport flowReport,
        MegaFilesIndex megaFilesIndex,
        string symbolPackJson)
    {
        if (flowReport.Plots.Count == 0)
        {
            return FlowCapabilityLinkReport.Empty;
        }

        if (!TryParseCapabilities(symbolPackJson, out var capabilities, out var diagnostics))
        {
            return new FlowCapabilityLinkReport(Array.Empty<FlowCapabilityLinkRecord>(), diagnostics);
        }

        var links = new List<FlowCapabilityLinkRecord>();
        var fallbackSource = megaFilesIndex.GetEnabledFilesInLoadOrder().FirstOrDefault()?.FileName ?? "unknown";
        foreach (var plot in flowReport.Plots.OrderBy(x => x.SourceFile, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.PlotId, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var flowEvent in plot.Events.OrderBy(x => x.EventName, StringComparer.OrdinalIgnoreCase))
            {
                var featureIds = ResolveFeatureIds(flowEvent.ModeHint);
                foreach (var featureId in featureIds)
                {
                    var capability = ResolveCapability(capabilities, featureId);
                    links.Add(new FlowCapabilityLinkRecord(
                        MegaFileSource: fallbackSource,
                        PlotId: plot.PlotId,
                        EventName: flowEvent.EventName,
                        ModeHint: flowEvent.ModeHint,
                        FeatureId: featureId,
                        Available: capability.Available,
                        State: capability.State,
                        ReasonCode: capability.ReasonCode));
                }
            }
        }

        return new FlowCapabilityLinkReport(links, diagnostics);
    }

    private IReadOnlyList<FlowEventRecord> ExtractEvents(XElement? root, string sourceFile)
    {
        if (root is null)
        {
            return Array.Empty<FlowEventRecord>();
        }

        var events = new List<FlowEventRecord>();
        foreach (var node in root.Descendants())
        {
            var eventName = node.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(eventName) ||
                !eventName.StartsWith("STORY_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var attributes = node
                .Attributes()
                .Where(attribute => !_ignoredEventAttributeNames.Contains(attribute.Name.LocalName))
                .ToDictionary(
                    attribute => attribute.Name.LocalName,
                    attribute => attribute.Value,
                    StringComparer.OrdinalIgnoreCase);

            var scriptReference = ReadFirstNonEmptyAttribute(node, _scriptAttributeCandidates);
            var modeHint = ResolveModeHint(eventName);
            events.Add(new FlowEventRecord(eventName, modeHint, sourceFile, scriptReference, attributes));
        }

        return events;
    }

    private static string? ReadFirstNonEmptyAttribute(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = element.Attribute(name)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static FlowModeHint ResolveModeHint(string eventName)
    {
        if (eventName.Contains("SPACE_TACTICAL", StringComparison.OrdinalIgnoreCase))
        {
            return FlowModeHint.TacticalSpace;
        }

        if (eventName.Contains("LAND_TACTICAL", StringComparison.OrdinalIgnoreCase))
        {
            return FlowModeHint.TacticalLand;
        }

        if (eventName.Contains("GALACTIC", StringComparison.OrdinalIgnoreCase))
        {
            return FlowModeHint.Galactic;
        }

        return FlowModeHint.Unknown;
    }

    private static IReadOnlyList<string> ResolveFeatureIds(FlowModeHint modeHint)
    {
        return modeHint switch
        {
            FlowModeHint.TacticalLand => TacticalFeatureIds,
            FlowModeHint.TacticalSpace => TacticalFeatureIds,
            FlowModeHint.Galactic => GalacticFeatureIds,
            _ => Array.Empty<string>()
        };
    }

    private static CapabilitySnapshot ResolveCapability(
        IReadOnlyDictionary<string, CapabilitySnapshot> capabilities,
        string featureId)
    {
        if (capabilities.TryGetValue(featureId, out var capability))
        {
            return capability;
        }

        return new CapabilitySnapshot(
            Available: false,
            State: "Unavailable",
            ReasonCode: "CAPABILITY_REQUIRED_MISSING");
    }

    private static bool TryParseCapabilities(
        string symbolPackJson,
        out Dictionary<string, CapabilitySnapshot> capabilities,
        out List<string> diagnostics)
    {
        diagnostics = new List<string>();
        capabilities = new Dictionary<string, CapabilitySnapshot>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(symbolPackJson))
        {
            diagnostics.Add("symbol-pack payload is empty");
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SymbolPackDto>(symbolPackJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (payload?.Capabilities is null || payload.Capabilities.Count == 0)
            {
                diagnostics.Add("symbol-pack capabilities are missing");
                return false;
            }

            foreach (var capability in payload.Capabilities.Where(capability => !string.IsNullOrWhiteSpace(capability.FeatureId)))
            {
                capabilities[capability.FeatureId!] = new CapabilitySnapshot(
                    Available: capability.Available,
                    State: capability.State ?? "Unknown",
                    ReasonCode: capability.ReasonCode ?? "CAPABILITY_UNKNOWN");
            }

            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Add($"symbol-pack parse failed: {ex.Message}");
            return false;
        }
    }

    private sealed class SymbolPackDto
    {
        public List<SymbolPackCapabilityDto>? Capabilities { get; set; } = new();
    }

    private sealed class SymbolPackCapabilityDto
    {
        public string? FeatureId { get; set; } = string.Empty;

        public bool Available { get; set; }

        public string? State { get; set; } = string.Empty;

        public string? ReasonCode { get; set; } = string.Empty;
    }

    private sealed record CapabilitySnapshot(
        bool Available,
        string State,
        string ReasonCode);
}
