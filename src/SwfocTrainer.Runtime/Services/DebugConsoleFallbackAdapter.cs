using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class DebugConsoleFallbackAdapter : IDebugConsoleFallbackAdapter
{
    public SdkFallbackResult Prepare(
        SdkOperationId operationId,
        string profileId,
        RuntimeMode runtimeMode,
        IReadOnlyDictionary<string, object?>? payload = null)
    {
        if (operationId is SdkOperationId.SetOwner or SdkOperationId.SetPlanetOwner)
        {
            var faction = ResolveFaction(payload);
            var prepared = $"SwitchControl {faction}";
            return new SdkFallbackResult(
                Supported: true,
                Mode: "debug_console_prepared",
                ReasonCode: "switchcontrol_template",
                PreparedCommand: prepared);
        }

        return new SdkFallbackResult(
            Supported: false,
            Mode: "none",
            ReasonCode: "fallback_not_supported");
    }

    private static string ResolveFaction(IReadOnlyDictionary<string, object?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return "<faction>";
        }

        if (payload.TryGetValue("faction", out var faction) && faction is not null)
        {
            return faction.ToString() ?? "<faction>";
        }

        if (payload.TryGetValue("intValue", out var intValue) && intValue is not null)
        {
            return intValue.ToString() ?? "<faction>";
        }

        if (payload.TryGetValue("ownerFaction", out var owner) && owner is not null)
        {
            return owner.ToString() ?? "<faction>";
        }

        return "<faction>";
    }
}
