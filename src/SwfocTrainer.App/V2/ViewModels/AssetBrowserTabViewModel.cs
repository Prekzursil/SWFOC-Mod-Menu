using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 321, Asset Browser tab kickoff — closes iter-313 honest
/// defer; iter 333 extends to 6 categories adding weapons + abilities).
///
/// Surfaces ALL extracted .dds assets under the operator's IconsRoot in one
/// browsable DataGrid, classified by filename prefix into 6 categories
/// (unit / hero / planet / faction / weapon / ability). Orthogonal to
/// iter-308/317/318/319 per-tab consumer surfaces — this tab is the
/// operator's "what's available across all 6 asset classes" view.
///
/// Phase 1 (iter 321): VM + AssetRow ObservableCollection + RefreshCommand
/// that walks the IconsRoot file system + SetIconResolver hot-swap.
/// Phase 2 (iter 333): extended to 6 categories via longest-prefix-first
/// claim tracking — fixes the pre-existing iter-321 prefix-overlap bug
/// where i_button_* glob over-matched i_button_hp_* + i_button_ability_*
/// files (would have produced ghost duplicate rows under multiple categories).
/// </summary>
public sealed class AssetBrowserTabViewModel : ObservableBase
{
    private static readonly (string Prefix, string Category)[] CategoryPrefixes =
    {
        // 2026-05-07 (iter 333): ORDER MATTERS — longer/more-specific prefixes
        // FIRST so they claim their files before the shorter/superstring prefix
        // (i_button_) does. i_button_hp_ and i_button_ability_ are SUBSETS of
        // i_button_; without longest-prefix-first ordering, the unit-icon walk
        // would over-match hp/ability files and produce ghost duplicate rows.
        // RefreshAssets uses a HashSet<string> to track claimed DDS paths and
        // enforce single-category-per-file (longest prefix wins).
        ("i_button_hp_",      "weapon"),
        ("i_button_ability_", "ability"),
        ("i_button_",         "unit"),
        ("i_portrait_",       "hero"),
        ("i_planet_",         "planet"),
        ("i_faction_",        "faction"),
    };

    private readonly ObservableCollection<AssetRow> _assets = new();
    private UnitIconResolver? _iconResolver;
    private string? _iconsRoot;
    private string _lastStatus = "(idle — click Refresh to scan IconsRoot)";

    public AssetBrowserTabViewModel(
        UnitIconResolver? iconResolver = null,
        string? iconsRoot = null)
    {
        // iter-321: optional resolver + optional root. Both default to null
        // for the dry-construct case (tests + cold-start). MainViewModelV2
        // wires both — resolver drives Resolve* lookups, root drives the
        // file-system walk that populates the browser.
        _iconResolver = iconResolver;
        _iconsRoot = iconsRoot;
        RefreshCommand = new RelayCommand(RefreshAssets);
    }

    /// <summary>
    /// Asset rows bound by the Asset Browser tab DataGrid ItemsSource.
    /// Empty until <see cref="RefreshCommand"/> walks the IconsRoot.
    /// </summary>
    public ObservableCollection<AssetRow> Assets => _assets;

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public ICommand RefreshCommand { get; }

    /// <summary>
    /// Returns the categories the browser knows about (operator-visible
    /// filter list). Pinned tuple for test/regression-guard purposes.
    /// </summary>
    public static IReadOnlyList<string> Categories =>
        CategoryPrefixes.Select(p => p.Category).ToArray();

