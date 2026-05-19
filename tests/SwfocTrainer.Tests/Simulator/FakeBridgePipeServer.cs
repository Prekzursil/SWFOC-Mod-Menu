using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SwfocTrainer.Tests.Simulator;

/// <summary>
/// In-process named-pipe server that speaks the same wire protocol as
/// the real <c>powrprof.dll</c> bridge:
/// <list type="number">
///   <item>Client connects.</item>
///   <item>Client writes Lua command bytes followed by a 0x00 terminator.</item>
///   <item>Server runs the matching handler and writes the ASCII reply.</item>
///   <item>Server disconnects. Loop.</item>
/// </list>
/// Handlers register a <em>command prefix</em>; longest prefix wins. The
/// editor's <c>V2BridgeAdapter</c> connects as a client just like
/// it would to the real bridge.
/// </summary>
/// <remarks>
/// <para>
/// This is the simulator's transport layer. The state and per-function
/// semantics live in <see cref="SwfocSimulator"/>.
/// </para>
/// <para>
/// Threading: a single listener loop accepts one client at a time
/// (mirrors the real bridge's <c>max_instances=1</c>). Handlers run on
/// the listener thread.
/// </para>
/// </remarks>
public sealed class FakeBridgePipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerLoop;
    private readonly Dictionary<string, Func<string, string>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private long _commandsServed;
    public long CommandsServed => Interlocked.Read(ref _commandsServed);

    public string PipeName => _pipeName;

    public FakeBridgePipeServer(string pipeName)
    {
        _pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
    }

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
            try
            {
                using var kick = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
                kick.Connect(200);
            }
            catch (Exception)
            {
                // Best-effort kick to break WaitForConnectionAsync.
            }
            _listenerLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException) { /* expected on cancellation */ }
        catch (TimeoutException) { /* listener leaked; pipe handles GC */ }
        _cts.Dispose();
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
                if (ct.IsCancellationRequested) break;
                await ServeOneClientAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { /* stale handle, recreate */ }
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

        while (totalRead < buffer.Length && !foundTerminator)
        {
            var got = await pipe.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
            if (got <= 0) break;
            for (var i = totalRead; i < totalRead + got; i++)
            {
                if (buffer[i] == 0x00)
                {
                    foundTerminator = true;
                    totalRead = i;
                    break;
                }
            }
            if (!foundTerminator) totalRead += got;
        }

        var command = Encoding.ASCII.GetString(buffer, 0, totalRead).Trim();
        var response = Dispatch(command);
        var responseBytes = Encoding.ASCII.GetBytes(response);
        await pipe.WriteAsync(responseBytes.AsMemory(), ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);

        Interlocked.Increment(ref _commandsServed);

        if (pipe.IsConnected)
        {
            try { pipe.WaitForPipeDrain(); } catch (IOException) { /* drained */ }
            pipe.Disconnect();
        }
    }

    private string Dispatch(string command)
    {
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
                return $"ERR: handler-throw: {ex.GetType().Name}: {ex.Message}";
            }
        }
        return "ERR: no handler for: " + (command.Length > 80 ? command[..80] + "..." : command);
    }
}
