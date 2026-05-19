using System.IO.Pipes;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Round-trip tests for <see cref="NamedPipeLuaBridgeClient"/>. A test-local
/// pipe server simulates the bridge protocol so we don't need the game.
/// </summary>
public sealed class NamedPipeLuaBridgeClientTests
{
    private static string UniquePipeName(string suffix = "") =>
        $"swfoc_bridge_test_{Guid.NewGuid():N}{suffix}";

    [Fact]
    public async Task SendAsync_ReceivesSuccessResponse_FromTestServer()
    {
        var pipeName = UniquePipeName();
        var serverTask = RunOneShotServer(pipeName, expectedRequest: "return 1+1\0", reply: "2\n");
        var client = new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000);

        var result = await client.SendAsync("return 1+1", CancellationToken.None);
        await serverTask;

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Be("2");
        result.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_ReturnsFailure_WhenBridgeRepliesWithErrPrefix()
    {
        var pipeName = UniquePipeName();
        var serverTask = RunOneShotServer(pipeName, expectedRequest: "return broken\0", reply: "ERR: syntax error\n");
        var client = new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000);

        var result = await client.SendAsync("return broken", CancellationToken.None);
        await serverTask;

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("ERR:");
        result.Response.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_ReturnsFailure_WhenPipeUnavailable()
    {
        var pipeName = UniquePipeName("_nope");
        var client = new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 250, readTimeoutMs: 1000);

        var result = await client.SendAsync("return 1", CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SendAsync_RejectsEmptyCommand_WithoutTouchingPipe()
    {
        var pipeName = UniquePipeName("_empty");
        var client = new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 250, readTimeoutMs: 1000);

        var result = await client.SendAsync(string.Empty, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Empty command");
    }

    [Fact]
    public void IsBridgeAvailable_ReturnsFalse_WhenPipeMissing()
    {
        var pipeName = UniquePipeName("_unavail");
        var client = new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 250, readTimeoutMs: 1000);

        client.IsBridgeAvailable().Should().BeFalse();
    }

    [Fact]
    public async Task IsBridgeAvailable_ReturnsTrue_WhenServerListening()
    {
        var pipeName = UniquePipeName("_avail");
        using var ready = new ManualResetEventSlim(initialState: false);

        // Server task: enter the wait state, then signal ready, then accept and
        // tolerate any client behavior (sync probe or full round-trip).
        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            // Begin the connection wait BEFORE signaling the client thread.
            // This guarantees the server is in the listening state when the
            // client probes — eliminates the race where the client connects
            // and disconnects before WaitForConnectionAsync runs.
            var waitTask = server.WaitForConnectionAsync();
            ready.Set();
            try
            {
                await waitTask;
            }
            catch (IOException)
            {
                // Client may have already closed when we observed the connection.
            }
        });

        ready.Wait(2000).Should().BeTrue();
        var client = new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 1000);
        var available = client.IsBridgeAvailable();

        await serverTask;
        available.Should().BeTrue();
    }

    [Fact]
    public void Constructor_RejectsEmptyPipeName()
    {
        var act = () => new NamedPipeLuaBridgeClient(pipeName: " ", connectTimeoutMs: 100, readTimeoutMs: 100);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsNonPositiveConnectTimeout()
    {
        var act = () => new NamedPipeLuaBridgeClient("p", connectTimeoutMs: 0, readTimeoutMs: 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_RejectsNonPositiveReadTimeout()
    {
        var act = () => new NamedPipeLuaBridgeClient("p", connectTimeoutMs: 100, readTimeoutMs: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultPipeName_MatchesBridgeContract()
    {
        NamedPipeLuaBridgeClient.DefaultPipeName.Should().Be("swfoc_bridge");
    }

    private static Task RunOneShotServer(string pipeName, string expectedRequest, string reply)
    {
        return Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync();

            var buffer = new byte[1024];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = await server.ReadAsync(buffer.AsMemory(totalRead));
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (buffer[totalRead - 1] == 0x00)
                {
                    break;
                }
            }

            var received = Encoding.ASCII.GetString(buffer, 0, totalRead);
            received.Should().Be(expectedRequest);

            var replyBytes = Encoding.ASCII.GetBytes(reply);
            await server.WriteAsync(replyBytes, 0, replyBytes.Length);
            await server.FlushAsync();
            server.WaitForPipeDrain();
        });
    }
}
