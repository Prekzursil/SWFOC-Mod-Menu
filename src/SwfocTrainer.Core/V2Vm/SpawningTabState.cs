using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 5 (Spawning). Task #149 — searchable mod-aware type browser
/// + position picker + count.
///
/// The "type browser" surface lives in this Core VM as a flat list of
/// available type ids; the App project supplies the list (loaded from
/// the mod's GameObjects.xml or a verified-facts cache) at startup
/// via SetAvailableTypes. The VM filters that list by SearchQuery.
/// </summary>
public sealed class SpawningTabState
{
    private readonly ISpawningDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private List<string> _availableTypes = new();

    public SpawningTabState(ISpawningDispatcher dispatcher, IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    public string SelectedTypeId { get; set; } = string.Empty;
    public int FactionSlot { get; set; } = -1;
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public int Count { get; set; } = 1;
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary>
    /// 2026-04-27: faceted filter — narrows the type browser to a single
    /// faction prefix (e.g. "EMPIRE", "REBEL", "AOTR_REBEL"). Empty = no
    /// filter. Heuristic: type names typically embed the faction string
    /// as a substring (mod conventions vary, but this works for vanilla
    /// + the major mod families).
    /// </summary>
    public string FactionFilter { get; set; } = string.Empty;

    /// <summary>
    /// 2026-04-27: faceted filter — narrows the type browser to "Space",
    /// "Ground", or "Unknown" (anything that doesn't match either).
    /// Empty = no filter. Heuristic-based, see <see cref="ClassifyDomain"/>.
    /// </summary>
    public string DomainFilter { get; set; } = string.Empty;

    /// <summary>
    /// Replace the available-types list (called once at startup from
    /// the mod's GameObjects.xml).
    /// </summary>
    public void SetAvailableTypes(IEnumerable<string> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        _availableTypes = types.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }

    /// <summary>
    /// Returns the current available-types list (used by the App layer to
    /// derive filter dropdown options like the unique faction prefixes).
    /// </summary>
    public IReadOnlyList<string> AvailableTypes => _availableTypes;

    /// <summary>
    /// Filter the available types by SearchQuery + FactionFilter +
    /// DomainFilter, all composed (AND-ed). Empty filters are no-ops.
    /// 2026-04-27: extended from search-only to a faceted filter so the
    /// operator can narrow a 500+ unit catalogue to "Empire ground units"
    /// without scrolling.
    /// </summary>
    public IReadOnlyList<string> FilteredTypes()
    {
        IEnumerable<string> q = _availableTypes;
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var needle = SearchQuery.Trim();
            q = q.Where(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(FactionFilter))
        {
            var faction = FactionFilter.Trim();
            q = q.Where(t => t.Contains(faction, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(DomainFilter))
        {
            var domain = DomainFilter.Trim();
            q = q.Where(t => string.Equals(ClassifyDomain(t), domain, StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    /// <summary>
    /// 2026-04-27: heuristic classifier. Returns "Space" / "Ground" /
    /// "Unknown" based on substring matches in the type id. Mod authors
    /// follow Petroglyph's naming conventions closely enough that this
    /// produces useful results without needing GameObjects.xml metadata.
    /// </summary>
    public static string ClassifyDomain(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return "Unknown";
        // Order matters: check space markers before ground markers because
        // some entries (e.g. "STAR_DESTROYER_SHIP_GUN_TURRET") contain
        // both "SHIP" and "TURRET" — we want the SHIP classification to win.
        foreach (var marker in s_spaceMarkers)
        {
            if (typeId.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return "Space";
            }
        }
        foreach (var marker in s_groundMarkers)
        {
            if (typeId.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return "Ground";
            }
        }
        return "Unknown";
    }

    private static readonly string[] s_spaceMarkers =
    {
        "FRIGATE", "CRUISER", "CORVETTE", "DESTROYER", "BATTLECRUISER",
        "STARFIGHTER", "FIGHTER", "BOMBER", "GUNSHIP", "TRANSPORT_SHIP",
        "STAR_DESTROYER", "STARFIELD", "_SHIP",
        "_FALCON", "_TIE_", "_X_WING", "_Y_WING", "_A_WING", "_B_WING",
        "STARBASE", "SPACE_STATION", "SPACE_",
    };

    private static readonly string[] s_groundMarkers =
    {
        "INFANTRY", "SOLDIER", "TROOPER", "MERCENARY",
        "TANK", "WALKER", "SPEEDER", "AT_AT", "AT_ST", "ATAT", "ATST",
        "BARRACKS", "FACTORY", "STRUCTURE", "TURRET", "BUILDING",
        "VEHICLE", "ARTILLERY", "GROUND_",
    };

    public async Task<UxFeedback> SpawnAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedTypeId))
        {
            return Emit(UxFeedback.Error("spawn_unit", "no type selected", "spawn_unit"));
        }
        if (FactionSlot < 0)
        {
            return Emit(UxFeedback.Error("spawn_unit",
                $"faction slot must be >= 0, got {FactionSlot}", "spawn_unit"));
        }
        if (Count <= 0)
        {
            return Emit(UxFeedback.Error("spawn_unit",
                $"count must be > 0, got {Count}", "spawn_unit"));
        }
        var ok = await _dispatcher.SpawnUnitAsync(
            SelectedTypeId, FactionSlot, PosX, PosY, PosZ, Count, ct);
        return Emit(ok
            ? UxFeedback.Success("spawn_unit",
                $"spawned {Count}× {SelectedTypeId} for slot {FactionSlot} at ({PosX:0.0},{PosY:0.0},{PosZ:0.0})",
                "spawn_unit")
            : UxFeedback.Error("spawn_unit", "bridge rejected", "spawn_unit"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface ISpawningDispatcher
{
    Task<bool> SpawnUnitAsync(string typeId, int slot, float x, float y, float z,
                               int count, CancellationToken ct);
}
