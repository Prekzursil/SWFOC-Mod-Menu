using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 85) — pins the file-export JSON variant. Tests
/// drive <see cref="DiagnosticsTabViewModel.WriteActivityLogJsonToFile"/>
/// directly (the Save-File-Dialog UI seam is not unit-testable). The
/// dialog-driven <c>SaveActivityLogJsonCommand</c> exposure is also
/// pinned so the XAML binding doesn't silently break.
/// </summary>
public sealed class Iter85SaveActivityLogJsonTests : IDisposable
{
    private readonly string _tempFile;

    public Iter85SaveActivityLogJsonTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"swfoc_iter85_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { /* best-effort cleanup */ }
        }
    }

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
    public void SaveJsonCommand_IsExposed()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.SaveActivityLogJsonCommand.Should().NotBeNull();
    }

    [Fact]
    public void WriteJsonToFile_EmptyBuffer_StillWritesEmptyArrayLiteral()
    {
        // The dialog-driven Save handler skips the file when the buffer
        // is empty (operator-friendly), but the WriteActivityLogJsonToFile
        // helper itself is unconditional — testers driving the file-emit
        // path get the same "[]" content as the clipboard variant.
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.WriteActivityLogJsonToFile(_tempFile);

        File.Exists(_tempFile).Should().BeTrue();
        File.ReadAllText(_tempFile).Should().Be("[]");
    }

    [Fact]
    public void WriteJsonToFile_WithEntries_RoundTripsWithJsonDocument()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(Entry("return SWFOC_GodMode(1)", true, "OK", 5));
        adapter.RecordForTest(Entry("return SWFOC_Bad()", false, "ERR", 12));

        vm.WriteActivityLogJsonToFile(_tempFile);

        File.Exists(_tempFile).Should().BeTrue();
        var content = File.ReadAllText(_tempFile);
        var doc = JsonDocument.Parse(content);
        doc.RootElement.GetArrayLength().Should().Be(2);
        // Most-recent first per the ring-buffer convention.
        doc.RootElement[0].GetProperty("luaCommand").GetString().Should().Be("return SWFOC_Bad()");
        doc.RootElement[0].GetProperty("succeeded").GetBoolean().Should().BeFalse();
        doc.RootElement[1].GetProperty("luaCommand").GetString().Should().Be("return SWFOC_GodMode(1)");
    }

    [Fact]
    public void WriteJsonToFile_WritesUtf8WithoutBom()
    {
        // Schema-pin: UTF-8 *without* BOM. BOM-prefixed JSON would break
        // some strict parsers. The TSV save path also uses Encoding.UTF8
        // (which has BOM); we deliberately diverge here for parser
        // compatibility.
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(Entry("return SWFOC_X()", true, "OK", 1));

        vm.WriteActivityLogJsonToFile(_tempFile);

        var bytes = File.ReadAllBytes(_tempFile);
        bytes.Should().NotBeEmpty();
        // UTF-8 BOM is EF BB BF (3 bytes). First byte should NOT be 0xEF.
        bytes[0].Should().NotBe(0xEF, "UTF-8 BOM would break strict JSON parsers");
        // Actual JSON should start with '[' (0x5B).
        bytes[0].Should().Be(0x5B);
    }

    [Fact]
    public void WriteJsonToFile_NullPath_Throws()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        Action act = () => vm.WriteActivityLogJsonToFile(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
