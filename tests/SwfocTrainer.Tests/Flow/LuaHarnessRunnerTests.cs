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
        var harnessScript = Path.Combine(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var luaScript = Path.Combine(root, "mods", "SwfocTrainerTelemetry", "Data", "Scripts", "TelemetryModeEmitter.lua");
        var runner = new LuaHarnessRunner(harnessScript);

        var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScript, Mode: "TacticalLand"));

        result.Succeeded.Should().BeTrue(result.Message);
        result.OutputLines.Should().Contain(x => x.Contains("SWFOC_TRAINER_TELEMETRY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenLuaScriptDoesNotExist()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Combine(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var runner = new LuaHarnessRunner(harnessScript);

        var result = await runner.RunAsync(new LuaHarnessRunRequest(Path.Combine(root, "missing.lua")));

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("lua_script_missing");
    }
}
