using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Wraps the C++ bridge's <c>SWFOC_DumpState</c> helper so the editor can ask
/// the running game to write a crash-analysis snapshot to disk.
/// </summary>
/// <remarks>
/// The helper returns <c>"OK: snapshot written to &lt;path&gt; (N bytes)"</c>
/// on success or <c>"ERR: ..."</c> on failure. This service applies a small
/// amount of client-side validation (see <see cref="ValidatePath"/>) before
/// forwarding the command to the bridge because the snapshot path will be
/// interpolated straight into a Lua string literal.
/// </remarks>
public sealed class CrashAnalyzerService : ICrashAnalyzerService
{
    internal const string FeatureId = "v5_crash_analyzer_dump";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<CrashAnalyzerService> _logger;

    /// <summary>
    /// Creates a live crash-analyzer service.
    /// </summary>
    public CrashAnalyzerService(
        ILuaBridgeExecutor bridge,
        ILogger<CrashAnalyzerService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    /// <summary>
    /// Creates an offline crash-analyzer service.
    /// </summary>
    public CrashAnalyzerService(ILogger<CrashAnalyzerService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActionExecutionResult> CaptureSnapshotAsync(
        string profileId, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(path);

        if (!ValidatePath(path))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "Snapshot path failed validation (contains '..' traversal or escape sequences)",
                AddressSource: AddressSource.None,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["rejected_path"] = path
                });
        }

        var luaCommand = BuildCaptureSnapshotLuaCommand(path);

        _logger.LogInformation(
            "CrashAnalyzer capture executing for profile {Profile}: {LuaCommand}",
            profileId, luaCommand);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Snapshot request queued for {path} (offline)",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["path"] = path
            });
    }

    /// <summary>
    /// Builds the Lua command that invokes <c>SWFOC_DumpState</c>.
    /// </summary>
    internal static string BuildCaptureSnapshotLuaCommand(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return "return SWFOC_DumpState(\"" + path + "\")";
    }

    /// <summary>
    /// Rejects suspicious snapshot paths. A path is invalid when it is empty,
    /// contains directory-traversal <c>..</c> segments, or contains a
    /// backslash immediately followed by a double-quote (which would break
    /// out of the Lua string literal we interpolate into).
    /// </summary>
    public static bool ValidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Contains("\\\"", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Contains('"', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
