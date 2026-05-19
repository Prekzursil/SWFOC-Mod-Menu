using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

// === Part 3.4 bridge helper services ===
//
// These contracts wrap the 28 SWFOC_* Lua helpers exposed by the C++ bridge
// DLL (powrprof.dll). Each implementation builds a Lua command string that
// invokes the corresponding SWFOC_* helper, then forwards it through the
// standard ILuaBridgeExecutor pipeline. All public methods return the
// canonical ActionExecutionResult envelope used by the rest of the editor.

/// <summary>
/// Toggles the SWFOC bridge's god mode hook via <c>SWFOC_GodMode</c>.
/// </summary>
public interface IGodModeService
{
    /// <summary>
    /// Enables or disables the god mode hook for the active player.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="enable">True to enable, false to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> SetGodModeAsync(
        string profileId, bool enable, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetGodModeAsync(string, bool, CancellationToken)"/>
    Task<ActionExecutionResult> SetGodModeAsync(string profileId, bool enable)
    {
        return SetGodModeAsync(profileId, enable, CancellationToken.None);
    }
}

/// <summary>
/// Toggles the SWFOC bridge's one-hit kill hook via <c>SWFOC_OneHitKill</c>.
/// </summary>
public interface IOneHitKillService
{
    /// <summary>
    /// Enables or disables the one-hit kill hook.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="enable">True to enable, false to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> SetOneHitKillAsync(
        string profileId, bool enable, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetOneHitKillAsync(string, bool, CancellationToken)"/>
    Task<ActionExecutionResult> SetOneHitKillAsync(string profileId, bool enable)
    {
        return SetOneHitKillAsync(profileId, enable, CancellationToken.None);
    }
}

/// <summary>
/// Wraps the SWFOC bridge's credit and tech helpers into a single service.
/// </summary>
public interface IEconomyService
{
    /// <summary>
    /// Sets the credits for a specific player slot.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="slot">Target player slot (0-based). Negative values route
    /// to the local player via <c>SWFOC_SetCredits</c> for backwards compat.</param>
    /// <param name="amount">Credit amount to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> SetCreditsAsync(
        string profileId, int slot, double amount, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetCreditsAsync(string, int, double, CancellationToken)"/>
    Task<ActionExecutionResult> SetCreditsAsync(string profileId, int slot, double amount)
    {
        return SetCreditsAsync(profileId, slot, amount, CancellationToken.None);
    }

    /// <summary>
    /// Reads the credits for a specific player slot.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="slot">Target player slot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> GetCreditsAsync(
        string profileId, int slot, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetCreditsAsync(string, int, CancellationToken)"/>
    Task<ActionExecutionResult> GetCreditsAsync(string profileId, int slot)
    {
        return GetCreditsAsync(profileId, slot, CancellationToken.None);
    }

