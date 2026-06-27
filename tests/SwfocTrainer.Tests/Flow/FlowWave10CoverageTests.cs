using System.Collections;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class FlowWave10CoverageTests
{
    // ── LuaHarnessRunner: ResolveDefaultHarnessScriptPath fallback (line 78) ──
    [Fact]
    public void ResolveDefaultHarnessScriptPath_ShouldReturnPath()
    {
        var method = typeof(LuaHarnessRunner).GetMethod("ResolveDefaultHarnessScriptPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = (string)method!.Invoke(null, null)!;
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("run-lua-harness");
    }

    // ── StoryPlotFlowExtractor: TryParseCapabilities (line 243) ──
    // Method has out parameters, invoke via reflection with object[] args
    [Fact]
    public void TryParseCapabilities_NullCapabilities_ReturnsFalse()
    {
        var method = typeof(StoryPlotFlowExtractor).GetMethod("TryParseCapabilities",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var json = JsonSerializer.Serialize(new { capabilities = default(object?) });
        var args = new object?[] { json, null, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
        var diagnostics = (List<string>)args[2]!;
        diagnostics.Should().Contain(x => x.Contains("missing"));
    }

    [Fact]
    public void TryParseCapabilities_EmptyCapabilities_ReturnsFalse()
    {
        var method = typeof(StoryPlotFlowExtractor).GetMethod("TryParseCapabilities",
            BindingFlags.NonPublic | BindingFlags.Static);

        var json = JsonSerializer.Serialize(new { capabilities = Array.Empty<object>() });
        var args = new object?[] { json, null, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCapabilities_InvalidJson_ReturnsFalse()
    {
        var method = typeof(StoryPlotFlowExtractor).GetMethod("TryParseCapabilities",
            BindingFlags.NonPublic | BindingFlags.Static);

        var args = new object?[] { "not json at all", null, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCapabilities_EmptyString_ReturnsFalse()
    {
        var method = typeof(StoryPlotFlowExtractor).GetMethod("TryParseCapabilities",
            BindingFlags.NonPublic | BindingFlags.Static);

        var args = new object?[] { "   ", null, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
        var diagnostics = (List<string>)args[2]!;
        diagnostics.Should().Contain(x => x.Contains("empty"));
    }

    [Fact]
    public void TryParseCapabilities_ValidCapabilities_ReturnsTrue()
    {
        var method = typeof(StoryPlotFlowExtractor).GetMethod("TryParseCapabilities",
            BindingFlags.NonPublic | BindingFlags.Static);

        var json = JsonSerializer.Serialize(new
        {
            capabilities = new[]
            {
                new { featureId = "credits", available = true, state = "Active", reasonCode = "OK" },
                new { featureId = "", available = false, state = "Disabled", reasonCode = "MISSING" }
            }
        });
        var args = new object?[] { json, null, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        var capabilities = (IDictionary)args[1]!;
        capabilities.Contains("credits").Should().BeTrue();
        capabilities.Contains("").Should().BeFalse(); // blank featureId excluded
    }
}
