using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SwfocTrainer.UiTests;

/// <summary>
/// In-process fake of the bridge DLL pipe server (`\\.\pipe\swfoc_bridge`).
/// Speaks the same wire protocol as <c>powrprof.dll</c>:
/// <list type="number">
///   <item>Client connects.</item>
///   <item>Client writes Lua command bytes followed by a single 0x00 terminator.</item>
///   <item>Server dispatches to a registered handler and writes the ASCII response.</item>
///   <item>Server closes the connection. Loop.</item>
/// </list>
/// Use via the disposable pattern; <see cref="Dispose"/> stops the listener.
/// </summary>
/// <remarks>
/// <para>
/// This class is the SERVER side of the named pipe — the editor (under test)
/// is the client. Spawning this fake before launching the editor lets the
/// V2 Diagnostics tab succeed its <c>SWFOC_GetVersion</c>/<c>SWFOC_GetBuildInfo</c>/
/// <c>SWFOC_DiagListRegisteredFunctions</c>/<c>SWFOC_DiagSelfTest</c> probes
/// without the real bridge DLL or a running game, so the tab renders its
/// rich surface and our UI audit can walk the controls underneath.
/// </para>
/// <para>
/// The dispatcher takes the full Lua-command string (e.g.
/// <c>return SWFOC_GetVersion()</c>) and matches against registered prefixes;
/// the longest matching prefix wins. Responses are returned as raw ASCII —
/// the editor's Lua bridge expects to re-parse them into Lua values, which
/// for diagnostics it does in a forgiving display-only manner.
/// </para>
/// </remarks>
public sealed class FakeBridgeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerLoop;
    private readonly Dictionary<string, Func<string, string>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private long _commandsServed;
    public long CommandsServed => Interlocked.Read(ref _commandsServed);

    public FakeBridgeServer(string pipeName = "swfoc_bridge")
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
        RegisterDefaultDiagnosticsHandlers();
    }

    /// <summary>
    /// Register a custom handler for any Lua command whose body STARTS WITH
    /// <paramref name="commandPrefix"/>. Longest-prefix wins. Default
    /// diagnostics commands are pre-registered by the constructor.
    /// </summary>
    public void Register(string commandPrefix, Func<string, string> handler)
    {
        ArgumentNullException.ThrowIfNull(commandPrefix);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[commandPrefix] = handler;
    }

    public void Start()
    {
        if (_listenerLoop is not null)
        {
            throw new InvalidOperationException("Listener already running.");
        }
        _listenerLoop = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            // Kick the listener out of WaitForConnectionAsync by opening a
            // dummy client — the listener's outer try/finally cleans up.
            try
            {
                using var kick = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                kick.Connect(200);
            }
            catch (Exception)
            {
                // Best-effort kick.
            }
            _listenerLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Cancellation propagated up — expected.
        }
        catch (TimeoutException)
        {
            // Listener did not exit in time — leak the task; pipe handles
            // will be GC'd.
        }
        _cts.Dispose();
    }

    private void RegisterDefaultDiagnosticsHandlers()
    {
        // The editor's V2 DiagnosticsTabViewModel probes these on Loaded
        // when V2Settings.AutoConnectOnStartup is true. Returning plausible
        // short ASCII strings unblocks the tab's full surface (PipeConnected,
        // VersionText, BuildInfoText, RegisteredHelpersText, SelfTestText).

        // Wrap responses in a leading '"' / trailing '"' so the Lua-bridge
        // adapter on the editor side parses them as strings. The actual
        // bridge sends raw text; the editor display-renders whatever it gets.
        Register("return SWFOC_GetVersion",
            _ => "swfoc_lua_bridge fake-server v0.0.1 (UI-test stub)");
        Register("return SWFOC_GetBuildInfo",
            _ => "build=fake-server commit=ui-test branch=ui-test ts=2026-04-27T00:00:00Z");
        Register("return SWFOC_DiagListRegisteredFunctions",
            _ => "SWFOC_GetVersion,SWFOC_GetBuildInfo,SWFOC_DiagSelfTest,SWFOC_DiagListRegisteredFunctions");
        Register("return SWFOC_DiagSelfTest",
            _ => "OK self_test=pass probes=4/4 fake_server=true");

        // Generic ping — some panels send this directly.
        Register("ping", _ => "pong");

        // Any other return-statement: respond with a benign string so the tab
        // doesn't error-out on unrecognised diagnostics probes that may have
        // been added since this fake was written.
        Register("return ", _ => "(fake-bridge: unknown probe; returning stub)");
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    break;
                }
                await ServeOneClientAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // Stale handle / pipe broken; recreate.
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task ServeOneClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var buffer = new byte[16384];
        var totalRead = 0;
        var foundTerminator = false;

        // Read until we hit a 0x00 or the pipe closes / buffer fills.
        while (totalRead < buffer.Length && !foundTerminator)
        {
            var got = await pipe.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
            if (got <= 0)
            {
                break;
            }
            for (var i = totalRead; i < totalRead + got; i++)
            {
                if (buffer[i] == 0x00)
                {
                    foundTerminator = true;
                    totalRead = i;
                    break;
                }
            }
            if (!foundTerminator)
            {
                totalRead += got;
            }
        }

        var command = Encoding.ASCII.GetString(buffer, 0, totalRead).Trim();
        var response = Dispatch(command);
        var responseBytes = Encoding.ASCII.GetBytes(response);
        await pipe.WriteAsync(responseBytes.AsMemory(), ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);

        Interlocked.Increment(ref _commandsServed);

        // Disconnect so the next iteration can accept a new client.
        if (pipe.IsConnected)
        {
            try
            {
                pipe.WaitForPipeDrain();
            }
            catch (IOException)
            {
                // Drained partial — fine.
            }
            pipe.Disconnect();
        }
    }

    private string Dispatch(string command)
    {
        // Longest-prefix match.
        string? bestKey = null;
        foreach (var key in _handlers.Keys)
        {
            if (command.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                if (bestKey is null || key.Length > bestKey.Length)
                {
                    bestKey = key;
                }
            }
        }
        if (bestKey is not null && _handlers.TryGetValue(bestKey, out var handler))
        {
            try
            {
                return handler(command);
            }
            catch (Exception ex)
            {
                return $"ERR: handler-throw: {ex.GetType().Name}";
            }
        }
        return "ERR: no handler for: " + (command.Length > 80 ? command[..80] + "..." : command);
    }
}
