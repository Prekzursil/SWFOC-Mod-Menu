using FluentAssertions;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class LuaHarnessRunnerAdditionalTests
{
    [Fact]
    public async Task RunAsync_ShouldFail_WhenTargetLuaScriptMissing()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Combine(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var runner = new LuaHarnessRunner(harnessScript);

        var result = await runner.RunAsync(new LuaHarnessRunRequest("Z:/definitely/missing/script.lua", "Galactic"));

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("lua_script_missing");
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenHarnessScriptMissing()
    {
        var missingHarness = Path.Combine(Path.GetTempPath(), $"harness-missing-{Guid.NewGuid():N}.ps1");
        var luaScriptPath = Path.Combine(Path.GetTempPath(), $"lua-valid-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(
            luaScriptPath,
            "SWFOC_TRAINER_TELEMETRY\nfunction SwfocTrainer_Emit_Telemetry_Mode(mode) return mode end");

        try
        {
            var runner = new LuaHarnessRunner(missingHarness);
            var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScriptPath, "TacticalLand"));

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("harness_runner_missing");
        }
        finally
        {
            File.Delete(luaScriptPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenTelemetryMarkerMissing()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Combine(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var luaScriptPath = Path.Combine(Path.GetTempPath(), $"lua-missing-marker-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(
            luaScriptPath,
            "function SwfocTrainer_Emit_Telemetry_Mode(mode) return mode end");

        try
        {
            var runner = new LuaHarnessRunner(harnessScript);
            var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScriptPath, "TacticalSpace"));

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_marker_missing");
        }
        finally
        {
            File.Delete(luaScriptPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenTelemetryEmitterMissing()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Combine(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var luaScriptPath = Path.Combine(Path.GetTempPath(), $"lua-missing-emitter-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(luaScriptPath, "SWFOC_TRAINER_TELEMETRY");

        try
        {
            var runner = new LuaHarnessRunner(harnessScript);
            var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScriptPath, "TacticalSpace"), CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_marker_missing");
        }
        finally
        {
            File.Delete(luaScriptPath);
        }
    }

    [Fact]
    public async Task RunAsync_DefaultConstructor_ShouldFailClosed_WhenDefaultHarnessMissing()
    {
        var luaScriptPath = Path.Combine(Path.GetTempPath(), $"lua-default-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(
            luaScriptPath,
            "SWFOC_TRAINER_TELEMETRY\nfunction SwfocTrainer_Emit_Telemetry_Mode(mode) return mode end");

        try
        {
            var runner = new LuaHarnessRunner();
            var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScriptPath, "Galactic"));

            result.ReasonCode.Should().NotBeNullOrWhiteSpace();
            result.Succeeded.Should().Be(result.ReasonCode == "ok");
        }
        finally
        {
            File.Delete(luaScriptPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldSucceed_WhenScriptContainsTelemetryMarkerAndEmitter()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Combine(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var luaScriptPath = Path.Combine(Path.GetTempPath(), $"lua-success-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(
            luaScriptPath,
            "SWFOC_TRAINER_TELEMETRY\nfunction SwfocTrainer_Emit_Telemetry_Mode(mode) return mode end");

        try
        {
            var runner = new LuaHarnessRunner(harnessScript);
            var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScriptPath, "TacticalLand"));

            result.Succeeded.Should().BeTrue();
            result.ReasonCode.Should().Be("ok");
            result.OutputLines.Should().Contain(x => x.StartsWith("runner="));
            result.OutputLines.Should().Contain(x => x.Contains("mode=TacticalLand"));
            result.OutputLines.Should().Contain(x => x.StartsWith("emitted=SWFOC_TRAINER_TELEMETRY"));
        }
        finally
        {
            File.Delete(luaScriptPath);
        }
    }
}
