using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Validation;

namespace SwfocTrainer.Core.Services;

public sealed class TrainerOrchestrator
{
    private const string FreezeSymbolKey = "symbol";
    private const string FreezeToggleKey = "freeze";
    private const string IntValueKey = "intValue";
    private const string FloatValueKey = "floatValue";
    private const string BoolValueKey = "boolValue";

    private readonly IProfileRepository _profiles;
    private readonly IRuntimeAdapter _runtime;
    private readonly IValueFreezeService _freezeService;
    private readonly IAuditLogger _auditLogger;
    private readonly ITelemetrySnapshotService _telemetry;

    public TrainerOrchestrator(
        IProfileRepository profiles,
        IRuntimeAdapter runtime,
        IValueFreezeService freezeService,
        IAuditLogger auditLogger,
        ITelemetrySnapshotService telemetry)
    {
        _profiles = profiles;
        _runtime = runtime;
        _freezeService = freezeService;
        _auditLogger = auditLogger;
        _telemetry = telemetry;
    }

    public TrainerOrchestrator(
        IProfileRepository profiles,
        IRuntimeAdapter runtime,
        IValueFreezeService freezeService,
        IAuditLogger auditLogger)
        : this(profiles, runtime, freezeService, auditLogger, new TelemetrySnapshotService())
    {
    }

    /// <summary>
    /// Unfreeze all active freezes. Should be called when detaching from the process.
    /// </summary>
    public void UnfreezeAll() => _freezeService.UnfreezeAll();

    public async Task<ActionExecutionResult> ExecuteAsync(
        string profileId,
        string actionId,
        System.Text.Json.Nodes.JsonObject payload,
        RuntimeMode runtimeMode,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
        if (!profile.Actions.TryGetValue(actionId, out var action))
        {
            return new ActionExecutionResult(false, $"Action '{actionId}' not found in profile '{profileId}'", AddressSource.None);
        }

        var runtimeModeFailure = ValidateRuntimeMode(actionId, runtimeMode, action.Mode);
        if (runtimeModeFailure is not null)
        {
            return runtimeModeFailure;
        }

        var payloadValidationFailure = ValidatePayload(action, payload);
        if (payloadValidationFailure is not null)
        {
            return payloadValidationFailure;
        }

        var result = await ExecuteActionInternalAsync(profileId, action, payload, runtimeMode, context, cancellationToken);

        var mergedDiagnostics = MergeDiagnostics(result.Diagnostics, context);
        if (!ReferenceEquals(mergedDiagnostics, result.Diagnostics))
        {
            result = result with { Diagnostics = mergedDiagnostics };
        }

        if (_runtime.CurrentSession is not null)
        {
            await _auditLogger.WriteAsync(
                new ActionAuditRecord(
                    DateTimeOffset.UtcNow,
                    profileId,
                    _runtime.CurrentSession.Process.ProcessId,
                    actionId,
                    result.AddressSource,
                    result.Succeeded,
                    result.Message,
                    mergedDiagnostics),
                cancellationToken);
        }

        _telemetry.RecordAction(actionId, result.AddressSource, result.Succeeded);
        return result;
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        string profileId,
        string actionId,
        System.Text.Json.Nodes.JsonObject payload,
        RuntimeMode runtimeMode)
    {
        return ExecuteAsync(profileId, actionId, payload, runtimeMode, null, CancellationToken.None);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        string profileId,
        string actionId,
        System.Text.Json.Nodes.JsonObject payload,
        RuntimeMode runtimeMode,
        IReadOnlyDictionary<string, object?>? context)
    {
        return ExecuteAsync(profileId, actionId, payload, runtimeMode, context, CancellationToken.None);
    }

    private static ActionExecutionResult? ValidateRuntimeMode(string actionId, RuntimeMode runtimeMode, RuntimeMode actionMode)
    {
        // Mode detection is best-effort (often Unknown). Only enforce the mode gate when
        // the runtime adapter could actually infer a specific mode.
        if (runtimeMode == RuntimeMode.Unknown || actionMode == RuntimeMode.Unknown || actionMode == runtimeMode)
        {
            return null;
        }

        return new ActionExecutionResult(false, $"Action '{actionId}' not allowed for runtime mode {runtimeMode}", AddressSource.None);
    }

    private static ActionExecutionResult? ValidatePayload(ActionSpec action, System.Text.Json.Nodes.JsonObject payload)
    {
        var validation = ActionPayloadValidator.Validate(action.PayloadSchema, payload);
        return validation.IsValid ? null : new ActionExecutionResult(false, validation.Message, AddressSource.None);
    }

