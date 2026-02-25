using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;

namespace SwfocTrainer.Flow.Services;

public sealed class FlowLabSnapshotBuilder
{
    private readonly StringComparer _scriptReferenceComparer = StringComparer.OrdinalIgnoreCase;

    public FlowLabSnapshot Build(FlowIndexReport flowReport, MegaFilesIndex megaFilesIndex)
    {
        ArgumentNullException.ThrowIfNull(flowReport);
        ArgumentNullException.ThrowIfNull(megaFilesIndex);

        var events = flowReport.GetAllEvents();
        var modeCounts = events
            .GroupBy(evt => evt.ModeHint)
            .OrderBy(group => group.Key)
            .Select(group => new FlowModeCount(group.Key, group.Count()))
            .ToArray();

        var scriptReferences = events
            .Select(evt => evt.ScriptReference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference!.Trim())
            .Distinct(_scriptReferenceComparer)
            .OrderBy(reference => reference, _scriptReferenceComparer)
            .ToArray();

        var megaLoadOrder = megaFilesIndex.Files
            .Where(file => file.Enabled)
            .OrderBy(file => file.LoadOrder)
            .Select(file => file.FileName)
            .ToArray();

        var diagnostics = flowReport.Diagnostics
            .Concat(megaFilesIndex.Diagnostics)
            .ToArray();

        return new FlowLabSnapshot(modeCounts, scriptReferences, megaLoadOrder, diagnostics);
    }
}
