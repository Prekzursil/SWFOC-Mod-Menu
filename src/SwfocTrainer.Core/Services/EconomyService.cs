using System.Globalization;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Wraps the C++ bridge's credit and tech helpers into a single economy
/// service. Each public method corresponds to one or two <c>SWFOC_*</c>
/// Lua helpers and every Lua command is built by a dedicated
/// <c>BuildXxxLuaCommand</c> static method so it is unit-testable in
/// isolation.
/// </summary>
/// <remarks>
/// Credit / tech helpers covered:
/// <list type="bullet">
///   <item><description><c>SWFOC_SetCredits(amount)</c> (local player only)</description></item>
///   <item><description><c>SWFOC_GetCredits()</c> (local player only)</description></item>
///   <item><description><c>SWFOC_SetTechLevel(level)</c> (local player only)</description></item>
///   <item><description><c>SWFOC_SetCreditsForSlot(slot, amount)</c></description></item>
///   <item><description><c>SWFOC_GetCreditsForSlot(slot)</c></description></item>
///   <item><description><c>SWFOC_SetTechForSlot(slot, level)</c></description></item>
///   <item><description><c>SWFOC_GetTechForSlot(slot)</c></description></item>
///   <item><description><c>SWFOC_DrainEnemyCredits()</c></description></item>
///   <item><description><c>SWFOC_UncapCredits()</c></description></item>
///   <item><description><c>SWFOC_GetMaxCredits()</c></description></item>
/// </list>
/// A negative <c>slot</c> argument to <see cref="SetCreditsAsync"/> or
/// <see cref="SetTechAsync"/> routes to the local-player-only helper
/// (<c>SWFOC_SetCredits</c> / <c>SWFOC_SetTechLevel</c>), whereas a
/// non-negative slot uses the slot-aware helpers.
/// </remarks>
public sealed class EconomyService : IEconomyService
{
    internal const string FeatureId = "v5_economy";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<EconomyService> _logger;

    /// <summary>
    /// Creates a live economy service backed by the Lua bridge.
    /// </summary>
    public EconomyService(
        ILuaBridgeExecutor bridge,
        ILogger<EconomyService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline service used by tests and design-time mocks.
    /// </summary>
    public EconomyService(ILogger<EconomyService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ActionExecutionResult> SetCreditsAsync(
        string profileId, int slot, double amount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ExecuteAsync(profileId, BuildSetCreditsLuaCommand(slot, amount), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionExecutionResult> GetCreditsAsync(
        string profileId, int slot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ExecuteAsync(profileId, BuildGetCreditsLuaCommand(slot), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionExecutionResult> DrainEnemyCreditsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ExecuteAsync(profileId, BuildDrainEnemyCreditsLuaCommand(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionExecutionResult> UncapCreditsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ExecuteAsync(profileId, BuildUncapCreditsLuaCommand(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionExecutionResult> GetMaxCreditsAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ExecuteAsync(profileId, BuildGetMaxCreditsLuaCommand(), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionExecutionResult> SetTechAsync(
        string profileId, int slot, int level, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ExecuteAsync(profileId, BuildSetTechLuaCommand(slot, level), cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActionExecutionResult> GetTechAsync(
        string profileId, int slot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        return ExecuteAsync(profileId, BuildGetTechLuaCommand(slot), cancellationToken);
    }

    private async Task<ActionExecutionResult> ExecuteAsync(
        string profileId, string luaCommand, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Economy executing {LuaCommand} for profile {Profile}",
            luaCommand, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Economy offline: {luaCommand}",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand
            });
    }

    /// <summary>
    /// Builds the Lua command for setting credits. When <paramref name="slot"/>
    /// is negative the local-player helper is used, otherwise the slot-aware
    /// helper is used.
    /// </summary>
    internal static string BuildSetCreditsLuaCommand(int slot, double amount)
    {
        var amountStr = amount.ToString("R", CultureInfo.InvariantCulture);
        return slot < 0
            ? $"return SWFOC_SetCredits({amountStr})"
            : $"return SWFOC_SetCreditsForSlot({slot.ToString(CultureInfo.InvariantCulture)}, {amountStr})";
    }

    /// <summary>
    /// Builds the Lua command for reading credits.
    /// </summary>
    internal static string BuildGetCreditsLuaCommand(int slot)
    {
        return slot < 0
            ? "return SWFOC_GetCredits()"
            : $"return SWFOC_GetCreditsForSlot({slot.ToString(CultureInfo.InvariantCulture)})";
    }

    /// <summary>
    /// Builds the Lua command that drains enemy credits.
    /// </summary>
    internal static string BuildDrainEnemyCreditsLuaCommand()
    {
        return "return SWFOC_DrainEnemyCredits()";
    }

    /// <summary>
    /// Builds the Lua command that removes the engine credit cap.
    /// </summary>
    internal static string BuildUncapCreditsLuaCommand()
    {
        return "return SWFOC_UncapCredits()";
    }

    /// <summary>
    /// Builds the Lua command that reads the current credit cap.
    /// </summary>
    internal static string BuildGetMaxCreditsLuaCommand()
    {
        return "return SWFOC_GetMaxCredits()";
    }

    /// <summary>
    /// Builds the Lua command for setting tech level.
    /// </summary>
    internal static string BuildSetTechLuaCommand(int slot, int level)
    {
        var levelStr = level.ToString(CultureInfo.InvariantCulture);
        return slot < 0
            ? $"return SWFOC_SetTechLevel({levelStr})"
            : $"return SWFOC_SetTechForSlot({slot.ToString(CultureInfo.InvariantCulture)}, {levelStr})";
    }

    /// <summary>
    /// Builds the Lua command for reading tech level.
    /// </summary>
    internal static string BuildGetTechLuaCommand(int slot)
    {
        return slot < 0
            ? "return SWFOC_GetTechForSlot(-1)"
            : $"return SWFOC_GetTechForSlot({slot.ToString(CultureInfo.InvariantCulture)})";
    }
}
