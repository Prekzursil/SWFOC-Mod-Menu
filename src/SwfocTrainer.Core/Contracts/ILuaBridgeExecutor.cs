using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Lightweight execution contract for v5 services that need to send Lua commands
/// through the runtime bridge without taking a dependency on the full orchestrator.
/// </summary>
public interface ILuaBridgeExecutor
{
    /// <summary>
    /// Executes a Lua command string through the runtime bridge.
    /// </summary>
    /// <param name="profileId">Active profile identifier.</param>
    /// <param name="luaCommand">Lua command to execute in the game.</param>
    /// <param name="featureId">Logical feature identifier for diagnostics and routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> ExecuteLuaAsync(
        string profileId,
        string luaCommand,
        string featureId,
        CancellationToken cancellationToken);

    /// <inheritdoc cref="ExecuteLuaAsync(string, string, string, CancellationToken)"/>
    Task<ActionExecutionResult> ExecuteLuaAsync(
        string profileId,
        string luaCommand,
        string featureId)
    {
        return ExecuteLuaAsync(profileId, luaCommand, featureId, CancellationToken.None);
    }
}
