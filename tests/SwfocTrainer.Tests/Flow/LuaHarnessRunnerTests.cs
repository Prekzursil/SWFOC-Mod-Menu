using FluentAssertions;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class LuaHarnessRunnerTests
{
    [Fact]
    public async Task RunAsync_ShouldExecuteOfflineHarness_AndEmitTelemetryMarker()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var luaScript = Path.Join(root, "mods", "SwfocTrainerTelemetry", "Data", "Scripts", "TelemetryModeEmitter.lua");
        var runner = new LuaHarnessRunner(harnessScript);

        var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScript, Mode: "TacticalLand"));

        result.Succeeded.Should().BeTrue(result.Message);
        result.OutputLines.Should().Contain(x => x.Contains("SWFOC_TRAINER_TELEMETRY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenLuaScriptDoesNotExist()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var runner = new LuaHarnessRunner(harnessScript);

        var result = await runner.RunAsync(new LuaHarnessRunRequest(Path.Join(root, "missing.lua")));

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("lua_script_missing");
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenScriptPathIsWhitespace()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var runner = new LuaHarnessRunner(harnessScript);

        var result = await runner.RunAsync(new LuaHarnessRunRequest("   "));

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("lua_script_missing");
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenHarnessRunnerScriptIsMissing()
    {
        var root = TestPaths.FindRepoRoot();
        var luaScript = Path.Join(root, "mods", "SwfocTrainerTelemetry", "Data", "Scripts", "TelemetryModeEmitter.lua");
        var runner = new LuaHarnessRunner(Path.Join(root, "nonexistent", "run-harness.ps1"));

        var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScript));

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("harness_runner_missing");
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenLuaScriptMissesTelemetryMarker()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var tempDir = Path.Join(Path.GetTempPath(), $"swfoc-lua-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempLua = Path.Join(tempDir, "no_telemetry.lua");
            await File.WriteAllTextAsync(tempLua, "-- no telemetry markers here");
            var runner = new LuaHarnessRunner(harnessScript);

            var result = await runner.RunAsync(new LuaHarnessRunRequest(tempLua));

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_marker_missing");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenLuaScriptHasMarkerButNoEmitter()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var tempDir = Path.Join(Path.GetTempPath(), $"swfoc-lua-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempLua = Path.Join(tempDir, "partial.lua");
            await File.WriteAllTextAsync(tempLua, "-- SWFOC_TRAINER_TELEMETRY but no emitter function");
            var runner = new LuaHarnessRunner(harnessScript);

            var result = await runner.RunAsync(new LuaHarnessRunRequest(tempLua));

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_marker_missing");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_ShouldThrow_WhenRequestIsNull()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var runner = new LuaHarnessRunner(harnessScript);

        var act1 = async () => await runner.RunAsync((LuaHarnessRunRequest)null!);
        var act2 = async () => await runner.RunAsync((LuaHarnessRunRequest)null!, CancellationToken.None);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenHarnessPathIsNull()
    {
        var act = () => new LuaHarnessRunner(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultConstructor_ShouldResolveDefaultHarnessPath()
    {
        var runner = new LuaHarnessRunner();
        runner.Should().NotBeNull();
    }
}
