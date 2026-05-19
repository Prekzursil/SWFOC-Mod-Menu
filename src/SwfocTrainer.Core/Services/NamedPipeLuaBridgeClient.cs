using System.IO.Pipes;
using System.Text;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Direct named-pipe client for the SWFOC Lua bridge (powrprof.dll injected
/// into the game). Connects to <c>\\.\pipe\swfoc_bridge</c>, writes a Lua
/// command, and reads the bridge's response. Used by v5 services that need
/// to execute Lua directly without routing through the SwfocExtender.Host
/// helper plugin (which is a stub for non-native features).
/// </summary>
/// <remarks>
/// The bridge protocol is:
/// <list type="number">
/// <item>Client connects.</item>
/// <item>Client writes command bytes followed by a single 0x00 terminator.</item>
/// <item>Bridge queues the command for the next <c>luaD_call</c> hook fire on a
///       registered Lua state, executes via <c>DoString</c>, and writes the
///       result back as ASCII bytes.</item>
/// <item>Bridge closes the connection.</item>
/// </list>
/// The bridge times out after ~10 seconds with <c>ERR: timeout</c> when the
/// game is paused or in a menu (no Lua hooks firing). Callers should treat
/// timeout as a recoverable, transient state.
/// </remarks>
public sealed class NamedPipeLuaBridgeClient
{
    /// <summary>Bridge pipe name (server side: powrprof.dll injected into the game).</summary>
    public const string DefaultPipeName = "swfoc_bridge";

    /// <summary>Maximum response payload accepted from the bridge.</summary>
    /// <remarks>
    /// Bumped from 4096 -> 16384 on 2026-04-10 to match the bridge-side
    /// <c>PIPE_CMD_MAX</c> bump in <c>lua_bridge.cpp</c>. The change lets the
    /// <c>SWFOC_DiagListRegisteredFunctions</c> manifest (and any future
    /// diagnostic payloads) round-trip without being silently truncated
    /// at the client buffer.
    /// </remarks>
    public const int MaxResponseBytes = 16384;

    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;
    private readonly int _readTimeoutMs;

    public NamedPipeLuaBridgeClient()
        : this(DefaultPipeName, connectTimeoutMs: 1500, readTimeoutMs: 12000)
    {
    }

    public NamedPipeLuaBridgeClient(string pipeName, int connectTimeoutMs, int readTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name must not be empty.", nameof(pipeName));
        }

        if (connectTimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeoutMs));
        }

        if (readTimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(readTimeoutMs));
        }

        _pipeName = pipeName;
        _connectTimeoutMs = connectTimeoutMs;
        _readTimeoutMs = readTimeoutMs;
    }

    /// <summary>Pipe name this client connects to.</summary>
    public string PipeName => _pipeName;

    /// <summary>
    /// Synchronously checks whether the bridge pipe is currently reachable.
    /// Returns true only if a connection can be established within the configured timeout.
    /// </summary>
    public bool IsBridgeAvailable()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            pipe.Connect(_connectTimeoutMs);
            return pipe.IsConnected;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a Lua command to the bridge and reads the response.
    /// </summary>
    /// <param name="luaCommand">Lua source to execute (e.g. <c>return SWFOC_GetVersion()</c>).</param>
    /// <param name="cancellationToken">Cancellation token honored by both connect and read.</param>
    /// <returns>The raw bridge response (may begin with <c>ERR:</c> on failure).</returns>
    public async Task<BridgeRoundTripResult> SendAsync(string luaCommand, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(luaCommand);
        if (luaCommand.Length == 0)
        {
            return BridgeRoundTripResult.Failure("Empty command rejected at client.");
        }

        try
        {
            await using var pipe = new NamedPipeClientStream(
                serverName: ".",
                pipeName: _pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            await pipe.ConnectAsync(_connectTimeoutMs, cancellationToken).ConfigureAwait(false);

            // Bridge protocol expects command bytes followed by a single null terminator.
            var payload = Encoding.ASCII.GetBytes(luaCommand);
            await pipe.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await pipe.WriteAsync(new byte[] { 0x00 }, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Read with an upper bound so we don't hang forever if the bridge is wedged.
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(_readTimeoutMs);

            var buffer = new byte[MaxResponseBytes];
            var totalRead = 0;
            try
            {
                while (totalRead < buffer.Length)
                {
                    var read = await pipe.ReadAsync(buffer.AsMemory(totalRead), readCts.Token)
                        .ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    totalRead += read;
                    if (buffer[totalRead - 1] == '\n' || buffer[totalRead - 1] == 0x00)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return BridgeRoundTripResult.Failure(
                    $"Bridge response read timed out after {_readTimeoutMs}ms.");
            }

            if (totalRead == 0)
            {
                return BridgeRoundTripResult.Failure("Bridge closed connection without responding.");
            }

            var response = Encoding.ASCII.GetString(buffer, 0, totalRead).TrimEnd('\0', '\n', '\r');
            return response.StartsWith("ERR:", StringComparison.Ordinal)
                ? BridgeRoundTripResult.Failure(response)
                : BridgeRoundTripResult.Success(response);
        }
        catch (TimeoutException)
        {
            return BridgeRoundTripResult.Failure(
                $"Bridge pipe '{_pipeName}' did not accept a connection within {_connectTimeoutMs}ms.");
        }
        catch (IOException ex)
        {
            return BridgeRoundTripResult.Failure($"Bridge pipe IO error: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return BridgeRoundTripResult.Failure($"Bridge pipe access denied: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of a single round-trip to the SWFOC Lua bridge.
/// </summary>
/// <param name="Succeeded">True when the bridge returned a non-error payload.</param>
/// <param name="Response">Raw response payload (success path) or empty.</param>
/// <param name="ErrorMessage">Error description (failure path) or empty.</param>
public readonly record struct BridgeRoundTripResult(
    bool Succeeded,
    string Response,
    string ErrorMessage)
{
    public static BridgeRoundTripResult Success(string response) =>
        new(Succeeded: true, Response: response, ErrorMessage: string.Empty);

    public static BridgeRoundTripResult Failure(string errorMessage) =>
        new(Succeeded: false, Response: string.Empty, ErrorMessage: errorMessage);
}
