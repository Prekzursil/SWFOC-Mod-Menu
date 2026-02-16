using System.Globalization;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Runtime.Services;

public sealed class ExperimentalSdkRuntimeAdapter : ISdkRuntimeAdapter
{
    public Task<SdkCommandResult> ExecuteAsync(
        SdkCommandRequest request,
        AttachSession session,
        SdkOperationCapability capability,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var memory = new ProcessMemoryAccessor(session.Process.ProcessId);
            var result = request.OperationId switch
            {
                SdkOperationId.ListSelected => ExecuteListSelected(session, memory),
                SdkOperationId.ListNearby => ExecuteListNearby(session, memory),
                SdkOperationId.Spawn => Unsupported(request.OperationId, "spawn_not_implemented"),
                SdkOperationId.Kill => ExecuteKill(session, request.Payload, memory),
                SdkOperationId.SetOwner => ExecuteSetOwner(session, request.Payload, memory),
                SdkOperationId.Teleport => Unsupported(request.OperationId, "teleport_not_implemented"),
                SdkOperationId.SetPlanetOwner => ExecuteSetPlanetOwner(session, request.Payload, memory),
                SdkOperationId.SetHp => ExecuteSetFloatSymbol(session, request.Payload, memory, "selected_hp", request.OperationId),
                SdkOperationId.SetShield => ExecuteSetFloatSymbol(session, request.Payload, memory, "selected_shield", request.OperationId),
                SdkOperationId.SetCooldown => ExecuteSetFloatSymbol(session, request.Payload, memory, "selected_cooldown_multiplier", request.OperationId),
                _ => Unsupported(request.OperationId, "operation_not_supported")
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SdkCommandResult(
                false,
                request.OperationId,
                $"sdk_execution_exception: {ex.Message}",
                new Dictionary<string, object?>
                {
                    ["reasonCode"] = "sdk_execution_exception",
                    ["exceptionType"] = ex.GetType().Name
                }));
        }
    }

    private static SdkCommandResult ExecuteListSelected(AttachSession session, ProcessMemoryAccessor memory)
    {
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        diagnostics["selected_hp"] = TryReadFloat(session, memory, "selected_hp");
        diagnostics["selected_shield"] = TryReadFloat(session, memory, "selected_shield");
        diagnostics["selected_speed"] = TryReadFloat(session, memory, "selected_speed");
        diagnostics["selected_owner_faction"] = TryReadInt(session, memory, "selected_owner_faction");

        return new SdkCommandResult(
            true,
            SdkOperationId.ListSelected,
            "sdk_list_selected_collected",
            diagnostics);
    }

    private static SdkCommandResult ExecuteListNearby(AttachSession session, ProcessMemoryAccessor memory)
    {
        var baseResult = ExecuteListSelected(session, memory);
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (baseResult.Diagnostics is not null)
        {
            foreach (var kv in baseResult.Diagnostics)
            {
                diagnostics[kv.Key] = kv.Value;
            }
        }

        diagnostics["nearbyCount"] = 1;
        diagnostics["reasonCode"] = "nearby_enumeration_placeholder";

        return new SdkCommandResult(
            true,
            SdkOperationId.ListNearby,
            "sdk_list_nearby_placeholder",
            diagnostics);
    }

    private static SdkCommandResult ExecuteKill(AttachSession session, JsonObject payload, ProcessMemoryAccessor memory)
    {
        var hpValue = ResolveFloat(payload, "hpValue", fallback: 0f);
        if (!TryGetSymbol(session, "selected_hp", out var hpSymbol))
        {
            return Unsupported(SdkOperationId.Kill, "target_not_found");
        }

        memory.Write(hpSymbol.Address, hpValue);
        if (TryGetSymbol(session, "selected_shield", out var shieldSymbol))
        {
            memory.Write(shieldSymbol.Address, 0f);
        }

        return new SdkCommandResult(
            true,
            SdkOperationId.Kill,
            "sdk_kill_applied",
            new Dictionary<string, object?>
            {
                ["reasonCode"] = "ok",
                ["hpValue"] = hpValue
            });
    }

    private static SdkCommandResult ExecuteSetOwner(AttachSession session, JsonObject payload, ProcessMemoryAccessor memory)
    {
        if (!TryGetSymbol(session, "selected_owner_faction", out var symbol))
        {
            return Unsupported(SdkOperationId.SetOwner, "target_not_found");
        }

        var owner = ResolveInt(payload, "intValue", fallback: ResolveInt(payload, "ownerFaction", 0));
        memory.Write(symbol.Address, owner);

        return new SdkCommandResult(
            true,
            SdkOperationId.SetOwner,
            "sdk_set_owner_applied",
            new Dictionary<string, object?>
            {
                ["reasonCode"] = "ok",
                ["ownerFaction"] = owner
            });
    }

    private static SdkCommandResult ExecuteSetPlanetOwner(AttachSession session, JsonObject payload, ProcessMemoryAccessor memory)
    {
        if (!TryGetSymbol(session, "planet_owner", out var symbol))
        {
            return Unsupported(SdkOperationId.SetPlanetOwner, "target_not_found");
        }

        var owner = ResolveInt(payload, "intValue", fallback: ResolveInt(payload, "ownerFaction", 0));
        memory.Write(symbol.Address, owner);

        return new SdkCommandResult(
            true,
            SdkOperationId.SetPlanetOwner,
            "sdk_set_planet_owner_applied",
            new Dictionary<string, object?>
            {
                ["reasonCode"] = "ok",
                ["planetOwner"] = owner
            });
    }

    private static SdkCommandResult ExecuteSetFloatSymbol(
        AttachSession session,
        JsonObject payload,
        ProcessMemoryAccessor memory,
        string symbolName,
        SdkOperationId operationId)
    {
        if (!TryGetSymbol(session, symbolName, out var symbol))
        {
            return Unsupported(operationId, "target_not_found");
        }

        var value = ResolveFloat(payload, "floatValue", 0f);
        memory.Write(symbol.Address, value);
        return new SdkCommandResult(
            true,
            operationId,
            $"sdk_{operationId.ToString().ToLowerInvariant()}_applied",
            new Dictionary<string, object?>
            {
                ["reasonCode"] = "ok",
                ["symbol"] = symbolName,
                ["floatValue"] = value.ToString("0.###", CultureInfo.InvariantCulture)
            });
    }

    private static bool TryGetSymbol(AttachSession session, string symbolName, out SymbolInfo symbol)
    {
        if (session.Symbols.TryGetValue(symbolName, out var info) &&
            info is not null &&
            info.Address != nint.Zero)
        {
            symbol = info;
            return true;
        }

        symbol = default!;
        return false;
    }

    private static object TryReadFloat(AttachSession session, ProcessMemoryAccessor memory, string symbolName)
    {
        if (!TryGetSymbol(session, symbolName, out var symbol))
        {
            return "missing";
        }

        try
        {
            return memory.Read<float>(symbol.Address);
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static object TryReadInt(AttachSession session, ProcessMemoryAccessor memory, string symbolName)
    {
        if (!TryGetSymbol(session, symbolName, out var symbol))
        {
            return "missing";
        }

        try
        {
            return memory.Read<int>(symbol.Address);
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static int ResolveInt(JsonObject payload, string key, int fallback)
    {
        if (payload[key] is null)
        {
            return fallback;
        }

        try
        {
            return payload[key]!.GetValue<int>();
        }
        catch
        {
            if (int.TryParse(payload[key]!.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }

    private static float ResolveFloat(JsonObject payload, string key, float fallback)
    {
        if (payload[key] is null)
        {
            return fallback;
        }

        try
        {
            return payload[key]!.GetValue<float>();
        }
        catch
        {
            if (float.TryParse(payload[key]!.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }

    private static SdkCommandResult Unsupported(SdkOperationId operationId, string reasonCode)
    {
        return new SdkCommandResult(
            false,
            operationId,
            $"sdk_operation_unsupported: {reasonCode}",
            new Dictionary<string, object?>
            {
                ["reasonCode"] = reasonCode
            });
    }
}
