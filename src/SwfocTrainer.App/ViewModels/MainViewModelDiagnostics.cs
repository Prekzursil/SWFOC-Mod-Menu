using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelDiagnostics
{
    internal static string BuildProcessDiagnosticSummary(ProcessMetadata process, string unknownValue)
    {
        var dependencySegment = BuildProcessDependencySegment(
            ReadProcessMetadata(process, "dependencyValidation", "Pass"),
            ReadProcessMetadata(process, "dependencyValidationMessage", string.Empty));

        return $"target={process.ExeTarget} | launch={BuildLaunchKindSegment(process)} | hostRole={BuildHostRoleSegment(process)} | score={BuildSelectionScoreSegment(process)} | module={BuildModuleSizeSegment(process)} | workshopMatches={BuildWorkshopMatchesSegment(process)} | cmdLine={ReadProcessMetadata(process, "commandLineAvailable", "False")} | mods={ReadProcessMods(process)} | {BuildModPathSegment(process)} | {BuildRecommendationSegment(process, unknownValue)} | {BuildResolvedVariantSegment(process)} | via={ReadProcessMetadata(process, "detectedVia", unknownValue)} | {dependencySegment} | fallbackRate={ReadProcessMetadata(process, "fallbackHitRate", "n/a")} | unresolvedRate={ReadProcessMetadata(process, "unresolvedSymbolRate", "n/a")}";
    }

    internal static string ReadProcessMetadata(ProcessMetadata process, string key, string fallback)
    {
        if (process.Metadata is null || !process.Metadata.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value;
    }

    internal static string ReadProcessMods(ProcessMetadata process)
    {
        var mods = ReadProcessMetadata(process, "steamModIdsDetected", string.Empty);
        return string.IsNullOrWhiteSpace(mods) ? "none" : mods;
    }

    internal static string BuildProcessDependencySegment(string dependencyState, string dependencyMessage)
    {
        return dependencyState.Equals("Pass", StringComparison.OrdinalIgnoreCase) ||
               string.IsNullOrWhiteSpace(dependencyMessage)
            ? $"dependency={dependencyState}"
            : $"dependency={dependencyState} ({dependencyMessage})";
    }

    internal static string FormatPatchValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Null => "null",
                _ => element.ToString()
            };
        }

        return value.ToString() ?? string.Empty;
    }

    internal static string BuildPatchMetadataSummary(SavePatchPack pack)
    {
        return $"Patch {(pack.Metadata.SchemaVersion)} | profile={pack.Metadata.ProfileId} | schema={pack.Metadata.SchemaId} | ops={pack.Operations.Count}";
    }

    internal static string BuildDependencyDiagnostic(string dependency, string dependencyMessage)
    {
        return string.IsNullOrWhiteSpace(dependencyMessage)
            ? $"dependency: {dependency}"
            : $"dependency: {dependency} ({dependencyMessage})";
    }

    internal static object ParsePrimitive(string input)
    {
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (bool.TryParse(input, out var boolValue))
        {
            return boolValue;
        }

        var trimmed = input.Trim();
        if (trimmed.EndsWith("f", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(trimmed[..^1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatValue))
        {
            return floatValue;
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        return input;
    }

    internal static string ResolveBundleGateResult(ActionReliabilityViewItem? reliability, string unknownValue)
    {
        if (reliability is null)
        {
            return unknownValue;
        }

        return reliability.State == "unavailable" ? "blocked" : "bundle_pass";
    }

    internal static string BuildDiagnosticsStatusSuffix(ActionExecutionResult result)
    {
        if (result.Diagnostics is null)
        {
            return string.Empty;
        }

        var segments = new List<string>(capacity: 5);
        AppendDiagnosticSegment(segments, result.Diagnostics, "backend", "backend", "backendRoute");
        AppendDiagnosticSegment(segments, result.Diagnostics, "routeReasonCode", "routeReasonCode", "reasonCode");
        AppendDiagnosticSegment(segments, result.Diagnostics, "capabilityProbeReasonCode", "capabilityProbeReasonCode", "probeReasonCode");
        AppendDiagnosticSegment(segments, result.Diagnostics, "hookState", "hookState");
        AppendDiagnosticSegment(segments, result.Diagnostics, "hybridExecution", "hybridExecution");

        return segments.Count == 0 ? string.Empty : $" [{string.Join(", ", segments)}]";
    }

    internal static string BuildQuickActionStatus(string actionId, ActionExecutionResult result)
    {
        var diagnosticsSuffix = BuildDiagnosticsStatusSuffix(result);
        return result.Succeeded
            ? $"✓ {actionId}: {result.Message}{diagnosticsSuffix}"
            : $"✗ {actionId}: {result.Message}{diagnosticsSuffix}";
    }

    internal static string ReadDiagnosticString(IReadOnlyDictionary<string, object?>? diagnostics, string key)
    {
        if (diagnostics is null || !diagnostics.TryGetValue(key, out var raw) || raw is null)
        {
            return string.Empty;
        }

        if (raw is string s)
        {
            return s;
        }

        return raw.ToString() ?? string.Empty;
    }

    private static void AppendDiagnosticSegment(
        ICollection<string> segments,
        IReadOnlyDictionary<string, object?> diagnostics,
        string segmentKey,
        params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            var value = TryGetDiagnosticString(diagnostics, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                segments.Add($"{segmentKey}={value}");
                return;
            }
        }
    }

    private static string? TryGetDiagnosticString(IReadOnlyDictionary<string, object?> diagnostics, string key)
    {
        if (!diagnostics.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? value.ToString();
    }

    private static string BuildLaunchKindSegment(ProcessMetadata process)
    {
        return process.LaunchContext?.LaunchKind.ToString() ?? "Unknown";
    }

    private static string BuildHostRoleSegment(ProcessMetadata process)
    {
        return process.HostRole.ToString().ToLowerInvariant();
    }

    private static string BuildSelectionScoreSegment(ProcessMetadata process)
    {
        return process.SelectionScore.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string BuildModuleSizeSegment(ProcessMetadata process)
    {
        return process.MainModuleSize > 0
            ? process.MainModuleSize.ToString(CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static string BuildWorkshopMatchesSegment(ProcessMetadata process)
    {
        return process.WorkshopMatchCount.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildModPathSegment(ProcessMetadata process)
    {
        var modPath = process.LaunchContext?.ModPathNormalized;
        return string.IsNullOrWhiteSpace(modPath) ? "modPath=none" : $"modPath={modPath}";
    }

    private static string BuildRecommendationSegment(ProcessMetadata process, string unknownValue)
    {
        var recommendation = process.LaunchContext?.Recommendation;
        var profile = recommendation?.ProfileId ?? "none";
        var reason = recommendation?.ReasonCode ?? unknownValue;
        var confidence = recommendation is null ? "0.00" : recommendation.Confidence.ToString("0.00");
        return $"rec={profile}:{reason}:{confidence}";
    }

    private static string BuildResolvedVariantSegment(ProcessMetadata process)
    {
        var resolvedVariant = ReadProcessMetadata(process, "resolvedVariant", "n/a");
        var reason = ReadProcessMetadata(process, "resolvedVariantReasonCode", "n/a");
        var confidence = ReadProcessMetadata(process, "resolvedVariantConfidence", "0.00");
        return $"variant={resolvedVariant}:{reason}:{confidence}";
    }
}
