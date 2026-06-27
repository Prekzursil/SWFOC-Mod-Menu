using System.Collections;
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.DataIndex.Services;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

public sealed class DataIndexWave10CoverageTests
{
    // ── EffectiveGameDataIndexService: AddLooseEntries whitespace rootPath (line 176) ──
    [Fact]
    public void AddLooseEntries_WhitespaceRoot_ShouldSkip()
    {
        var method = typeof(EffectiveGameDataIndexService).GetMethod("AddLooseEntries",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var diagnostics = new List<string>();
        var records = CreateMutableEntryList();
        var activeIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int rank = 0;

        var args = new object?[] { "   ", "loose", diagnostics, records, activeIndex, rank };
        method!.Invoke(null, args);
        diagnostics.Should().BeEmpty();
        records.Count.Should().Be(0);
    }

    // ── EffectiveGameDataIndexService: AddLooseEntries non-existent directory (line 181-184) ──
    [Fact]
    public void AddLooseEntries_NonExistentDir_ShouldAddDiagnostic()
    {
        var method = typeof(EffectiveGameDataIndexService).GetMethod("AddLooseEntries",
            BindingFlags.NonPublic | BindingFlags.Static);

        var diagnostics = new List<string>();
        var records = CreateMutableEntryList();
        var activeIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int rank = 0;

        var fakePath = Path.Join(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");
        var args = new object?[] { fakePath, "loose", diagnostics, records, activeIndex, rank };
        method!.Invoke(null, args);
        diagnostics.Should().ContainSingle().Which.Should().Contain("does not exist");
    }

    // ── EffectiveGameDataIndexService: ResolveMegaPath rooted path that doesn't exist (line 202-204) ──
    [Fact]
    public void ResolveMegaPath_RootedNonExistent_ReturnsNull()
    {
        var method = typeof(EffectiveGameDataIndexService).GetMethod("ResolveMegaPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var fakePath = Path.Join(Path.GetTempPath(), $"fake_{Guid.NewGuid():N}.meg");
        var result = method!.Invoke(null, new object[] { Path.GetTempPath(), fakePath });
        result.Should().BeNull();
    }

    // ── EffectiveGameDataIndexService: ResolveMegaPath relative, not found anywhere (line 208-219) ──
    [Fact]
    public void ResolveMegaPath_RelativeNonExistent_ReturnsNull()
    {
        var method = typeof(EffectiveGameDataIndexService).GetMethod("ResolveMegaPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        var gameRoot = Path.Join(Path.GetTempPath(), $"game_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gameRoot);
        try
        {
            var result = method!.Invoke(null, new object[] { gameRoot, "nonexistent.meg" });
            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(gameRoot, true);
        }
    }

    // ── EffectiveGameDataIndexService: NormalizePath ──
    [Fact]
    public void NormalizePath_BackslashToForwardSlash()
    {
        var method = typeof(EffectiveGameDataIndexService).GetMethod("NormalizePath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = (string)method!.Invoke(null, new object[] { @"DATA\XML\UNITS.XML" })!;
        result.Should().NotContain("\\");
        result.Should().Contain("/");
    }

    // Create correctly typed list for MutableEffectiveEntry
    private static IList CreateMutableEntryList()
    {
        var entryType = typeof(EffectiveGameDataIndexService).GetNestedType("MutableEffectiveEntry",
            BindingFlags.NonPublic);
        entryType.Should().NotBeNull("MutableEffectiveEntry nested type should exist");
        var listType = typeof(List<>).MakeGenericType(entryType!);
        return (IList)Activator.CreateInstance(listType)!;
    }
}
