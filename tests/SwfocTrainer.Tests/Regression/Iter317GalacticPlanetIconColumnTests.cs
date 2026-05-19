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
/// 2026-05-07 (iter 317, first UI consumer of iter-315 ResolvePlanetIcon)
/// — pins Galactic-tab planet-icon column shipping. Mirrors the iter-308
/// SpawningTabViewModel pattern + iter-312 SetIconResolver hot-swap behavior.
///
/// Pinned to the same xUnit collection as iter-307+308+312 because the
/// SetIconResolver hot-swap test stages files under SWFOC_THUMB_CACHE and
/// can't run in parallel with other env-var-mutating tests.
///
/// What this iter ships:
///   - NEW PlanetRowWithIcon(PlanetId, OwnerFaction, TechLevel, IconPath?)
///     record (Core/V2Vm — parallel to existing PlanetRow, icon-aware).
///   - GalacticTabViewModel.PlanetRows ObservableCollection (bound by XAML).
///   - GalacticTabViewModel ctor optional UnitIconResolver? param (default
///     null — backward compatible with all existing callers).
///   - GalacticTabViewModel.SetIconResolver(UnitIconResolver?) hot-swap
///     method that rebuilds PlanetRows from the existing Planets list
///     immediately (no need to wait for next bridge-driven RefreshPlanets).
///   - MainViewModelV2 wires the same iconResolver instance through to
///     Galactic + Spawning, and OnSettingsPropertyChanged hot-swaps both.
///   - Galactic XAML DataGrid binds to PlanetRows + adds DataGridTemplateColumn
///     with an Image bound to IconPath.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter317GalacticPlanetIconColumnTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter317GalacticPlanetIconColumnTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter317_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter317_cache_{Guid.NewGuid():N}");
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

    private static (GalacticTabViewModel vm, SwfocSimulator sim) NewVm(
        UnitIconResolver? resolver)
    {
        var sim = new SwfocSimulator(FakeGameState.NewGalacticCampaign());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var unitMutator = new V2UnitMutationDispatcher(adapter);
        return (new GalacticTabViewModel(adapter, unitMutator, resolver), sim);
    }

    [Fact]
    public void PlanetRowWithIcon_RecordShape_PinsFourFields()
    {
        // Pin: record has exactly the 4 expected fields, in the expected
        // order, with the expected types. Catches any future "consolidation"
        // refactor that drifts the shape (e.g. dropping IconPath, or
        // reordering PlanetId/OwnerFaction).
        var row = new PlanetRowWithIcon("Coruscant", "EMPIRE", 5, "/icons/coruscant.png");
        row.PlanetId.Should().Be("Coruscant");
        row.OwnerFaction.Should().Be("EMPIRE");
        row.TechLevel.Should().Be(5);
        row.IconPath.Should().Be("/icons/coruscant.png");

        // IconPath must accept null (no resolver / DDS not extracted).
        var nullIconRow = new PlanetRowWithIcon("Tatooine", "REBEL", 1, null);
        nullIconRow.IconPath.Should().BeNull();
    }

    [Fact]
    public void GalacticTabViewModel_OptionalIconResolverCtor_AcceptsNull()
    {
        // iter-301/308/311 optional-default-null pattern: existing callers
        // that pass only (bridge, unitMutator) keep working. New iconResolver
        // param is optional with default null.
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.Should().NotBeNull();
        vm.PlanetRows.Should().NotBeNull(
            because: "PlanetRows ObservableCollection must exist regardless of resolver wiring");
    }

    [Fact]
    public void GalacticTabViewModel_PlanetRows_IsObservableCollection()
    {
        // Pin: PlanetRows must be ObservableCollection<PlanetRowWithIcon>
        // (NOT IReadOnlyList) so WPF DataGrid auto-refreshes on Add/Clear.
        // Without this binding the icon column wouldn't show new planets
        // until the operator force-rebuilt the DataGrid.
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        vm.PlanetRows.Should().BeOfType<System.Collections.ObjectModel.ObservableCollection<PlanetRowWithIcon>>(
            because: "WPF DataGrid auto-refresh requires ObservableCollection, not IReadOnlyList");
    }

    [Fact]
    public void SetIconResolver_PublicMethod_Exists()
    {
        // Pin: the hot-swap method is public + accepts nullable. Composition
        // root (MainViewModelV2) calls this on Settings.IconsRoot change.
        var t = typeof(GalacticTabViewModel);
        var method = t.GetMethod("SetIconResolver", new[] { typeof(UnitIconResolver) });
        method.Should().NotBeNull(
            because: "MainViewModelV2.OnSettingsPropertyChanged needs Galactic.SetIconResolver to hot-swap");
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void SetIconResolver_FromNullToValid_RebuildsPlanetRows()
    {
        // Stage a planet DDS + cached PNG so the resolver finds something.
        // We use planet-name "TestPlanet9999" so it definitely doesn't match
        // anything the simulator might return — we ONLY want our seed data
        // to drive the test.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_planet_TestPlanet9999.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 96));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var (vm, sim) = NewVm(resolver: null); using var _ = sim;

        SeedPlanets(vm, new PlanetRow("TestPlanet9999", "EMPIRE", 5));
        vm.SetIconResolver(new UnitIconResolver(_ddsRoot));

        vm.PlanetRows.Should().Contain(r => r.PlanetId == "TestPlanet9999",
            because: "the seeded planet must appear in the rebuilt PlanetRows projection");
        var row = vm.PlanetRows.First(r => r.PlanetId == "TestPlanet9999");
        row.OwnerFaction.Should().Be("EMPIRE");
        row.TechLevel.Should().Be(5);
        row.IconPath.Should().Be(cachedPng,
            because: "hot-swap forces RebuildPlanetRows; TestPlanet9999 resolves to its cached PNG via iter-315 ResolvePlanetIcon (default size 96)");
    }

    [Fact]
    public void SetIconResolver_FromValidToNull_ClearsIconPaths()
    {
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_planet_TestPlanet9999.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 96));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var (vm, sim) = NewVm(new UnitIconResolver(_ddsRoot)); using var _ = sim;
        SeedPlanets(vm, new PlanetRow("TestPlanet9999", "REBEL", 1));
        vm.SetIconResolver(new UnitIconResolver(_ddsRoot)); // force rebuild

        var seededRow = vm.PlanetRows.First(r => r.PlanetId == "TestPlanet9999");
        seededRow.IconPath.Should().NotBeNull(
            because: "initial resolver finds the staged DDS+cache hit for TestPlanet9999");

        // Operator clears IconsRoot in Settings → composition root passes null.
        vm.SetIconResolver(null);
        var clearedRow = vm.PlanetRows.First(r => r.PlanetId == "TestPlanet9999");
        clearedRow.IconPath.Should().BeNull(
            because: "null resolver = no icons; rows refresh immediately, not at next planet refresh");
    }

    [Fact]
    public void SetIconResolver_PreservesPlanetMetadata()
    {
        // Pin: hot-swap must not corrupt PlanetId/OwnerFaction/TechLevel.
        // Only IconPath should change. Uses sentinel planet IDs that won't
        // collide with the simulator's planet roster so we can verify our
        // seeded rows survive the hot-swap intact.
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        SeedPlanets(vm,
            new PlanetRow("TestPlanetA9999", "EMPIRE", 5),
            new PlanetRow("TestPlanetB9999", "REBEL", 1),
            new PlanetRow("TestPlanetC9999", "REBEL", 3));

        vm.SetIconResolver(new UnitIconResolver(_ddsRoot));

        var rowA = vm.PlanetRows.First(r => r.PlanetId == "TestPlanetA9999");
        rowA.OwnerFaction.Should().Be("EMPIRE");
        rowA.TechLevel.Should().Be(5);
        var rowB = vm.PlanetRows.First(r => r.PlanetId == "TestPlanetB9999");
        rowB.OwnerFaction.Should().Be("REBEL");
        rowB.TechLevel.Should().Be(1);
        var rowC = vm.PlanetRows.First(r => r.PlanetId == "TestPlanetC9999");
        rowC.OwnerFaction.Should().Be("REBEL");
        rowC.TechLevel.Should().Be(3);
    }

    [Fact]
    public void RebuildPlanetRows_NoResolver_AllIconPathsNull()
    {
        // Pin: when no resolver is wired, every IconPath is null. This is
        // the operator default (no SWFOC_EXTRACTED_DDS_ROOT, no
        // Settings.IconsRoot) — graceful no-icons display.
        var (vm, sim) = NewVm(resolver: null); using var _ = sim;
        SeedPlanets(vm,
            new PlanetRow("Coruscant", "EMPIRE", 5),
            new PlanetRow("Tatooine", "REBEL", 1));
        vm.SetIconResolver(null); // explicit null + force rebuild

        vm.PlanetRows.Should().AllSatisfy(r => r.IconPath.Should().BeNull(),
            because: "no resolver = no icons; null IconPath hides the WPF Image control gracefully");
    }

    [Fact]
    public void MainViewModelV2_WiresResolverThroughToGalactic()
    {
        // Source-level regression guard: ensure the wiring at MainViewModelV2.cs
        // hands the resolver to GalacticTabViewModel ctor. If a future refactor
        // drops the parameter, the source-level check fires immediately
        // (cheaper than instantiating MainViewModelV2's 17-dep constructor).
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("new GalacticTabViewModel(bridge, unitMutator, iconResolver)",
            because: "iter-317 wires the same iconResolver instance to Galactic just like iter-309 wired Spawning");
    }

    [Fact]
    public void MainViewModelV2_SettingsHotSwap_AlsoUpdatesGalactic()
    {
        // Source-level regression guard: ensure OnSettingsPropertyChanged
        // hot-swaps Galactic alongside Spawning. If a future edit drops
        // Galactic.SetIconResolver(...) from this handler, the operator's
        // IconsRoot edit would silently leave the Galactic tab stale until
        // editor restart — a regression in iter-312's "no restart required"
        // contract.
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("Galactic.SetIconResolver(",
            because: "iter-317 extends iter-312 hot-swap to cover Galactic — both tabs flip together when operator changes IconsRoot");
    }

    [Fact]
    public void GalacticXaml_DataGridBindsToPlanetRows_NotPlanets()
    {
        // Source-level regression guard: ensure the XAML DataGrid binds to
        // PlanetRows (the icon-aware projection), not Planets (the original
        // string-keyed list). If a future XAML refactor flips the binding
        // back to Planets, the icon column would appear empty without the
        // PlanetRowWithIcon shape behind it.
        var xamlPath = Path.Combine(EditorRoot(), "src", "SwfocTrainer.App",
            "V2", "MainWindowV2.xaml");
        var xaml = File.ReadAllText(xamlPath);
        xaml.Should().Contain("ItemsSource=\"{Binding PlanetRows}\"",
            because: "iter-317 flips the Galactic DataGrid binding from Planets to PlanetRows");
    }

    [Fact]
    public void GalacticXaml_HasIconColumn_BoundToIconPath()
    {
        // Source-level regression guard: ensure the new icon column exists
        // and binds to IconPath. Catches accidental column-removal refactors.
        var xamlPath = Path.Combine(EditorRoot(), "src", "SwfocTrainer.App",
            "V2", "MainWindowV2.xaml");
        var xaml = File.ReadAllText(xamlPath);
        xaml.Should().Contain("Source=\"{Binding IconPath}\"",
            because: "iter-317 adds an Image column bound to PlanetRowWithIcon.IconPath");
    }

    // -- test helpers --

    /// <summary>
    /// Seeds the VM's _planets field via reflection so tests don't have to
    /// stand up a working SWFOC_GetPlanets simulator handler. Mirrors how
    /// iter-308 SpawningTabViewModelTests use SetAvailableTypes for the same
    /// "bypass the bridge for deterministic test data" purpose.
    /// </summary>
    private static void SeedPlanets(GalacticTabViewModel vm, params PlanetRow[] planets)
    {
        var planetsField = typeof(GalacticTabViewModel).GetField("_planets",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        planetsField.Should().NotBeNull("VM must hold an internal _planets ObservableCollection");
        var collection = (System.Collections.ObjectModel.ObservableCollection<PlanetRow>)planetsField!.GetValue(vm)!;
        collection.Clear();
        foreach (var p in planets) collection.Add(p);
    }

    private static string MainViewModelV2SourcePath() =>
        Path.Combine(EditorRoot(), "src", "SwfocTrainer.App", "V2",
            "ViewModels", "MainViewModelV2.cs");

    /// <summary>
    /// Locates the editor root by walking up from the test assembly's AppContext.BaseDirectory.
    /// Same pattern as other regression tests that read source files for source-level guards.
    /// </summary>
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