    /// <summary>
    /// Drains credits from all enemy slots of the active player.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> DrainEnemyCreditsAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="DrainEnemyCreditsAsync(string, CancellationToken)"/>
    Task<ActionExecutionResult> DrainEnemyCreditsAsync(string profileId)
    {
        return DrainEnemyCreditsAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Removes the engine credit cap for the active player.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> UncapCreditsAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="UncapCreditsAsync(string, CancellationToken)"/>
    Task<ActionExecutionResult> UncapCreditsAsync(string profileId)
    {
        return UncapCreditsAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Reads the currently configured engine maximum credit cap.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> GetMaxCreditsAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetMaxCreditsAsync(string, CancellationToken)"/>
    Task<ActionExecutionResult> GetMaxCreditsAsync(string profileId)
    {
        return GetMaxCreditsAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Sets the tech level for a specific player slot.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="slot">Target player slot (0-based). Negative values route
    /// to the local player via <c>SWFOC_SetTechLevel</c>.</param>
    /// <param name="level">Target tech level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> SetTechAsync(
        string profileId, int slot, int level, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetTechAsync(string, int, int, CancellationToken)"/>
    Task<ActionExecutionResult> SetTechAsync(string profileId, int slot, int level)
    {
        return SetTechAsync(profileId, slot, level, CancellationToken.None);
    }

    /// <summary>
    /// Reads the current tech level for a specific player slot.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="slot">Target player slot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> GetTechAsync(
        string profileId, int slot, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetTechAsync(string, int, CancellationToken)"/>
    Task<ActionExecutionResult> GetTechAsync(string profileId, int slot)
    {
        return GetTechAsync(profileId, slot, CancellationToken.None);
    }
}

/// <summary>
/// Drives the FOWManager fog-of-war reveal Lua API via the bridge.
/// </summary>
public interface IMaphackService
{
    /// <summary>
    /// Reveals the entire tactical map for the local player.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> RevealAllAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="RevealAllAsync(string, CancellationToken)"/>
    Task<ActionExecutionResult> RevealAllAsync(string profileId)
    {
        return RevealAllAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Undoes a previous full-map reveal for the local player.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> UndoRevealAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="UndoRevealAsync(string, CancellationToken)"/>
    Task<ActionExecutionResult> UndoRevealAsync(string profileId)
    {
        return UndoRevealAsync(profileId, CancellationToken.None);
    }
}

/// <summary>
/// Inspects a specific unit via the SWFOC bridge's <c>SWFOC_InspectUnit</c> helper.
/// </summary>
public interface IUnitInspectorService
{
    /// <summary>
    /// Captures a snapshot of a unit at a given object address.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="objAddr">Absolute runtime address of the GameObject
    /// (CLS-compliant <see cref="long"/>; user-space x64 pointers fit in
    /// 48 bits so casting from a <c>ulong</c> or <c>IntPtr</c> is lossless).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> InspectUnitAsync(
        string profileId, long objAddr, CancellationToken cancellationToken);

    /// <inheritdoc cref="InspectUnitAsync(string, long, CancellationToken)"/>
    Task<ActionExecutionResult> InspectUnitAsync(string profileId, long objAddr)
    {
        return InspectUnitAsync(profileId, objAddr, CancellationToken.None);
    }
}

/// <summary>
/// Reads hardpoint data for a unit via the SWFOC bridge's <c>SWFOC_GetHardpoints</c> helper.
/// </summary>
public interface IHardpointService
{
    /// <summary>
    /// Fetches the hardpoint list for a unit at a given object address.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="objAddr">Absolute runtime address of the GameObject
    /// (CLS-compliant <see cref="long"/>; see
    /// <see cref="IUnitInspectorService.InspectUnitAsync(string, long, CancellationToken)"/>
    /// for rationale).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> GetHardpointsAsync(
        string profileId, long objAddr, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetHardpointsAsync(string, long, CancellationToken)"/>
    Task<ActionExecutionResult> GetHardpointsAsync(string profileId, long objAddr)
    {
        return GetHardpointsAsync(profileId, objAddr, CancellationToken.None);
    }
}

/// <summary>
/// Configures hero respawn behavior via the SWFOC bridge.
/// </summary>
public interface IHeroRespawnService
{
    /// <summary>
    /// Sets a custom hero respawn duration in seconds.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="seconds">Respawn duration in seconds (non-negative).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> SetCustomRespawnAsync(
        string profileId, double seconds, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetCustomRespawnAsync(string, double, CancellationToken)"/>
    Task<ActionExecutionResult> SetCustomRespawnAsync(string profileId, double seconds)
    {
        return SetCustomRespawnAsync(profileId, seconds, CancellationToken.None);
    }

    /// <summary>
    /// Enables or disables the instant-respawn hook.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="enable">True to enable instant respawn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> SetInstantRespawnAsync(
        string profileId, bool enable, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetInstantRespawnAsync(string, bool, CancellationToken)"/>
    Task<ActionExecutionResult> SetInstantRespawnAsync(string profileId, bool enable)
    {
        return SetInstantRespawnAsync(profileId, enable, CancellationToken.None);
    }
}

/// <summary>
/// Captures crash-analysis snapshots via the SWFOC bridge's <c>SWFOC_DumpState</c> helper.
/// </summary>
public interface ICrashAnalyzerService
{
    /// <summary>
    /// Writes a snapshot of the current game state to the provided path.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="path">Target snapshot file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result from the bridge.</returns>
    Task<ActionExecutionResult> CaptureSnapshotAsync(
        string profileId, string path, CancellationToken cancellationToken);

    /// <inheritdoc cref="CaptureSnapshotAsync(string, string, CancellationToken)"/>
    Task<ActionExecutionResult> CaptureSnapshotAsync(string profileId, string path)
    {
        return CaptureSnapshotAsync(profileId, path, CancellationToken.None);
    }
}
