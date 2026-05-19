using System.Linq;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.V2Vm;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 308, Thread D arc FINALE): pin tests for the parallel
/// <see cref="SpawningTabViewModel.FilteredTypeRows"/> collection that
/// drives the Spawning tab ListBox ItemTemplate (icon + type name).
///
/// Locks the lock-step contract between FilteredTypes (string list) and
/// FilteredTypeRows (UnitTypeRow list): same length, same TypeId order,
/// IconPath populated by the optional UnitIconResolver.
///
/// xUnit collection pin serializes against Iter308UnitIconResolverTests so
/// the process-wide SWFOC_THUMB_CACHE env var doesn't race between the two
/// classes (both set + restore it per test).
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter308SpawningRowCollectionTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter308SpawningRowCollectionTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_dds_root_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_cache_{Guid.NewGuid():N}");
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
    public void Constructor_WithoutResolver_LeavesRowsEmpty_UntilSetAvailableTypes()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.FilteredTypeRows.Should().BeEmpty(
            because: "no SetAvailableTypes call yet = no rows surfaced");
        vm.FilteredTypes.Should().BeEmpty(
            because: "string collection mirrors row collection (both empty pre-population)");
    }

    [Fact]
    public void SetAvailableTypes_PopulatesBothCollectionsInLockStep()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT", "Rebel_X_Wing", "Underworld_F9TZ" });

        vm.FilteredTypes.Should().HaveCount(3,
            because: "all 3 types pass through with no filter");
        vm.FilteredTypeRows.Should().HaveCount(3,
            because: "row collection mirrors string collection 1:1");
        vm.FilteredTypeRows.Select(r => r.TypeId).Should().Equal(vm.FilteredTypes,
            because: "same order, same content — UI ListBox needs deterministic ordering");
    }

    [Fact]
    public void Rows_Without_Resolver_HaveNullIconPath()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT", "Rebel_X_Wing" });

        vm.FilteredTypeRows.Select(r => r.IconPath)
            .Should().AllBeEquivalentTo<string?>(null,
                because: "no resolver = no IconPath; WPF Image control hidden via null binding");
    }

    [Fact]
    public void Rows_With_Resolver_GetIconPathFromCache()
    {
        // Stage: drop a DDS in the canonical location + a PNG matching its
        // SHA256-keyed cache filename. Resolver should pick this up.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_Empire_AT_AT.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var expectedFilename = ThumbnailCache.ComputeCacheFilename(ddsPath, 32);
        var cachedPng = Path.Combine(_cacheDir, expectedFilename);
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var (vm, sim) = NewVm(resolver); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT", "Rebel_X_Wing" });

        var atatRow = vm.FilteredTypeRows.Single(r => r.TypeId == "Empire_AT_AT");
        atatRow.IconPath.Should().Be(cachedPng,
            because: "DDS exists + cache PNG present => resolver returns cache path");

        var xwingRow = vm.FilteredTypeRows.Single(r => r.TypeId == "Rebel_X_Wing");
        xwingRow.IconPath.Should().BeNull(
            because: "no DDS for Rebel_X_Wing in extracted-DDS root => resolver returns null gracefully");
    }

    [Fact]
    public void SearchQueryChange_RebuildsRows_KeepingLockStep()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.SetAvailableTypes(new[] { "Empire_AT_AT", "Empire_AT_ST", "Rebel_X_Wing" });

        vm.SearchQuery = "Empire";

        vm.FilteredTypes.Should().HaveCount(2,
            because: "filter narrows to 2 Empire types");
        vm.FilteredTypeRows.Should().HaveCount(2,
            because: "row collection MUST stay in lock-step with string collection on filter change");
        vm.FilteredTypeRows.Select(r => r.TypeId).Should().Equal(vm.FilteredTypes,
            because: "same filtered set, same order");
    }

    [Fact]
    public void FactionFilterChange_RebuildsRows()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.SetAvailableTypes(new[]
        {
            "Empire_AT_AT", "Empire_AT_ST", "Rebel_X_Wing",
        });

        // SelectedFactionFilter must be one of the auto-derived options.
        // Empire is the prefix common to 2 of the 3 types.
        vm.SelectedFactionFilter = "Empire";

        vm.FilteredTypeRows.Should().HaveCount(2);
        vm.FilteredTypeRows.Select(r => r.TypeId).Should().Equal(vm.FilteredTypes);
    }
}
