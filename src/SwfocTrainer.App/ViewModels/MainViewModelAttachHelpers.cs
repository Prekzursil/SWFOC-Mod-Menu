using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelAttachHelpers
{
    internal static string BuildAttachProcessHintSummary(IReadOnlyList<ProcessMetadata> processes, string unknownValue)
    {
        var summary = string.Join(", ", processes
            .Take(3)
            .Select(process => BuildAttachProcessHintSegment(process, unknownValue)));
        var more = processes.Count > 3 ? $", +{processes.Count - 3} more" : string.Empty;
        return $"Detected game processes: {summary}{more}";
    }

    internal static string? ResolveFallbackProfileRecommendation(IReadOnlyList<ProcessMetadata> processes, string baseSwfocProfileId)
    {
        if (HasSteamModId(processes, "3447786229"))
        {
            return "roe_3447786229_swfoc";
        }

        if (HasSteamModId(processes, "1397421866"))
        {
            return "aotr_1397421866_swfoc";
        }

        if (processes.Any(x => x.ExeTarget == ExeTarget.Swfoc) || processes.Any(IsStarWarsGProcess))
        {
            return baseSwfocProfileId;
        }

        return processes.Any(x => x.ExeTarget == ExeTarget.Sweaw)
            ? "base_sweaw"
            : null;
    }

    internal static string BuildAttachStartStatus(string effectiveProfileId, ProfileVariantResolution? variant)
    {
        return variant is null
            ? $"Attaching using profile '{effectiveProfileId}'..."
            : $"Attaching using universal profile -> '{effectiveProfileId}' ({variant.ReasonCode}, conf={variant.Confidence:0.00})...";
    }

    internal static bool IsActionAvailableForCurrentSession(
        string actionId,
        ActionSpec spec,
        AttachSession session,
        IReadOnlyDictionary<string, string> defaultSymbolByActionId,
        out string? unavailableReason)
    {
        unavailableReason = ResolveActionUnavailableReason(actionId, spec, session, defaultSymbolByActionId);
        return string.IsNullOrWhiteSpace(unavailableReason);
    }

    internal static bool IsStarWarsGProcess(ProcessMetadata process)
    {
        if (process.ProcessName.Equals("StarWarsG", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("StarWarsG.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("isStarWarsG", out var raw) &&
            bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return process.ProcessPath.Contains("StarWarsG.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAttachProcessHintSegment(ProcessMetadata process, string unknownValue)
    {
        var launchContext = process.LaunchContext;
        var cmd = MainViewModelDiagnostics.ReadProcessMetadata(process, "commandLineAvailable", "False");
        var mods = MainViewModelDiagnostics.ReadProcessMetadata(process, "steamModIdsDetected", string.Empty);
        var via = MainViewModelDiagnostics.ReadProcessMetadata(process, "detectedVia", unknownValue);
        var launch = launchContext?.LaunchKind.ToString() ?? "n/a";
        var recommended = launchContext?.Recommendation.ProfileId ?? string.Empty;
        var reason = launchContext?.Recommendation.ReasonCode ?? unknownValue;
        var confidence = launchContext is null
            ? "0.00"
            : launchContext.Recommendation.Confidence.ToString("0.00");
        return $"{process.ProcessName}:{process.ProcessId}:{process.ExeTarget}:cmd={cmd}:mods={mods}:launch={launch}:rec={recommended}:{reason}:{confidence}:via={via}";
    }

    private static bool HasSteamModId(IEnumerable<ProcessMetadata> processes, string workshopId)
    {
        return processes.Any(process => ProcessHasSteamModId(process, workshopId));
    }

    private static bool ProcessHasSteamModId(ProcessMetadata process, string workshopId)
    {
        return HasLaunchContextModId(process, workshopId) ||
               HasCommandLineModId(process, workshopId) ||
               HasMetadataModId(process, workshopId);
    }

    private static bool HasLaunchContextModId(ProcessMetadata process, string workshopId)
    {
        return process.LaunchContext is not null &&
               process.LaunchContext.SteamModIds.Any(id => id.Equals(workshopId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCommandLineModId(ProcessMetadata process, string workshopId)
    {
        return process.CommandLine?.Contains(workshopId, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool HasMetadataModId(ProcessMetadata process, string workshopId)
    {
        var ids = MainViewModelDiagnostics.ReadProcessMetadata(process, "steamModIdsDetected", string.Empty);
        if (string.IsNullOrWhiteSpace(ids))
        {
            return false;
        }

        var split = ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return split.Any(id => id.Equals(workshopId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveActionUnavailableReason(
        string actionId,
        ActionSpec spec,
        AttachSession session,
        IReadOnlyDictionary<string, string> defaultSymbolByActionId)
    {
        if (IsDependencyDisabledAction(actionId, session))
        {
            return "action is disabled by dependency validation for this attachment.";
        }

        var requiredSymbol = ResolveRequiredSymbolForSessionGate(actionId, spec, defaultSymbolByActionId);
        if (string.IsNullOrWhiteSpace(requiredSymbol))
        {
            return null;
        }

        if (!session.Symbols.TryGetValue(requiredSymbol, out var symbolInfo) ||
            symbolInfo is null ||
            symbolInfo.Address == nint.Zero ||
            symbolInfo.HealthStatus == SymbolHealthStatus.Unresolved)
        {
            return $"required symbol '{requiredSymbol}' is unresolved for this attachment.";
        }

        return null;
    }

    private static bool IsDependencyDisabledAction(string actionId, AttachSession session)
    {
        if (session.Process.Metadata is null ||
            !session.Process.Metadata.TryGetValue("dependencyDisabledActions", out var disabledIdsRaw) ||
            string.IsNullOrWhiteSpace(disabledIdsRaw))
        {
            return false;
        }

        var disabledIds = disabledIdsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return disabledIds.Any(x => x.Equals(actionId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveRequiredSymbolForSessionGate(
        string actionId,
        ActionSpec spec,
        IReadOnlyDictionary<string, string> defaultSymbolByActionId)
    {
        if (spec.ExecutionKind is not (ExecutionKind.Memory or ExecutionKind.CodePatch or ExecutionKind.Freeze or ExecutionKind.Sdk))
        {
            return null;
        }

        if (!spec.PayloadSchema.TryGetPropertyValue("required", out var requiredNode) || requiredNode is not JsonArray required)
        {
            return null;
        }

        var requiresSymbol = required.Any(x => string.Equals(x?.GetValue<string>(), MainViewModelDefaults.PayloadKeySymbol, StringComparison.OrdinalIgnoreCase));
        if (!requiresSymbol)
        {
            return null;
        }

        return defaultSymbolByActionId.TryGetValue(actionId, out var symbol) && !string.IsNullOrWhiteSpace(symbol)
            ? symbol
            : null;
    }
}