    private async Task<ActionExecutionResult> ExecuteActionInternalAsync(
        string profileId,
        ActionSpec action,
        System.Text.Json.Nodes.JsonObject payload,
        RuntimeMode runtimeMode,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken cancellationToken)
    {
        // Freeze actions are handled directly by the orchestrator to avoid a circular
        // dependency between IRuntimeAdapter and IValueFreezeService.
        if (action.ExecutionKind == ExecutionKind.Freeze)
        {
            return ExecuteFreezeAction(action, payload);
        }

        var request = new ActionExecutionRequest(action, payload, profileId, runtimeMode, context);
        return await _runtime.ExecuteAsync(request, cancellationToken);
    }

    private ActionExecutionResult ExecuteFreezeAction(ActionSpec action, System.Text.Json.Nodes.JsonObject payload)
    {
        var symbol = payload[FreezeSymbolKey]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return new ActionExecutionResult(false, "Freeze action requires 'symbol' in payload.", AddressSource.None);
        }

        // Determine freeze vs unfreeze
        var freeze = payload[FreezeToggleKey]?.GetValue<bool>()
            ?? !action.Id.Equals("unfreeze_symbol", StringComparison.OrdinalIgnoreCase);

        if (!freeze)
        {
            return BuildUnfreezeResult(symbol);
        }

        var freezeSetResult = TryBuildFreezeSetResult(symbol, payload);
        if (freezeSetResult is not null)
        {
            return freezeSetResult;
        }

        return new ActionExecutionResult(false,
            "Freeze action requires one of: intValue, floatValue, or boolValue when freeze=true.",
            AddressSource.None);
    }

    private ActionExecutionResult BuildUnfreezeResult(string symbol)
    {
        var removed = _freezeService.Unfreeze(symbol);
        return new ActionExecutionResult(
            true,
            removed ? $"Unfroze symbol '{symbol}'." : $"Symbol '{symbol}' was not frozen.",
            AddressSource.None,
            BuildFreezeDiagnostics(symbol, frozen: false, value: null));
    }

    private ActionExecutionResult? TryBuildFreezeSetResult(string symbol, System.Text.Json.Nodes.JsonObject payload)
    {
        if (payload[IntValueKey] is not null)
        {
            var value = payload[IntValueKey]!.GetValue<int>();
            _freezeService.FreezeInt(symbol, value);
            return BuildFreezeResult(symbol, "int", value);
        }

        if (payload[FloatValueKey] is not null)
        {
            var value = ReadFloatFreezeValue(payload);
            _freezeService.FreezeFloat(symbol, value);
            return BuildFreezeResult(symbol, "float", value);
        }

        if (payload[BoolValueKey] is not null)
        {
            var value = payload[BoolValueKey]!.GetValue<bool>();
            _freezeService.FreezeBool(symbol, value);
            return BuildFreezeResult(symbol, "bool", value);
        }

        return null;
    }

    private static float ReadFloatFreezeValue(System.Text.Json.Nodes.JsonObject payload)
    {
        try
        {
            return payload[FloatValueKey]!.GetValue<float>();
        }
        catch (InvalidOperationException)
        {
            return (float)payload[FloatValueKey]!.GetValue<double>();
        }
    }

    private static ActionExecutionResult BuildFreezeResult(string symbol, string valueType, object value)
    {
        return new ActionExecutionResult(
            true,
            $"Froze '{symbol}' to {valueType} {value}.",
            AddressSource.None,
            BuildFreezeDiagnostics(symbol, frozen: true, value));
    }

    private static Dictionary<string, object?> BuildFreezeDiagnostics(string symbol, bool frozen, object? value)
    {
        var diagnostics = new Dictionary<string, object?>
        {
            [FreezeSymbolKey] = symbol,
            ["frozen"] = frozen
        };

        if (value is not null)
        {
            diagnostics["value"] = value;
        }

        return diagnostics;
    }

    private static IReadOnlyDictionary<string, object?>? MergeDiagnostics(
        IReadOnlyDictionary<string, object?>? diagnostics,
        IReadOnlyDictionary<string, object?>? context)
    {
        if ((diagnostics is null || diagnostics.Count == 0) &&
            (context is null || context.Count == 0))
        {
            return diagnostics;
        }

        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (diagnostics is not null)
        {
            foreach (var kv in diagnostics)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        if (context is not null)
        {
            foreach (var kv in context)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }
}
