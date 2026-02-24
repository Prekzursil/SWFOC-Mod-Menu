using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Validation;

namespace SwfocTrainer.Core.Services;

public sealed class TrainerOrchestrator
{
    private const string PayloadKeySymbol = "symbol";
    private const string DiagnosticKeySymbol = "symbol";
    private const string DiagnosticKeyFrozen = "frozen";

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

        // Mode detection is best-effort (often Unknown). Only enforce the mode gate when
        // the runtime adapter could actually infer a specific mode.
        if (runtimeMode != RuntimeMode.Unknown && action.Mode != RuntimeMode.Unknown && action.Mode != runtimeMode)
        {
            return new ActionExecutionResult(false, $"Action '{actionId}' not allowed for runtime mode {runtimeMode}", AddressSource.None);
        }

        var validation = ActionPayloadValidator.Validate(action.PayloadSchema, payload);
        if (!validation.IsValid)
        {
            return new ActionExecutionResult(false, validation.Message, AddressSource.None);
        }

        ActionExecutionResult result;

        // Freeze actions are handled directly by the orchestrator to avoid a circular
        // dependency between IRuntimeAdapter and IValueFreezeService.
        if (action.ExecutionKind == ExecutionKind.Freeze)
        {
            result = ExecuteFreezeAction(action, payload);
        }
        else
        {
            var request = new ActionExecutionRequest(action, payload, profileId, runtimeMode, context);
            result = await _runtime.ExecuteAsync(request, cancellationToken);
        }

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

    private ActionExecutionResult ExecuteFreezeAction(ActionSpec action, System.Text.Json.Nodes.JsonObject payload)
    {
        var symbol = payload[PayloadKeySymbol]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return new ActionExecutionResult(false, $"Freeze action requires '{PayloadKeySymbol}' in payload.", AddressSource.None);
        }

        // Determine freeze vs unfreeze
        var freeze = payload["freeze"]?.GetValue<bool>()
            ?? !action.Id.Equals("unfreeze_symbol", StringComparison.OrdinalIgnoreCase);

        if (!freeze)
        {
            var removed = _freezeService.Unfreeze(symbol);
            return new ActionExecutionResult(true,
                removed ? $"Unfroze symbol '{symbol}'." : $"Symbol '{symbol}' was not frozen.",
                AddressSource.None,
                new Dictionary<string, object?> { [DiagnosticKeySymbol] = symbol, [DiagnosticKeyFrozen] = false });
        }

        if (payload["intValue"] is not null)
        {
            var value = payload["intValue"]!.GetValue<int>();
            _freezeService.FreezeInt(symbol, value);
            return new ActionExecutionResult(true, $"Froze '{symbol}' to int {value}.",
                AddressSource.None,
                new Dictionary<string, object?> { [DiagnosticKeySymbol] = symbol, [DiagnosticKeyFrozen] = true, ["value"] = value });
        }

        if (payload["floatValue"] is not null)
        {
            float value;
            try { value = payload["floatValue"]!.GetValue<float>(); }
            catch (InvalidOperationException) { value = (float)payload["floatValue"]!.GetValue<double>(); }
            _freezeService.FreezeFloat(symbol, value);
            return new ActionExecutionResult(true, $"Froze '{symbol}' to float {value}.",
                AddressSource.None,
                new Dictionary<string, object?> { [DiagnosticKeySymbol] = symbol, [DiagnosticKeyFrozen] = true, ["value"] = value });
        }

        if (payload["boolValue"] is not null)
        {
            var value = payload["boolValue"]!.GetValue<bool>();
            _freezeService.FreezeBool(symbol, value);
            return new ActionExecutionResult(true, $"Froze '{symbol}' to bool {value}.",
                AddressSource.None,
                new Dictionary<string, object?> { [DiagnosticKeySymbol] = symbol, [DiagnosticKeyFrozen] = true, ["value"] = value });
        }

        return new ActionExecutionResult(false,
            "Freeze action requires one of: intValue, floatValue, or boolValue when freeze=true.",
            AddressSource.None);
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
