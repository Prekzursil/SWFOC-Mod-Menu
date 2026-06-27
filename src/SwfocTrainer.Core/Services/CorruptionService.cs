using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Manages planet corruption state via the Lua bridge.
/// FoC-SPECIFIC: corruption mechanics only exist in Forces of Corruption,
/// not in base Empire at War.
/// </summary>
public sealed class CorruptionService : ICorruptionService
{
    internal const string SetFeatureId = "v5_corruption_set";
    internal const string RemoveFeatureId = "v5_corruption_remove";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<CorruptionService> _logger;

    public CorruptionService(
        ILuaBridgeExecutor bridge,
        ILogger<CorruptionService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public CorruptionService(ILogger<CorruptionService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    public async Task<ActionExecutionResult> SetCorruptionAsync(
        string profileId, CorruptionEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.PlanetId))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "PlanetId must not be empty",
                AddressSource: AddressSource.None);
        }

        if (!ValidateCorruptionType(entry.Type))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "Specify a corruption type \u2014 CorruptionType.None is not a valid corruption action",
                AddressSource: AddressSource.None);
        }

        var luaCommand = BuildCorruptionLuaCommand(entry);

        _logger.LogInformation(
            "Corruption executing: {Type} on {Planet} (level {Level}) for profile {Profile}",
            entry.Type, entry.PlanetId, entry.Level, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, SetFeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Corruption set: {entry.Type} on {entry.PlanetId}",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["planet_id"] = entry.PlanetId,
                ["corruption_type"] = entry.Type.ToString(),
                ["lua_call"] = luaCommand,
                ["foc_only"] = "corruption mechanics are FoC-specific"
            });
    }

    public async Task<ActionExecutionResult> RemoveCorruptionAsync(
        string profileId, string planetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(planetId);

        if (string.IsNullOrWhiteSpace(planetId))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "PlanetId must not be empty",
                AddressSource: AddressSource.None);
        }

        var luaCommand = BuildRemoveCorruptionLuaCommand(planetId);

        _logger.LogInformation(
            "Corruption removal executing from {Planet} for profile {Profile}",
            planetId, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, RemoveFeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Corruption removed from {planetId}",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["planet_id"] = planetId,
                ["lua_call"] = luaCommand,
                ["foc_only"] = "corruption mechanics are FoC-specific"
            });
    }

    /// <summary>
    /// Builds the Lua command string for applying corruption to a planet.
    /// Uses <c>Story_Event</c> with the pattern CORRUPTION_{TYPE}_{PLANET}.
    /// </summary>
    internal static string BuildCorruptionLuaCommand(CorruptionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var typeUpper = entry.Type.ToString().ToUpperInvariant();
        var planetUpper = entry.PlanetId.ToUpperInvariant();
        return $"Story_Event(\"CORRUPTION_{typeUpper}_{planetUpper}\")";
    }

    /// <summary>
    /// Builds the Lua command string for removing corruption from a planet.
    /// </summary>
    internal static string BuildRemoveCorruptionLuaCommand(string planetId)
    {
        ArgumentNullException.ThrowIfNull(planetId);
        var planetUpper = planetId.ToUpperInvariant();
        return $"Story_Event(\"REMOVE_CORRUPTION_{planetUpper}\")";
    }

    /// <summary>
    /// Returns true when the corruption type represents a valid corruption action.
    /// <see cref="CorruptionType.None"/> is invalid because the caller must
    /// explicitly choose a mechanism.
    /// </summary>
    internal static bool ValidateCorruptionType(CorruptionType type)
    {
        return type switch
        {
            CorruptionType.None => false,
            CorruptionType.Racketeering => true,
            CorruptionType.Bribery => true,
            CorruptionType.Piracy => true,
            CorruptionType.Kidnapping => true,
            CorruptionType.Sabotage => true,
            _ => false
        };
    }
}
