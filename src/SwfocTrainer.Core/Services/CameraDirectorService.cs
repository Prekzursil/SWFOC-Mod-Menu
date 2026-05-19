using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class CameraDirectorService : ICameraDirectorService
{
    private static readonly IReadOnlyDictionary<string, string> CommandMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["point_at"] = "Point_Camera_At(unit)",
            ["scroll_to"] = "Scroll_Camera_To(position)",
            ["zoom"] = "Zoom_Camera(level)",
            ["rotate"] = "Rotate_Camera_By(degrees)",
            ["letterbox_on"] = "Letter_Box_On()",
            ["letterbox_off"] = "Letter_Box_Off()",
            ["freeze"] = "Game_Set_Speed(0)",
            ["unfreeze"] = "Game_Set_Speed(1)"
        };

    internal const string FeatureId = "v5_camera_director";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<CameraDirectorService> _logger;

    public CameraDirectorService(
        ILuaBridgeExecutor bridge,
        ILogger<CameraDirectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public CameraDirectorService(ILogger<CameraDirectorService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    public async Task<ActionExecutionResult> ExecuteCameraCommandAsync(
        string profileId, string command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(command);

        var luaCall = BuildCameraLuaCommand(command, null);

        if (luaCall is null)
        {
            _logger.LogWarning(
                "Unknown camera command '{Command}' in profile {ProfileId}",
                command, profileId);

            return new ActionExecutionResult(
                Succeeded: false,
                Message: $"Unknown camera command: '{command}'",
                AddressSource: AddressSource.None);
        }

        _logger.LogInformation(
            "Camera command '{Command}' executing as {LuaCall} for profile {ProfileId}",
            command, luaCall, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCall, FeatureId, cancellationToken);
        }

        // Fallback: return prepared result when no bridge is configured.
        var diagnostics = new Dictionary<string, object?>
        {
            ["lua_call"] = luaCall,
            ["command"] = command
        };

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Camera command '{command}' prepared",
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// Builds the Lua command string for a camera operation.
    /// Returns null when <paramref name="command"/> is not a recognized camera command.
    /// </summary>
    internal static string? BuildCameraLuaCommand(string command, string? parameter)
    {
        ArgumentNullException.ThrowIfNull(command);

        var trimmed = command.Trim();
        if (!CommandMap.ContainsKey(trimmed))
        {
            return null;
        }

        return trimmed.ToUpperInvariant() switch
        {
            "ZOOM" => $"Zoom_Camera({parameter ?? "1.0"})",
            "ROTATE" => $"Rotate_Camera_By({parameter ?? "0"})",
            "POINT_AT" => "Point_Camera_At(selectedUnit)",
            "SCROLL_TO" => $"Scroll_Camera_To({parameter ?? "0,0,0"})",
            "LETTERBOX_ON" => "Letter_Box_On()",
            "LETTERBOX_OFF" => "Letter_Box_Off()",
            "FREEZE" => "Game_Set_Speed(0)",
            "UNFREEZE" => "Game_Set_Speed(1)",
            _ => null
        };
    }

    internal static string? ResolveCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return CommandMap.TryGetValue(command.Trim(), out var luaCall)
            ? luaCall
            : null;
    }
}
