using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

internal static class NamedPipeExtenderBackendContextHelpers
{
    internal static Dictionary<string, BackendCapability> ParseCapabilities(
        IReadOnlyDictionary<string, object?>? diagnostics,
        IEnumerable<string> nativeAuthoritativeFeatureIds)
    {
        var capabilities = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetCapabilitiesElement(diagnostics, out var element))
        {
            EnsureNativeFeatureEntries(capabilities, nativeAuthoritativeFeatureIds);
            return capabilities;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!TryParseCapability(property, out var capability))
            {
                continue;
            }

            capabilities[property.Name] = capability;
        }

        EnsureNativeFeatureEntries(capabilities, nativeAuthoritativeFeatureIds);
        return capabilities;
    }

    internal static int ReadContextInt(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (!TryReadContextValue(context, key, out var raw) || raw is null)
        {
            return 0;
        }

        if (raw is int intValue)
        {
            return intValue;
        }

        if (raw is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
        {
            return (int)longValue;
        }

        return int.TryParse(raw.ToString(), out var parsed) ? parsed : 0;
    }

    internal static string ReadContextString(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (!TryReadContextValue(context, key, out var raw) || raw is null)
        {
            return string.Empty;
        }

        return raw as string ?? raw.ToString() ?? string.Empty;
    }

    internal static JsonObject ReadContextAnchors(IReadOnlyDictionary<string, object?>? context)
    {
        var resolved = new JsonObject();
        if (TryReadContextValue(context, "resolvedAnchors", out var anchorsRaw))
        {
            MergeAnchors(resolved, anchorsRaw);
        }

        if (resolved.Count == 0 && TryReadContextValue(context, "anchors", out var legacyAnchorsRaw))
        {
            MergeAnchors(resolved, legacyAnchorsRaw);
        }

        return resolved;
    }

    private static void EnsureNativeFeatureEntries(
        Dictionary<string, BackendCapability> capabilities,
        IEnumerable<string> nativeAuthoritativeFeatureIds)
    {
        foreach (var featureId in nativeAuthoritativeFeatureIds)
        {
            if (capabilities.ContainsKey(featureId))
            {
                continue;
            }

            capabilities[featureId] = new BackendCapability(
                FeatureId: featureId,
                Available: false,
                Confidence: CapabilityConfidenceState.Unknown,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Notes: "Feature omitted from capability probe payload.");
        }
    }

    private static bool TryGetCapabilitiesElement(
        IReadOnlyDictionary<string, object?>? diagnostics,
        out JsonElement capabilitiesElement)
    {
        capabilitiesElement = default;
        if (diagnostics is null || diagnostics.Count == 0)
        {
            return false;
        }

        if (!diagnostics.TryGetValue("capabilities", out var rawCapabilities) || rawCapabilities is not JsonElement element)
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        capabilitiesElement = element;
        return true;
    }

    private static bool TryParseCapability(JsonProperty property, out BackendCapability capability)
    {
        capability = null!;
        var value = property.Value;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var available = IsAvailable(value);
        var state = TryGetStringProperty(value, "state", out var rawState)
            ? ParseCapabilityConfidence(rawState)
            : CapabilityConfidenceState.Unknown;
        var reasonCode = TryGetStringProperty(value, "reasonCode", out var rawReasonCode)
            ? ParseRuntimeReasonCode(rawReasonCode)
            : RuntimeReasonCode.CAPABILITY_UNKNOWN;

        capability = new BackendCapability(
            FeatureId: property.Name,
            Available: available,
            Confidence: state,
            ReasonCode: reasonCode,
            Notes: "Feature returned by extender capability probe.");
        return true;
    }

    private static bool IsAvailable(JsonElement value)
    {
        return value.TryGetProperty("available", out var availableElement) &&
               availableElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               availableElement.GetBoolean();
    }

    private static bool TryGetStringProperty(JsonElement value, string propertyName, out string? propertyValue)
    {
        propertyValue = null;
        if (!value.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        propertyValue = propertyElement.GetString();
        return true;
    }

    private static CapabilityConfidenceState ParseCapabilityConfidence(string? rawState)
    {
        if (string.Equals(rawState, "Verified", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityConfidenceState.Verified;
        }

        if (string.Equals(rawState, "Experimental", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityConfidenceState.Experimental;
        }

        return CapabilityConfidenceState.Unknown;
    }

    private static RuntimeReasonCode ParseRuntimeReasonCode(string? rawCode)
    {
        return Enum.TryParse<RuntimeReasonCode>(rawCode, ignoreCase: true, out var parsed)
            ? parsed
            : RuntimeReasonCode.CAPABILITY_UNKNOWN;
    }

    private static void MergeAnchors(JsonObject destination, object? rawAnchors)
    {
        if (rawAnchors is null)
        {
            return;
        }

        if (TryMergeJsonObjectAnchors(destination, rawAnchors) ||
            TryMergeJsonElementAnchors(destination, rawAnchors) ||
            TryMergeObjectDictionaryAnchors(destination, rawAnchors) ||
            TryMergeStringPairAnchors(destination, rawAnchors))
        {
            return;
        }

        TryMergeSerializedAnchors(destination, rawAnchors);
    }

    private static bool TryMergeJsonObjectAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not JsonObject jsonObject)
        {
            return false;
        }

        foreach (var kv in jsonObject)
        {
            if (kv.Value is null)
            {
                continue;
            }

            destination[kv.Key] = kv.Value.ToString();
        }

        return true;
    }

    private static bool TryMergeJsonElementAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            destination[property.Name] = property.Value.ToString();
        }

        return true;
    }

    private static bool TryMergeObjectDictionaryAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not IReadOnlyDictionary<string, object?> dictionary)
        {
            return false;
        }

        foreach (var kv in dictionary)
        {
            if (kv.Value is null)
            {
                continue;
            }

            destination[kv.Key] = kv.Value.ToString();
        }

        return true;
    }

    private static bool TryMergeStringPairAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not IEnumerable<KeyValuePair<string, string>> stringPairs)
        {
            return false;
        }

        foreach (var kv in stringPairs)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            destination[kv.Key] = kv.Value;
        }

        return true;
    }

    private static void TryMergeSerializedAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not string serialized || string.IsNullOrWhiteSpace(serialized))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
            if (parsed is null)
            {
                return;
            }

            foreach (var kv in parsed)
            {
                if (string.IsNullOrWhiteSpace(kv.Value))
                {
                    continue;
                }

                destination[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool TryReadContextValue(
        IReadOnlyDictionary<string, object?>? context,
        string key,
        out object? value)
    {
        value = null;
        if (context is null)
        {
            return false;
        }

        if (!context.TryGetValue(key, out var raw))
        {
            return false;
        }

        value = raw;
        return true;
    }
}
