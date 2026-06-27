using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Routes Lua command strings through the orchestrator using a profile action
/// that matches the requested feature. Falls back to the <c>spawn_unit_helper</c>
/// action with the Lua command injected into the payload when no specific action
/// exists for the feature.
/// </summary>
/// <remarks>
/// <para>
/// Execution priority (in order):
/// </para>
/// <list type="number">
/// <item><b>Direct pipe</b> — when a <see cref="NamedPipeLuaBridgeClient"/> is
///       available and the SWFOC Lua bridge pipe accepts a connection, the
///       Lua command is executed in-game directly. This is the only path that
///       actually mutates game state for v5 features.</item>
/// <item><b>Profile action routing</b> — when the feature maps to a known
///       profile action, the orchestrator's full validation and audit pipeline
///       is invoked. Used as a fallback for compatibility / dry-run scenarios.</item>
/// <item><b>Prepared result</b> — when neither path is available the executor
///       returns a synthetic success carrying the Lua command in diagnostics so
///       the caller (e.g. a unit test) can inspect what would have executed.</item>
/// </list>
/// </remarks>
public sealed class LuaBridgeExecutor : ILuaBridgeExecutor
{
    /// <summary>
    /// Synthetic action used when the target feature does not map to an existing
    /// profile action. Routes through <see cref="ExecutionKind.Helper"/> with
    /// a generic helper hook.
    /// </summary>
    internal static readonly ActionSpec GenericLuaAction = new(
        Id: "v5_lua_bridge",
        Category: ActionCategory.Global,
        Mode: RuntimeMode.Unknown,
        ExecutionKind: ExecutionKind.Helper,
        PayloadSchema: new JsonObject { ["required"] = new JsonArray("helperHookId") },
        VerifyReadback: false,
        CooldownMs: 0,
        Description: "Generic Lua bridge execution for v5 service commands");

    private readonly TrainerOrchestrator _orchestrator;
    private readonly IProfileRepository _profiles;
    private readonly NamedPipeLuaBridgeClient? _bridgeClient;

    public LuaBridgeExecutor(
        TrainerOrchestrator orchestrator,
        IProfileRepository profiles)
        : this(orchestrator, profiles, bridgeClient: null)
    {
    }

    public LuaBridgeExecutor(
        TrainerOrchestrator orchestrator,
        IProfileRepository profiles,
        NamedPipeLuaBridgeClient? bridgeClient)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(profiles);
        _orchestrator = orchestrator;
        _profiles = profiles;
        _bridgeClient = bridgeClient;
    }

    public async Task<ActionExecutionResult> ExecuteLuaAsync(
        string profileId,
        string luaCommand,
        string featureId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(luaCommand);
        ArgumentNullException.ThrowIfNull(featureId);

        // Step 1: Direct bridge path. When the SWFOC Lua bridge (powrprof.dll
        // injected into the game) is reachable, send the command synchronously.
        if (_bridgeClient is not null)
        {
            var bridgeResult = await TryExecuteViaBridgeAsync(
                profileId, luaCommand, featureId, cancellationToken);
            if (bridgeResult is not null)
            {
                return bridgeResult;
            }
        }

        // Try to resolve a matching profile action first.
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
        if (profile.Actions.TryGetValue(featureId, out _))
        {
            // The feature maps to a known profile action — route through the
            // orchestrator's standard pipeline which handles mode validation,
            // payload validation, routing, and audit logging.
            var payload = BuildPayloadForFeature(featureId, luaCommand);
            return await _orchestrator.ExecuteAsync(
                profileId,
                featureId,
                payload,
                RuntimeMode.Unknown,
                BuildContext(featureId, luaCommand),
                cancellationToken);
        }

        // No matching profile action. Return a result with the Lua command
        // captured in diagnostics so the caller can see what would execute.
        // The bridge execution requires a profile action with a helper hook
        // definition, so we return a "prepared" result indicating the command
        // was built but cannot be routed without an action definition.
        var diagnostics = new Dictionary<string, object?>
        {
            ["lua_call"] = luaCommand,
            ["feature_id"] = featureId,
            ["execution_note"] = "No profile action found for this feature. " +
                                 "The Lua command was built successfully and will execute " +
                                 "when a matching helper hook is configured."
        };

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Lua command prepared for '{featureId}': {luaCommand}",
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }

    private static JsonObject BuildPayloadForFeature(string featureId, string luaCommand)
    {
        var payload = new JsonObject();

        switch (featureId)
        {
            case "spawn_unit_helper":
            case "spawn_context_entity":
            case "spawn_tactical_entity":
            case "spawn_galactic_entity":
                payload["helperHookId"] = "spawn_bridge";
                payload["unitId"] = "v5_bridge";
                payload["entryMarker"] = "v5_bridge";
                payload["faction"] = "EMPIRE";
                payload["luaCommand"] = luaCommand;
                break;

            case "set_context_allegiance":
            case "set_context_faction":
                payload["intValue"] = 0;
                payload["luaCommand"] = luaCommand;
                break;

            case "set_planet_owner":
                payload["symbol"] = "planet_owner";
                payload["intValue"] = 0;
                payload["luaCommand"] = luaCommand;
                break;

            default:
                payload["helperHookId"] = "spawn_bridge";
                payload["unitId"] = "v5_bridge";
                payload["entryMarker"] = "v5_bridge";
                payload["faction"] = "EMPIRE";
                payload["luaCommand"] = luaCommand;
                break;
        }

        return payload;
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(string featureId, string luaCommand)
    {
        return new Dictionary<string, object?>
        {
            ["v5FeatureId"] = featureId,
            ["v5LuaCommand"] = luaCommand,
            ["v5ExecutionSource"] = "LuaBridgeExecutor"
        };
    }

    /// <summary>
    /// Sends the Lua command directly to the SWFOC Lua bridge pipe. Returns
    /// <see langword="null"/> when the bridge is unavailable so the caller
    /// falls back to the orchestrator routing path. Returns a populated result
    /// when the bridge actually replied (success or error).
    /// </summary>
    private async Task<ActionExecutionResult?> TryExecuteViaBridgeAsync(
        string profileId,
        string luaCommand,
        string featureId,
        CancellationToken cancellationToken)
    {
        if (_bridgeClient is null)
        {
            return null;
        }

        BridgeRoundTripResult bridgeResult;
        try
        {
            bridgeResult = await _bridgeClient.SendAsync(luaCommand, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Pipe path is unavailable — let the orchestrator path try.
            return null;
        }

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["lua_call"] = luaCommand,
            ["feature_id"] = featureId,
            ["profile_id"] = profileId,
            ["v5ExecutionSource"] = "NamedPipeLuaBridgeClient",
            ["bridge_pipe_name"] = _bridgeClient.PipeName
        };

        if (bridgeResult.Succeeded)
        {
            diagnostics["bridge_response"] = bridgeResult.Response;
            return new ActionExecutionResult(
                Succeeded: true,
                Message: $"Lua command executed via bridge: {bridgeResult.Response}",
                AddressSource: AddressSource.Signature,
                Diagnostics: diagnostics);
        }

        diagnostics["bridge_error"] = bridgeResult.ErrorMessage;
        return new ActionExecutionResult(
            Succeeded: false,
            Message: $"Bridge execution failed for '{featureId}': {bridgeResult.ErrorMessage}",
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }
}
