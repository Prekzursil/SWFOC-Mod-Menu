using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 321, Asset Browser tab kickoff — closes iter-313 honest
/// defer; last UI consumer surface in the Thread D arc).
///
/// Phase 1 scope: VM + AssetRow record + RefreshCommand + SetIconResolver +
/// SetIconsRoot hot-swap + per-category resolver delegation. Tests focus on
/// the file-system walker behavior + record shape + composition root wiring
/// (5th tab in the iter-308/317/318/319 hot-swap chain).
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter321AssetBrowserTabTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter321AssetBrowserTabTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter321_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter321_cache_{Guid.NewGuid():N}");
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

    [Fact]
    public void AssetRow_RecordShape_PinsFourFields()
    {
        var row = new AssetRow("unit", "Empire_AT_AT", "/cache/atat.png", "/dds/i_button_Empire_AT_AT.dds");
        row.Category.Should().Be("unit");
        row.Name.Should().Be("Empire_AT_AT");
        row.IconPath.Should().Be("/cache/atat.png");
        row.DdsPath.Should().Be("/dds/i_button_Empire_AT_AT.dds");

        var nullRow = new AssetRow("hero", "Han", null, null);
        nullRow.IconPath.Should().BeNull();
        nullRow.DdsPath.Should().BeNull();
    }

    [Fact]
    public void AssetBrowserTabViewModel_OptionalCtorArgs_AcceptNull()
    {
        var vm = new AssetBrowserTabViewModel();
        vm.Assets.Should().NotBeNull();
        vm.Assets.Should().BeEmpty(
            because: "no IconsRoot configured at construction = no walk = empty Assets");
        vm.RefreshCommand.Should().NotBeNull(
            because: "RefreshCommand is always present even when no resolver/root wired");
    }

    [Fact]
    public void AssetBrowserTabViewModel_Categories_ListsSixClasses()
    {
        // Pin: the 6 categories the browser knows about. Mirrors the iter-308
        // (unit) + iter-313 (hero) + iter-314 (faction) + iter-315 (planet) +
        // iter-331 (weapon) + iter-332 (ability) resolver lineup.
        // iter-333: extended from 4 to 6 categories.
        AssetBrowserTabViewModel.Categories.Should().BeEquivalentTo(
            new[] { "unit", "hero", "planet", "faction", "weapon", "ability" },
            because: "iter-333 extended the iter-321 surface to all 6 asset classes that have iter-313/314/315/331/332 resolvers");
    }

    [Fact]
    public void RefreshCommand_NoRoot_SetsStatusMessage()
    {
        var vm = new AssetBrowserTabViewModel(iconResolver: null, iconsRoot: null);
        vm.RefreshCommand.Execute(null);
        vm.LastStatus.Should().Contain("no IconsRoot configured",
            because: "RefreshCommand without configured root must surface a clear operator-facing message");
        vm.Assets.Should().BeEmpty();
    }

    [Fact]
    public void RefreshCommand_RootPresent_PopulatesAssetsForAllSixCategories()
    {
        // Stage one DDS per category in the temp DDS root.
        // iter-333: extended from 4 to 6 categories adding weapons + abilities.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        File.WriteAllBytes(Path.Combine(ddsDir, "i_button_Empire_AT_AT.dds"), new byte[] { 0xCA, 0xFE });
        File.WriteAllBytes(Path.Combine(ddsDir, "i_portrait_Han_Solo.dds"), new byte[] { 0xCA, 0xFE });
        File.WriteAllBytes(Path.Combine(ddsDir, "i_planet_Coruscant.dds"), new byte[] { 0xCA, 0xFE });
        File.WriteAllBytes(Path.Combine(ddsDir, "i_faction_EMPIRE.dds"), new byte[] { 0xCA, 0xFE });
        File.WriteAllBytes(Path.Combine(ddsDir, "i_button_hp_TIE_Laser.dds"), new byte[] { 0xCA, 0xFE });
        File.WriteAllBytes(Path.Combine(ddsDir, "i_button_ability_Force_Push.dds"), new byte[] { 0xCA, 0xFE });

        var vm = new AssetBrowserTabViewModel(
            iconResolver: new UnitIconResolver(_ddsRoot),
            iconsRoot: _ddsRoot);
        vm.RefreshCommand.Execute(null);

        vm.Assets.Should().HaveCount(6,
            because: "one DDS per category staged → 6 rows expected (iter-333 extends from 4 to 6)");
        vm.Assets.Select(a => a.Category).Should().BeEquivalentTo(
            new[] { "unit", "hero", "planet", "faction", "weapon", "ability" });
        vm.Assets.Should().Contain(a => a.Name == "Empire_AT_AT" && a.Category == "unit");
        vm.Assets.Should().Contain(a => a.Name == "Han_Solo" && a.Category == "hero");
        vm.Assets.Should().Contain(a => a.Name == "Coruscant" && a.Category == "planet");
        vm.Assets.Should().Contain(a => a.Name == "EMPIRE" && a.Category == "faction");
        vm.Assets.Should().Contain(a => a.Name == "TIE_Laser" && a.Category == "weapon");
        vm.Assets.Should().Contain(a => a.Name == "Force_Push" && a.Category == "ability");
        vm.LastStatus.Should().Contain("6 asset(s) loaded");
    }

    [Fact]
    public void RefreshCommand_LongestPrefixWins_NoGhostUnitRowFromHpFile()
    {
        // iter-333 regression guard: the i_button_* glob pattern matches BOTH
        // i_button_X.dds AND i_button_hp_X.dds AND i_button_ability_X.dds. The
        // pre-iter-333 implementation would create 3 rows for an i_button_hp_X
        // file (one per matching glob). iter-333 longest-prefix-first ordering
        // + HashSet claim tracking ensures each file matches EXACTLY ONE category.
        // This test would have FAILED on the pre-iter-333 implementation.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        File.WriteAllBytes(Path.Combine(ddsDir, "i_button_hp_TIE_Laser.dds"), new byte[] { 0xCA });
        File.WriteAllBytes(Path.Combine(ddsDir, "i_button_ability_Force_Push.dds"), new byte[] { 0xCA });

        var vm = new AssetBrowserTabViewModel(
            iconResolver: new UnitIconResolver(_ddsRoot),
            iconsRoot: _ddsRoot);
        vm.RefreshCommand.Execute(null);

        // Each file must produce exactly ONE row, in the most-specific category.
        vm.Assets.Should().HaveCount(2,
            because: "longest-prefix-first claim tracking ensures each DDS file matches exactly ONE category");
        vm.Assets.Should().Contain(a => a.Category == "weapon" && a.Name == "TIE_Laser");
        vm.Assets.Should().Contain(a => a.Category == "ability" && a.Name == "Force_Push");

        // CRITICAL: NO ghost unit-category rows with names "hp_TIE_Laser" or "ability_Force_Push".
        vm.Assets.Should().NotContain(a => a.Category == "unit" && a.Name == "hp_TIE_Laser",
            because: "i_button_hp_X.dds must claim the weapon category and NOT also surface as a ghost unit row with name `hp_X`");
        vm.Assets.Should().NotContain(a => a.Category == "unit" && a.Name == "ability_Force_Push",
            because: "i_button_ability_X.dds must claim the ability category and NOT also surface as a ghost unit row with name `ability_X`");
    }

    [Fact]
    public void RefreshCommand_ResolvesIconPathPerCategory()
    {
        // Stage DDS + matching cache PNG for the unit category — pins that
        // the per-category resolver delegation (Resolve vs ResolvePortrait
        // vs ResolvePlanetIcon vs ResolveFactionEmblem) actually fires.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_TestUnit9999.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachePath = Path.Combine(_cacheDir, ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cachePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var vm = new AssetBrowserTabViewModel(
            iconResolver: new UnitIconResolver(_ddsRoot),
            iconsRoot: _ddsRoot);
        vm.RefreshCommand.Execute(null);

        var unitRow = vm.Assets.First(a => a.Name == "TestUnit9999");
        unitRow.Category.Should().Be("unit");
        unitRow.IconPath.Should().Be(cachePath,
            because: "iter-308 Resolve uses size=32; matching cache PNG must resolve");
        unitRow.DdsPath.Should().Be(ddsPath);
    }

    [Fact]
    public void SetIconResolver_HotSwap_ReResolvesExistingRows()
    {
        // Stage 1 unit DDS + matching cache.
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_TestUnit9999.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachePath = Path.Combine(_cacheDir, ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cachePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        // Construct + refresh with the working resolver.
        var vm = new AssetBrowserTabViewModel(
            iconResolver: new UnitIconResolver(_ddsRoot),
            iconsRoot: _ddsRoot);
        vm.RefreshCommand.Execute(null);
        vm.Assets.First().IconPath.Should().NotBeNull(
            because: "initial resolver finds the staged DDS+cache hit");

        // Swap to null resolver — should clear all IconPaths but keep DdsPaths.
        vm.SetIconResolver(null);
        vm.Assets.First().IconPath.Should().BeNull(
            because: "null resolver = no icons; rows refresh in-place");
        vm.Assets.First().DdsPath.Should().NotBeNull(
            because: "DdsPath is resolver-independent; survives the swap");
    }

    [Fact]
    public void SetIconsRoot_UpdatesRootAndStatus()
    {
        var vm = new AssetBrowserTabViewModel();
        vm.SetIconsRoot("/some/new/root");
        vm.LastStatus.Should().Contain("/some/new/root");

        vm.SetIconsRoot(null);
        vm.LastStatus.Should().Contain("cleared");
    }

    [Fact]
    public void MainViewModelV2_WiresAssetBrowser()
    {
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("new AssetBrowserTabViewModel(iconResolver, ResolveIconsRoot(settings))",
            because: "iter-321 wires AssetBrowser as 5th icon-consumer tab");
    }

    [Fact]
    public void MainViewModelV2_SettingsHotSwap_AlsoUpdatesAssetBrowser()
    {
        var src = File.ReadAllText(MainViewModelV2SourcePath());
        src.Should().Contain("AssetBrowser.SetIconResolver(",
            because: "iter-321 extends iter-312/iter-317/iter-318/iter-319 hot-swap to cover AssetBrowser");
        src.Should().Contain("AssetBrowser.SetIconsRoot(",
            because: "iter-321 also propagates the root path so the file-system walker uses the new root");
    }

    [Fact]
    public void AssetBrowserXaml_DataGridBindsToAssets()
    {
        var xamlPath = Path.Combine(EditorRoot(), "src", "SwfocTrainer.App",
            "V2", "MainWindowV2.xaml");
        var xaml = File.ReadAllText(xamlPath);
        xaml.Should().Contain("ItemsSource=\"{Binding Assets}\"",
            because: "iter-321 binds the Asset Browser DataGrid to the Assets ObservableCollection");
        xaml.Should().Contain("Asset Browser",
            because: "iter-321 added a TabItem with Header='Asset Browser'");
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
