using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class AiControlService : IAiControlService
{
    private const int DefaultSuspendSeconds = 9999;

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<AiControlService> _logger;

    public AiControlService(
        ILuaBridgeExecutor bridge,
        ILogger<AiControlService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public AiControlService(ILogger<AiControlService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    public async Task<ActionExecutionResult> ExecuteAiControlAsync(
        string profileId, AiControlRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(request);

        var actionId = ResolveAiAction(request.Action);
        var luaCommand = BuildAiLuaCommand(request);

        _logger.LogInformation(
            "AI control executing: {ActionId} for profile {Profile}",
            actionId, profileId);

        // PreventUsage is dangerous — log a crash warning regardless of bridge availability.
        if (request.Action == AiControlAction.PreventUsage)
        {
            _logger.LogWarning(
                "PreventUsage requested for unit {UnitId} -- crash risk if unit lacks AI",
                request.TargetUnitId);
        }

        if (_bridge is not null && !luaCommand.StartsWith("--", StringComparison.Ordinal))
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, actionId, cancellationToken);
        }

        // Fallback: return prepared result when no bridge is configured or
        // the Lua command is a comment (PreventUsage, SetDifficulty).
        var diagnostics = new Dictionary<string, object?>
        {
            ["lua_call"] = luaCommand,
            ["action_id"] = actionId
        };

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"AI control action '{actionId}' prepared",
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// Builds the Lua command string for an AI control action.
    /// </summary>
    internal static string BuildAiLuaCommand(AiControlRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Action switch
        {
            AiControlAction.SuspendAll => $"Suspend_AI({request.SuspendSeconds ?? DefaultSuspendSeconds})",
            AiControlAction.ResumeAll => "Suspend_AI(0)",
            AiControlAction.PreventUsage =>
                $"-- WARNING: crashes if unit has no AI\n-- unit:Prevent_AI_Usage(true)",
            AiControlAction.SetDifficulty =>
                $"-- Set difficulty for {request.FactionId ?? "unknown"}",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Action, "Unknown AI control action")
        };
    }

    internal static string ResolveAiAction(AiControlAction action)
    {
        return action switch
        {
            AiControlAction.SuspendAll => "ai_suspend_all",
            AiControlAction.ResumeAll => "ai_resume_all",
            AiControlAction.PreventUsage => "ai_prevent_usage",
            AiControlAction.SetDifficulty => "ai_set_difficulty",
            _ => throw new ArgumentOutOfRangeException(
                nameof(action), action, "Unknown AI control action")
        };
    }
}
