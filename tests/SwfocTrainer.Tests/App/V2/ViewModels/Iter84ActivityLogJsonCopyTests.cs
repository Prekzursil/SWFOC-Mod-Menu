using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using System.Text.Json;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 84) — pins the JSON copy variant of the activity
/// log. Tests ensure (a) empty buffer emits "[]" not null/empty, (b)
/// populated buffer round-trips through System.Text.Json without
/// errors, (c) the field shape is stable (timestamp / succeeded /
/// durationMs / luaCommand / responseOrError) so downstream parsers
/// don't break when fields are added.
/// </summary>
public sealed class Iter84ActivityLogJsonCopyTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, DiagnosticsTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        return (sim, adapter, new DiagnosticsTabViewModel(adapter, settings));
    }

    private static BridgeActivityEntry Entry(string lua, bool ok, string response, long ms) =>
        new BridgeActivityEntry(
            Timestamp: new DateTimeOffset(2026, 4, 28, 10, 30, 45, 123, TimeSpan.Zero),
            LuaCommand: lua,
            Succeeded: ok,
            ResponseOrError: response,
            DurationMs: ms);

    [Fact]
    public void JsonCommand_IsExposed()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.CopyActivityLogJsonCommand.Should().NotBeNull();
    }

    [Fact]
    public void BuildActivityLogJson_EmptyBuffer_ReturnsEmptyArrayLiteral()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.BuildActivityLogJson().Should().Be("[]",
            "downstream parsers expect valid JSON array, not null/empty string");
    }

    [Fact]
    public void BuildActivityLogJson_SingleEntry_RoundTripsThroughDeserialize()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(Entry("return SWFOC_GodMode(1)", true, "OK", 5));

        var json = vm.BuildActivityLogJson();
        var doc = JsonDocument.Parse(json);

        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        var first = doc.RootElement[0];
        first.GetProperty("luaCommand").GetString().Should().Be("return SWFOC_GodMode(1)");
        first.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        first.GetProperty("responseOrError").GetString().Should().Be("OK");
        first.GetProperty("durationMs").GetInt64().Should().Be(5);
        first.GetProperty("timestamp").GetString().Should().Contain("2026-04-28");
    }

    [Fact]
    public void BuildActivityLogJson_MultipleEntries_PreservesOrder()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        // Records are pushed to the front of the ring (most-recent first),
        // so the JSON array should reflect that order.
        adapter.RecordForTest(Entry("return SWFOC_First()", true, "OK1", 1));
        adapter.RecordForTest(Entry("return SWFOC_Second()", true, "OK2", 2));
        adapter.RecordForTest(Entry("return SWFOC_Third()", false, "ERR", 3));

        var json = vm.BuildActivityLogJson();
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetArrayLength().Should().Be(3);
        // Ring buffer: most-recent first → Third, Second, First
        doc.RootElement[0].GetProperty("luaCommand").GetString().Should().Be("return SWFOC_Third()");
        doc.RootElement[0].GetProperty("succeeded").GetBoolean().Should().BeFalse();
        doc.RootElement[1].GetProperty("luaCommand").GetString().Should().Be("return SWFOC_Second()");
        doc.RootElement[2].GetProperty("luaCommand").GetString().Should().Be("return SWFOC_First()");
    }

    [Fact]
    public void BuildActivityLogJson_FieldShape_IsStable()
    {
        // Schema-pin test: catches accidental field renames or removals
        // that would break downstream parsers.
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(Entry("return SWFOC_X()", true, "y", 1));

        var json = vm.BuildActivityLogJson();
        var doc = JsonDocument.Parse(json);
        var first = doc.RootElement[0];

        var propertyNames = new System.Collections.Generic.List<string>();
        foreach (var prop in first.EnumerateObject())
        {
            propertyNames.Add(prop.Name);
        }

        propertyNames.Should().BeEquivalentTo(new[]
        {
            "timestamp", "succeeded", "durationMs", "luaCommand", "responseOrError",
        }, "JSON schema is stable — adding/renaming fields is a breaking change for automation consumers");
    }
}
