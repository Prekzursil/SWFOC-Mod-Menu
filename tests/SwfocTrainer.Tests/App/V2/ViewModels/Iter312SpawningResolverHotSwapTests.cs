using System.Linq;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 312, Thread D arc post-finale 2/2): pin tests for
/// the SpawningTabViewModel.SetIconResolver hot-swap. Validates that
/// changing the resolver mid-session re-resolves IconPaths immediately
/// without requiring an editor restart.
///
/// Drops the iter-310 "restart editor for changes to take effect"
/// requirement. Pinned to the same xUnit collection as iter-307+308
/// because both touch SWFOC_THUMB_CACHE during cache-population tests.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter312SpawningResolverHotSwapTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter312SpawningResolverHotSwapTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter312_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter312_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_ddsRoot);
        Directory.CreateDirectory(_cacheDir);
        _origCacheEnv = Environment.GetEnvironmentVariable("SWFOC_THUMB_CACHE");
        Environment.SetEnvironmentVariable("SWFOC_THUMB_CACHE", _cacheDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SWFOC_THUMB_CACHE", _origCacheEnv);
        try { Directory.Delete(_ddsRoot, recursive: true); } catch { }
        try { Directory.Delete(_cacheDir, recursive: true); } catch { }
    }

    private static (SpawningTabViewModel vm, SwfocSimulator sim) NewVm(
        UnitIconResolver? resolver)
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (new SpawningTabViewModel(adapter, resolver), sim);
    }

    [Fact]
    public void SetIconResolver_FromNullToValid_PopulatesIconPaths()
    {
        // Stage: drop a DDS + cached PNG so the new resolver has something to find.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_Empire_AT_AT.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT", "Rebel_X_Wing" });

        // Initially: no resolver → all IconPaths null.
        vm.FilteredTypeRows.Should().AllSatisfy(r => r.IconPath.Should().BeNull(),
            because: "no resolver wired at construction = all rows have null IconPath");

        // Hot-swap: install the resolver pointing at the staged DDS root.
        vm.SetIconResolver(new UnitIconResolver(_ddsRoot));

        // Now: rows should re-resolve. Empire_AT_AT has DDS+cache → populated.
        var atatRow = vm.FilteredTypeRows.Single(r => r.TypeId == "Empire_AT_AT");
        atatRow.IconPath.Should().Be(cachedPng,
            because: "hot-swap forces RefreshFilteredTypes; Empire_AT_AT resolves to its cached PNG");
        var xwingRow = vm.FilteredTypeRows.Single(r => r.TypeId == "Rebel_X_Wing");
        xwingRow.IconPath.Should().BeNull(
            because: "no DDS for Rebel_X_Wing → resolver returns null gracefully");
    }

    [Fact]
    public void SetIconResolver_FromValidToNull_ClearsIconPaths()
    {
        // Stage with an initial resolver that has a hit.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_Empire_AT_AT.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var (vm, sim) = NewVm(new UnitIconResolver(_ddsRoot)); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT" });
        vm.FilteredTypeRows.Single().IconPath.Should().NotBeNull(
            because: "initial resolver finds the staged DDS+cache hit");

        // Operator clears IconsRoot in Settings → composition root passes null.
        vm.SetIconResolver(null);
        vm.FilteredTypeRows.Single().IconPath.Should().BeNull(
            because: "null resolver = no icons; rows refresh immediately, not at next filter edit");
    }

    [Fact]
    public void SetIconResolver_NewRoot_RebuildsWithNewIconPaths()
    {
        // Stage 2 candidate roots so we can verify the swap actually picks
        // up the NEW root, not just rebuilds with the old one.
        var oldRoot = _ddsRoot;
        var newRoot = Path.Combine(Path.GetTempPath(), $"swfoc_iter312_newroot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(newRoot, "Data", "Art", "Textures", "Units"));
        try
        {
            // Old root has the DDS but cache for new root (different content)
            // gives different SHA → different cache filename. So old-root resolver
            // hits, new-root resolver misses (no cache for the new content).
            var oldDds = Path.Combine(oldRoot, "Data", "Art", "Textures", "Units", "i_button_X.dds");
            Directory.CreateDirectory(Path.GetDirectoryName(oldDds)!);
            File.WriteAllBytes(oldDds, new byte[] { 0x01, 0x02 });
            var oldCachedPng = Path.Combine(_cacheDir,
                ThumbnailCache.ComputeCacheFilename(oldDds, 32));
            File.WriteAllBytes(oldCachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            // New root has different DDS bytes → different SHA → no cache hit yet.
            var newDds = Path.Combine(newRoot, "Data", "Art", "Textures", "Units", "i_button_X.dds");
            File.WriteAllBytes(newDds, new byte[] { 0xAA, 0xBB });

            var (vm, sim) = NewVm(new UnitIconResolver(oldRoot)); using var _ = sim;
            vm.SetAvailableTypes(new[] { "X" });
            vm.FilteredTypeRows.Single().IconPath.Should().Be(oldCachedPng,
                because: "initial old-root resolver finds the seeded cache hit");

            // Hot-swap to the new root which has different bytes + no cache.
            vm.SetIconResolver(new UnitIconResolver(newRoot));
            vm.FilteredTypeRows.Single().IconPath.Should().BeNull(
                because: "new root has DDS but no cached PNG matching its SHA — graceful null");
        }
        finally
        {
            try { Directory.Delete(newRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetIconResolver_PreservesRowCount_AndOrder()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT", "Empire_AT_ST", "Rebel_X_Wing" });
        var initialOrder = vm.FilteredTypeRows.Select(r => r.TypeId).ToList();

        vm.SetIconResolver(new UnitIconResolver(_ddsRoot));

        vm.FilteredTypeRows.Should().HaveCount(3,
            because: "row count is independent of resolver — only IconPaths change on hot-swap");
        vm.FilteredTypeRows.Select(r => r.TypeId).Should().Equal(initialOrder,
            because: "same TypeIds in same order — operator-visible list shouldn't reshuffle on resolver swap");
    }

    [Fact]
    public void SetIconResolver_RespectsCurrentFilter()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT", "Empire_AT_ST", "Rebel_X_Wing" });
        vm.SearchQuery = "Empire";
        vm.FilteredTypeRows.Should().HaveCount(2);

        vm.SetIconResolver(new UnitIconResolver(_ddsRoot));

        vm.FilteredTypeRows.Should().HaveCount(2,
            because: "hot-swap rebuilds rows from current filter result, not from raw available types");
        vm.FilteredTypeRows.Select(r => r.TypeId).Should().NotContain("Rebel_X_Wing",
            because: "search filter still active — Rebel types stay excluded");
    }
}
