using System.IO.Pipes;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Unit tests for <see cref="BridgeAssertionRunner"/>. A scripted pipe server serves
/// a queue of replies (pre-probe, command, post-probe) so each assertion performs
/// three independent round-trips against the same logical bridge.
/// </summary>
public sealed class BridgeAssertionRunnerTests
{
    private static string UniquePipeName(string suffix = "") =>
        $"swfoc_bridge_assertion_test_{Guid.NewGuid():N}{suffix}";

    [Fact]
    public async Task RunAsync_Passes_WhenDeltaMatches()
    {
        var pipeName = UniquePipeName();
        var serverTask = RunScriptedServer(pipeName, new[] { "10\n", "OK\n", "20\n" });
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000));

        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return credits",
            LuaCommand = "credits = credits + 10",
            PostStateProbe = "return credits",
            ExpectDelta = (pre, post) => int.Parse(post) == int.Parse(pre) + 10,
            Description = "credits increment by 10"
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        await serverTask;

        result.Passed.Should().BeTrue();
        result.PreState.Should().Be("10");
        result.PostState.Should().Be("20");
        result.CommandResponse.Should().Be("OK");
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_Fails_WhenDeltaDoesNotMatch()
    {
        var pipeName = UniquePipeName("_nodelta");
        var serverTask = RunScriptedServer(pipeName, new[] { "10\n", "OK\n", "10\n" });
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000));

        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return credits",
            LuaCommand = "credits = credits + 10",
            PostStateProbe = "return credits",
            ExpectDelta = (pre, post) => int.Parse(post) == int.Parse(pre) + 10,
            Description = "credits increment by 10"
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        await serverTask;

        result.Passed.Should().BeFalse();
        result.PreState.Should().Be("10");
        result.PostState.Should().Be("10");
        result.FailureReason.Should().NotBeNull();
        result.FailureReason.Should().Contain("pre='10'").And.Contain("post='10'");
    }

    [Fact]
    public async Task RunAsync_Fails_WhenPredicateThrows()
    {
        var pipeName = UniquePipeName("_throw");
        var serverTask = RunScriptedServer(pipeName, new[] { "garbage\n", "OK\n", "garbage\n" });
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000));

        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return credits",
            LuaCommand = "credits = credits + 10",
            PostStateProbe = "return credits",
            ExpectDelta = (pre, post) => throw new ArgumentException("bad shape"),
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        await serverTask;

        result.Passed.Should().BeFalse();
        result.FailureReason.Should().NotBeNull();
        result.FailureReason.Should().Contain("ExpectDelta predicate threw");
        result.FailureReason.Should().Contain("bad shape");
        result.PreState.Should().Be("garbage");
        result.PostState.Should().Be("garbage");
    }

    [Fact]
    public async Task RunAsync_Fails_WhenPreProbePipeUnreachable()
    {
        var pipeName = UniquePipeName("_unreachable");
        // No server running.
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 250, readTimeoutMs: 1000));

        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return credits",
            LuaCommand = "credits = credits + 10",
            PostStateProbe = "return credits",
            ExpectDelta = (pre, post) => true,
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.FailureReason.Should().NotBeNull();
        result.FailureReason.Should().Contain("PreStateProbe");
        result.PreState.Should().BeEmpty();
        result.PostState.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Fails_WhenCommandReturnsErr()
    {
        var pipeName = UniquePipeName("_cmderr");
        var serverTask = RunScriptedServer(pipeName, new[] { "10\n", "ERR: simulated failure\n" });
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000));

        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return credits",
            LuaCommand = "broken()",
            PostStateProbe = "return credits",
            ExpectDelta = (pre, post) => true,
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        await serverTask;

        result.Passed.Should().BeFalse();
        result.FailureReason.Should().NotBeNull();
        result.FailureReason.Should().Contain("LuaCommand");
        result.FailureReason.Should().Contain("simulated failure");
        result.PreState.Should().Be("10");
        result.PostState.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_Fails_WhenPostProbeReturnsErr()
    {
        var pipeName = UniquePipeName("_posterr");
        var serverTask = RunScriptedServer(pipeName, new[] { "10\n", "OK\n", "ERR: probe crashed\n" });
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000));

        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return credits",
            LuaCommand = "credits = credits + 10",
            PostStateProbe = "return credits",
            ExpectDelta = (pre, post) => true,
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        await serverTask;

        result.Passed.Should().BeFalse();
        result.FailureReason.Should().NotBeNull();
        result.FailureReason.Should().Contain("PostStateProbe");
        result.FailureReason.Should().Contain("probe crashed");
        result.PreState.Should().Be("10");
        result.CommandResponse.Should().Be("OK");
    }

    [Fact]
    public async Task RunAsync_Passes_ForReadOnlyAssertion_WhenPreEqualsPost()
    {
        var pipeName = UniquePipeName("_readonly");
        var serverTask = RunScriptedServer(pipeName, new[] { "100\n", "100\n", "100\n" });
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 2000, readTimeoutMs: 5000));

        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return credits",
            LuaCommand = "return credits",
            PostStateProbe = "return credits",
            ExpectDelta = (pre, post) => pre == post,
            Description = "read-only: state unchanged"
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        await serverTask;

        result.Passed.Should().BeTrue();
        result.PreState.Should().Be("100");
        result.PostState.Should().Be("100");
        result.CommandResponse.Should().Be("100");
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsNullBridge()
    {
        var act = () => new BridgeAssertionRunner(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_RejectsNullAssertion()
    {
        var pipeName = UniquePipeName("_nullassert");
        var runner = new BridgeAssertionRunner(
            new NamedPipeLuaBridgeClient(pipeName, connectTimeoutMs: 250, readTimeoutMs: 1000));

        var act = async () => await runner.RunAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// One-shot pipe server that accepts N sequential connections and returns the
    /// matching reply from <paramref name="scriptedReplies"/> for each. This mimics
    /// the real SWFOC bridge, which closes the pipe after every command.
    /// </summary>
    private static Task RunScriptedServer(string pipeName, IReadOnlyList<string> scriptedReplies)
    {
        return Task.Run(async () =>
        {
            foreach (var reply in scriptedReplies)
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();

                // Drain the client's command (terminated by 0x00) before replying.
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

                var replyBytes = Encoding.ASCII.GetBytes(reply);
                await server.WriteAsync(replyBytes, 0, replyBytes.Length);
                await server.FlushAsync();
                server.WaitForPipeDrain();
                server.Disconnect();
            }
        });
    }
}
