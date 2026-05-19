using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Drives fog-of-war reveal via the built-in Alamo <c>FOWManager</c> Lua API.
/// </summary>
/// <remarks>
/// <para>
/// NOTE: This service is intentionally NOT implemented on top of a
/// <c>SWFOC_*</c> helper. The C++ bridge does not currently ship a native
/// fog-of-war helper, so the editor instead leans on the engine's existing
/// <c>FOWManager.Reveal_All</c> / <c>FOWManager.Undo_Reveal_All</c> Lua
/// globals. These globals are always present in tactical scripts and have
/// been verified in the SWFOC reverse engineering notes under the
/// <c>knowledge-base/alamo_engine_reference.md</c> FOW section.
/// </para>
/// <para>
/// The Lua snippet uses a <c>local</c>-scoped <c>Find_Player</c> call and
/// guards the reveal behind an <c>if</c> so that, if FOWManager is unavailable
/// (e.g. the caller is in the menu or on the galactic layer), the command is a
/// safe no-op rather than a Lua error.
/// </para>
/// </remarks>
public sealed class MaphackService : IMaphackService
{
    internal const string RevealFeatureId = "v5_maphack_reveal";
    internal const string UndoFeatureId = "v5_maphack_undo";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<MaphackService> _logger;

    /// <summary>
    /// Creates a live maphack service.
    /// </summary>
    public MaphackService(
        ILuaBridgeExecutor bridge,
        ILogger<MaphackService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline maphack service.
    /// </summary>
    public MaphackService(ILogger<MaphackService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> RevealAllAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var luaCommand = BuildRevealAllLuaCommand();

        _logger.LogInformation(
            "Maphack reveal executing for profile {Profile}: {LuaCommand}",
            profileId, luaCommand);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, RevealFeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: "Maphack reveal issued (offline)",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["note"] = "non-SWFOC_* path; uses engine FOWManager directly"
            });
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> UndoRevealAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var luaCommand = BuildUndoRevealLuaCommand();

        _logger.LogInformation(
            "Maphack undo executing for profile {Profile}: {LuaCommand}",
            profileId, luaCommand);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, UndoFeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: "Maphack undo issued (offline)",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["note"] = "non-SWFOC_* path; uses engine FOWManager directly"
            });
    }

    /// <summary>
    /// Builds the Lua command that reveals the full map for the local player
    /// via the engine's <c>FOWManager.Reveal_All</c> API.
    /// </summary>
    internal static string BuildRevealAllLuaCommand()
    {
        return "local p = Find_Player(\"local\"); if p and FOWManager then FOWManager.Reveal_All(p) end";
    }

    /// <summary>
    /// Builds the Lua command that undoes a previous full-map reveal for the
    /// local player via <c>FOWManager.Undo_Reveal_All</c>.
    /// </summary>
    internal static string BuildUndoRevealLuaCommand()
    {
        return "local p = Find_Player(\"local\"); if p and FOWManager then FOWManager.Undo_Reveal_All(p) end";
    }
}
