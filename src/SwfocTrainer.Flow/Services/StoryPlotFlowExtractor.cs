using System.Xml.Linq;
using SwfocTrainer.Flow.Models;

namespace SwfocTrainer.Flow.Services;

public sealed class StoryPlotFlowExtractor
{
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
}
