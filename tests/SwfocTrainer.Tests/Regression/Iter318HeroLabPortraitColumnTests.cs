using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.V2Vm;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 318, second UI consumer of iter-313 ResolvePortrait —
/// follows iter-317 Galactic planet-icon column pattern verbatim) — pins
/// Hero Lab tab portrait column shipping. Mirrors iter-317 + iter-312
/// SetIconResolver hot-swap behavior.
///
/// Pinned to the same xUnit collection as iter-307+308+312+317 because the
/// SetIconResolver hot-swap test stages files under SWFOC_THUMB_CACHE.
///
/// What this iter ships:
///   - NEW HeroRowWithPortrait(ObjAddr, TypeName, OwnerSlot, Alive,
///     RespawnRemainingMs, RespawnEnabled, IconPath?) record (Core/V2Vm —
///     parallel to existing HeroRow, icon-aware).
///   - HeroLabTabViewModel.HeroRows ObservableCollection (bound by XAML).
///   - HeroLabTabViewModel ctor optional UnitIconResolver? param (default
///     null — backward compatible with all existing callers).
///   - HeroLabTabViewModel.SetIconResolver(UnitIconResolver?) hot-swap
///     method that rebuilds HeroRows from the existing Heroes list
///     immediately (no need to wait for next bridge-driven RefreshHeroes).
///   - MainViewModelV2 wires the same iconResolver instance through to
///     all 3 tabs (Spawning + Galactic + HeroLab), and OnSettingsPropertyChanged
///     hot-swaps all 3.
///   - HeroLab XAML DataGrid binds to HeroRows + adds DataGridTemplateColumn
///     with an Image bound to IconPath (Width/Height 64 — larger than iter-308
///     unit icons (32) and iter-317 planet icons (96 actually) since portraits
///     are typically rendered at hero-photo scale).
///   - HeroRowWithPortrait.RespawnRemainingDisplay mirror of HeroRow's
///     computed property — keeps existing XAML binding resolving cleanly
///     after the ItemsSource flip.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter318HeroLabPortraitColumnTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter318HeroLabPortraitColumnTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter318_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter318_cache_{Guid.NewGuid():N}");
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

    private static (HeroLabTabViewModel vm, SwfocSimulator sim) NewVm(
        UnitIconResolver? resolver)
    {
        var (vm, _, sim) = NewVmWithAdapter(resolver);
        return (vm, sim);
    }

    private static (HeroLabTabViewModel vm, V2BridgeAdapter adapter, SwfocSimulator sim) NewVmWithAdapter(
        UnitIconResolver? resolver)
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (new HeroLabTabViewModel(adapter, resolver), adapter, sim);
    }

    /// <summary>
    /// Wait up to <paramref name="timeoutMs"/> ms for the VM's ctor-time async
    /// RefreshHeroes to land in <paramref name="adapter"/>.RecentCalls. Replaces
    /// fixed Task.Delay(200) — same shape as the iter80 composite-test fix that
    /// flaked under stress.
    /// </summary>
    private static async Task WaitForCtorRefresh(V2BridgeAdapter adapter, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && adapter.RecentCalls.Count == 0)
        {
            await Task.Delay(20);
        }
        await Task.Delay(50);
    }

    private static void SeedHeroes(HeroLabTabViewModel vm, params HeroRow[] heroes)
    {
        var heroesField = typeof(HeroLabTabViewModel).GetField("_heroes",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        heroesField.Should().NotBeNull("VM must hold an internal _heroes ObservableCollection");
        var collection = (System.Collections.ObjectModel.ObservableCollection<HeroRow>)heroesField!.GetValue(vm)!;
        collection.Clear();
        foreach (var h in heroes) collection.Add(h);
    }

    [Fact]
    public void HeroRowWithPortrait_RecordShape_PinsSevenFields()
    {
        // Pin: record has exactly 7 expected fields (HeroRow's 6 + IconPath).
        var row = new HeroRowWithPortrait(
            ObjAddr: 0xDEADBEEF, TypeName: "Han_Solo", OwnerSlot: 1,
            Alive: true, RespawnRemainingMs: 5000, RespawnEnabled: true,
            IconPath: "/icons/han.png");
        row.ObjAddr.Should().Be(0xDEADBEEF);
        row.TypeName.Should().Be("Han_Solo");
        row.OwnerSlot.Should().Be(1);
        row.Alive.Should().BeTrue();
        row.RespawnRemainingMs.Should().Be(5000);
        row.RespawnEnabled.Should().BeTrue();
        row.IconPath.Should().Be("/icons/han.png");

        // IconPath must accept null (no resolver / DDS not extracted).
        var nullIconRow = new HeroRowWithPortrait(0, "Luke", 0, false, 0, false, null);
        nullIconRow.IconPath.Should().BeNull();
    }

    [Fact]
    public void HeroRowWithPortrait_RespawnRemainingDisplay_MirrorsHeroRow()
    {
        // Pin: computed property returns the same operator-visible string as
        // HeroRow.RespawnRemainingDisplay across the canonical inputs. If
        // either side drifts (em-dash, ms boundary, "5.0 sec" format), the
        // other catches it. Existing XAML binds to RespawnRemainingDisplay
        // by name so this test guards the binding contract.
        var disabled = new HeroRowWithPortrait(0, "x", 0, false, 5000, false, null);
        disabled.RespawnRemainingDisplay.Should().Be("—",
            because: "RespawnEnabled=false renders em-dash regardless of ms value");

        var instant = new HeroRowWithPortrait(0, "x", 0, false, 0, true, null);
        instant.RespawnRemainingDisplay.Should().Be("0 ms");

        var subSecond = new HeroRowWithPortrait(0, "x", 0, false, 250, true, null);
        subSecond.RespawnRemainingDisplay.Should().Be("250 ms");

        var seconds = new HeroRowWithPortrait(0, "x", 0, false, 5000, true, null);
        seconds.RespawnRemainingDisplay.Should().Be("5.0 sec");

        var minutesExact = new HeroRowWithPortrait(0, "x", 0, false, 60_000, true, null);
        minutesExact.RespawnRemainingDisplay.Should().Be("1 min");

        var minutesAndSec = new HeroRowWithPortrait(0, "x", 0, false, 90_000, true, null);
        minutesAndSec.RespawnRemainingDisplay.Should().Be("1 min 30 sec");
    }

    [Fact]
    public void HeroLabTabViewModel_OptionalIconResolverCtor_AcceptsNull()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.Should().NotBeNull();
        vm.HeroRows.Should().NotBeNull(
            because: "HeroRows ObservableCollection must exist regardless of resolver wiring");
    }

    [Fact]
    public void HeroLabTabViewModel_HeroRows_IsObservableCollection()
    {
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.HeroRows.Should().BeOfType<System.Collections.ObjectModel.ObservableCollection<HeroRowWithPortrait>>(
            because: "WPF DataGrid auto-refresh requires ObservableCollection");
    }

    [Fact]
    public void SetIconResolver_PublicMethod_Exists()
    {
        var t = typeof(HeroLabTabViewModel);
        var method = t.GetMethod("SetIconResolver", new[] { typeof(UnitIconResolver) });
        method.Should().NotBeNull(
            because: "MainViewModelV2.OnSettingsPropertyChanged needs HeroLab.SetIconResolver to hot-swap");
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public async Task SetIconResolver_FromNullToValid_RebuildsHeroRows()
    {
        // Stage a hero portrait DDS + cached PNG. Use sentinel hero name
        // that won't collide with simulator's hero roster.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_portrait_TestHero9999.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 64));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var (vm, adapter, sim) = NewVmWithAdapter(resolver: null); using var _ = sim;
        await WaitForCtorRefresh(adapter);

        SeedHeroes(vm, new HeroRow(0xCAFE, "TestHero9999", 1, true, 5000, true));
        vm.SetIconResolver(new UnitIconResolver(_ddsRoot));

        vm.HeroRows.Should().Contain(r => r.TypeName == "TestHero9999",
            because: "the seeded hero must appear in the rebuilt HeroRows projection");
        var row = vm.HeroRows.First(r => r.TypeName == "TestHero9999");
        row.ObjAddr.Should().Be(0xCAFE);
        row.OwnerSlot.Should().Be(1);
        row.Alive.Should().BeTrue();
        row.RespawnRemainingMs.Should().Be(5000);
        row.RespawnEnabled.Should().BeTrue();
        row.IconPath.Should().Be(cachedPng,
            because: "hot-swap forces RebuildHeroRows; TestHero9999 resolves to its cached PNG via iter-313 ResolvePortrait (default size 64)");
    }

    [Fact]
    public async Task SetIconResolver_FromValidToNull_ClearsIconPaths()
    {
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_portrait_TestHero9999.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 64));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var (vm, adapter, sim) = NewVmWithAdapter(new UnitIconResolver(_ddsRoot)); using var _ = sim;
        await WaitForCtorRefresh(adapter);
        SeedHeroes(vm, new HeroRow(1, "TestHero9999", 1, true, 5000, true));
        vm.SetIconResolver(new UnitIconResolver(_ddsRoot)); // force rebuild

        var seededRow = vm.HeroRows.First(r => r.TypeName == "TestHero9999");
        seededRow.IconPath.Should().NotBeNull(
            because: "initial resolver finds the staged DDS+cache hit");

        vm.SetIconResolver(null);
        var clearedRow = vm.HeroRows.First(r => r.TypeName == "TestHero9999");
        clearedRow.IconPath.Should().BeNull(
            because: "null resolver = no icons; rows refresh immediately");
    }

    [Fact]
    public async Task SetIconResolver_PreservesHeroMetadata()
    {
        var (vm, adapter, sim) = NewVmWithAdapter(resolver: null); using var _ = sim;
        await WaitForCtorRefresh(adapter);
        SeedHeroes(vm,
            new HeroRow(0xA, "TestHeroA9999", 1, true, 1000, true),
            new HeroRow(0xB, "TestHeroB9999", 2, false, 5000, true),
            new HeroRow(0xC, "TestHeroC9999", 0, true, 0, false));

        vm.SetIconResolver(new UnitIconResolver(_ddsRoot));

        var rowA = vm.HeroRows.First(r => r.TypeName == "TestHeroA9999");
        rowA.ObjAddr.Should().Be(0xA);
        rowA.OwnerSlot.Should().Be(1);
        rowA.RespawnRemainingMs.Should().Be(1000);
        var rowB = vm.HeroRows.First(r => r.TypeName == "TestHeroB9999");
        rowB.Alive.Should().BeFalse();
        rowB.OwnerSlot.Should().Be(2);
        var rowC = vm.HeroRows.First(r => r.TypeName == "TestHeroC9999");
        rowC.RespawnEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task RebuildHeroRows_NoResolver_AllIconPathsNull()
    {
        var (vm, adapter, sim) = NewVmWithAdapter(resolver: null); using var _ = sim;
        await WaitForCtorRefresh(adapter);
        SeedHeroes(vm,
            new HeroRow(0x1, "TestHeroA9999", 1, true, 5000, true),
            new HeroRow(0x2, "TestHeroB9999", 2, true, 5000, true));
        vm.SetIconResolver(null); // explicit null + force rebuild

        var seeded = vm.HeroRows.Where(r => r.TypeName.StartsWith("TestHero", StringComparison.Ordinal)).ToList();
        seeded.Should().AllSatisfy(r => r.IconPath.Should().BeNull(),
            because: "no resolver = no icons; null IconPath hides the WPF Image control gracefully");
    }

    [Fact]
    public void MainViewModelV2_WiresResolverThroughToHeroLab()
    {
        // Source-level regression guard for iter-318 wiring.
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("new HeroLabTabViewModel(bridge, iconResolver)",
            because: "iter-318 wires the same iconResolver to HeroLab just like iter-309 wired Spawning + iter-317 wired Galactic");
    }

    [Fact]
    public void MainViewModelV2_SettingsHotSwap_AlsoUpdatesHeroLab()
    {
        // Source-level regression guard: ensure OnSettingsPropertyChanged
        // hot-swaps HeroLab alongside Spawning + Galactic.
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("HeroLab.SetIconResolver(",
            because: "iter-318 extends iter-312/iter-317 hot-swap to cover HeroLab — all 3 tabs flip together when operator changes IconsRoot");
    }

    [Fact]
    public void HeroLabXaml_DataGridBindsToHeroRows_NotHeroes()
    {
        var xamlPath = Path.Combine(EditorRoot(), "src", "SwfocTrainer.App",
            "V2", "MainWindowV2.xaml");
        var xaml = File.ReadAllText(xamlPath);
        xaml.Should().Contain("ItemsSource=\"{Binding HeroRows}\"",
            because: "iter-318 flips the HeroLab DataGrid binding from Heroes to HeroRows");
    }

    [Fact]
    public void HeroLabXaml_HasPortraitColumn_BoundToIconPath()
    {
        var xamlPath = Path.Combine(EditorRoot(), "src", "SwfocTrainer.App",
            "V2", "MainWindowV2.xaml");
        var xaml = File.ReadAllText(xamlPath);
        // iter-317 Galactic also added a `Source="{Binding IconPath}"` column;
        // iter-318 adds a sibling for HeroLab. We want at least 2 occurrences
        // (one per tab) so the count assertion catches a future XAML refactor
        // that accidentally drops the HeroLab Image.
        var occurrences = System.Text.RegularExpressions.Regex.Matches(xaml,
            "Source=\"\\{Binding IconPath\\}\"").Count;
        occurrences.Should().BeGreaterThanOrEqualTo(2,
            because: "iter-317 added one Image column (Galactic), iter-318 adds another (HeroLab); both must exist");
    }

    private static string MainViewModelV2SourcePath() =>
        Path.Combine(EditorRoot(), "src", "SwfocTrainer.App", "V2",
            "ViewModels", "MainViewModelV2.cs");

    private static string EditorRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "SwfocTrainer.App")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException(
            "Could not locate editor root with src/SwfocTrainer.App/ from " + AppContext.BaseDirectory);
    }
}