    /// <summary>
    /// 2026-05-07 (iter 321): hot-swap the icon resolver. Composition root
    /// (MainViewModelV2) calls this when operator changes Settings.IconsRoot
    /// so Asset Browser rows re-resolve immediately. 5th call site after
    /// iter-312 Spawning + iter-317 Galactic + iter-318 HeroLab + iter-319
    /// PlayerState. Pass null to disable icons (clears all IconPaths).
    /// </summary>
    public void SetIconResolver(UnitIconResolver? iconResolver)
    {
        _iconResolver = iconResolver;
        // Re-resolve IconPath for any already-loaded rows. DdsPath is
        // resolver-independent so it stays put. Mirror of iter-318
        // RebuildHeroRows shape with defensive snapshot.
        var snapshot = _assets.ToList();
        _assets.Clear();
        foreach (var row in snapshot)
        {
            var newIconPath = ResolveIconForCategory(row.Category, row.Name);
            _assets.Add(row with { IconPath = newIconPath });
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 321): hot-swap the IconsRoot path used by the
    /// file-system walker. Composition root calls this alongside
    /// SetIconResolver when operator changes Settings.IconsRoot. Operator
    /// must click Refresh to pick up the new root (we don't auto-walk on
    /// SetIconsRoot to avoid surprise UI freezes on large extracted dirs).
    /// </summary>
    public void SetIconsRoot(string? iconsRoot)
    {
        _iconsRoot = iconsRoot;
        LastStatus = string.IsNullOrEmpty(iconsRoot)
            ? "(IconsRoot cleared — click Refresh to clear the asset list)"
            : $"(IconsRoot = {iconsRoot} — click Refresh to scan)";
    }

    /// <summary>
    /// 2026-05-07 (iter 321; iter 333 added longest-prefix-first claim
    /// tracking): walk the operator's IconsRoot directory tree for all 6
    /// i_*_*.dds prefixes; populate <see cref="Assets"/>. Honest scope:
    /// synchronous file-system walk; large extracted dirs (10k+ files) may
    /// briefly freeze the UI. Async-walk + progress reporting deferred to
    /// future iter if the freeze is operator-visible.
    ///
    /// iter-333 fix: HashSet&lt;string&gt; tracks claimed DDS paths so each
    /// file matches exactly ONE category (longest prefix wins). Without this,
    /// an `i_button_hp_X.dds` file would match both the weapon walk AND the
    /// unit walk (because `i_button_*` glob is a superset of `i_button_hp_*`),
    /// producing a ghost "unit" row with name `hp_X` alongside the correct
    /// "weapon" row with name `X`.
    /// </summary>
    private void RefreshAssets()
    {
        _assets.Clear();
        var root = _iconsRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            LastStatus = "(no IconsRoot configured — set it in Settings tab)";
            return;
        }

        var added = 0;
        try
        {
            // iter-333: track claimed DDS paths so each file matches exactly
            // ONE category. CategoryPrefixes is ordered longest-first so the
            // most-specific prefix wins (e.g. weapon claims `i_button_hp_X.dds`
            // before the unit walk sees it).
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (prefix, category) in CategoryPrefixes)
            {
                var matches = Directory.EnumerateFiles(root, $"{prefix}*.dds",
                    SearchOption.AllDirectories);
                foreach (var ddsPath in matches)
                {
                    if (!claimed.Add(ddsPath))
                    {
                        continue; // already claimed by a more-specific prefix
                    }
                    var fileName = Path.GetFileNameWithoutExtension(ddsPath);
                    var name = fileName.Substring(prefix.Length);
                    var iconPath = ResolveIconForCategory(category, name);
                    _assets.Add(new AssetRow(category, name, iconPath, ddsPath));
                    added++;
                }
            }
            LastStatus = $"Scanned {root} → {added} asset(s) loaded.";
        }
        catch (Exception ex)
        {
            LastStatus = $"Refresh failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 321; iter 333 added weapon + ability cases): per-category
    /// resolver delegation. Each category uses the corresponding
    /// iter-313/314/315/331/332 ResolveX method with the per-class default size.
    /// Returns null gracefully when no resolver is wired OR the cache miss path
    /// returns null.
    /// </summary>
    private string? ResolveIconForCategory(string category, string name)
    {
        if (_iconResolver is null) return null;
        return category switch
        {
            "unit" => _iconResolver.Resolve(name),
            "hero" => _iconResolver.ResolvePortrait(name),
            "planet" => _iconResolver.ResolvePlanetIcon(name),
            "faction" => _iconResolver.ResolveFactionEmblem(name),
            "weapon" => _iconResolver.ResolveWeaponIcon(name),
            "ability" => _iconResolver.ResolveAbilityIcon(name),
            _ => null,
        };
    }
}
