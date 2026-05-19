using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 6 (Galactic Map). Task #150 — planet list + owner change +
/// reveal-all + diplomacy + give-money + switch-sides.
/// </summary>
public sealed class GalacticTabState
{
    private readonly IGalacticDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly FeatureToggleCoordinator _toggles;
    private List<PlanetRow> _planets = new();

    public GalacticTabState(IGalacticDispatcher dispatcher, IUxFeedbackSink feedback,
                             FeatureToggleCoordinator toggles)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(toggles);
        _dispatcher = dispatcher;
        _feedback = feedback;
        _toggles = toggles;
    }

    public IReadOnlyList<PlanetRow> Planets => _planets;
    public string SelectedPlanetId { get; set; } = string.Empty;
    public string NewOwnerFaction { get; set; } = string.Empty;
    public string DiplomacySlotA { get; set; } = string.Empty;
    public string DiplomacySlotB { get; set; } = string.Empty;
    public DiplomacyRelation DiplomacyRelation { get; set; } = DiplomacyRelation.Neutral;

    public async Task<UxFeedback> RefreshPlanetsAsync(CancellationToken ct = default)
    {
        var planets = await _dispatcher.GetPlanetsAsync(ct);
        _planets = planets.ToList();
        return Emit(UxFeedback.Info("get_planets",
            $"loaded {_planets.Count} planets", "get_planets"));
    }

    public async Task<UxFeedback> ChangePlanetOwnerAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedPlanetId))
        {
            return Emit(UxFeedback.Error("change_owner", "no planet selected", "change_owner"));
        }
        if (string.IsNullOrWhiteSpace(NewOwnerFaction))
        {
            return Emit(UxFeedback.Error("change_owner", "no new-owner faction provided",
                "change_owner"));
        }
        var ok = await _dispatcher.ChangePlanetOwnerAsync(SelectedPlanetId, NewOwnerFaction, ct);
        return Emit(ok
            ? UxFeedback.Success("change_owner",
                $"{SelectedPlanetId} → {NewOwnerFaction}", "change_owner")
            : UxFeedback.Error("change_owner", "bridge rejected", "change_owner"));
    }

    /// <summary>
    /// 2026-04-27 (iter 34) — Overlay Feature 2 surfaced through the editor.
    /// Spawn a unit at a galactic-mode planet via the campaign's story-event
    /// fleet-arrival entry point. Inputs come from <see cref="StoryArrivalTypeId"/>
    /// / <see cref="StoryArrivalPlanetId"/> / <see cref="StoryArrivalFaction"/>.
    /// </summary>
    public async Task<UxFeedback> SpawnAsStoryArrivalAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(StoryArrivalTypeId))
        {
            return Emit(UxFeedback.Error("story_arrival",
                "no unit type provided", "story_arrival"));
        }
        if (string.IsNullOrWhiteSpace(StoryArrivalPlanetId))
        {
            return Emit(UxFeedback.Error("story_arrival",
                "no planet provided", "story_arrival"));
        }
        if (string.IsNullOrWhiteSpace(StoryArrivalFaction))
        {
            return Emit(UxFeedback.Error("story_arrival",
                "no faction provided", "story_arrival"));
        }
        var ok = await _dispatcher.SpawnAsStoryArrivalAsync(
            StoryArrivalTypeId, StoryArrivalPlanetId, StoryArrivalFaction, ct);
        return Emit(ok
            ? UxFeedback.Success("story_arrival",
                $"{StoryArrivalTypeId} arrived at {StoryArrivalPlanetId} for {StoryArrivalFaction}",
                "story_arrival")
            : UxFeedback.Error("story_arrival", "bridge rejected", "story_arrival"));
    }

    public string StoryArrivalTypeId { get; set; } = string.Empty;
    public string StoryArrivalPlanetId { get; set; } = string.Empty;
    public string StoryArrivalFaction { get; set; } = string.Empty;

    /// <summary>
    /// 2026-04-27 (iter 33) — Overlay Feature 3 surfaced through the editor.
    /// Lets the operator pick what happens to the garrison when the planet
    /// flips: kick (engine default), convert (re-team), or pure-kick (destroy).
    /// </summary>
    public async Task<UxFeedback> ChangePlanetOwnerWithModeAsync(
        PlanetFlipMode mode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedPlanetId))
        {
            return Emit(UxFeedback.Error("change_owner_mode", "no planet selected", "change_owner_mode"));
        }
        if (string.IsNullOrWhiteSpace(NewOwnerFaction))
        {
            return Emit(UxFeedback.Error("change_owner_mode", "no new-owner faction provided",
                "change_owner_mode"));
        }
        var ok = await _dispatcher.ChangePlanetOwnerWithModeAsync(
            SelectedPlanetId, NewOwnerFaction, mode, ct);
        var modeLabel = mode switch
        {
            PlanetFlipMode.Convert => "convert",
            PlanetFlipMode.PureKick => "pure-kick",
            _ => "default",
        };
        return Emit(ok
            ? UxFeedback.Success("change_owner_mode",
                $"{SelectedPlanetId} → {NewOwnerFaction} ({modeLabel})", "change_owner_mode")
            : UxFeedback.Error("change_owner_mode", "bridge rejected", "change_owner_mode"));
    }

    public Task<UxFeedback> ToggleRevealAllAsync(bool enable, CancellationToken ct = default)
    {
        return _toggles.ToggleAsync("reveal_all", enable,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetRevealAllAsync(enable, cancel);
                return ok
                    ? UxFeedback.Success("reveal_all",
                        enable ? "fog disabled (galactic + tactical)" : "fog restored",
                        "reveal_all")
                    : UxFeedback.Error("reveal_all", "bridge rejected", "reveal_all");
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetRevealAllAsync(false, cancel);
                    return ok
                        ? UxFeedback.Info("reveal_all", "fog restored (cleanup)", "reveal_all")
                        : UxFeedback.Warning("reveal_all", "cleanup-disable failed", "reveal_all");
                }
        : null,
            cancellationToken: ct);
    }

    public async Task<UxFeedback> SetDiplomacyAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(DiplomacySlotA) || string.IsNullOrWhiteSpace(DiplomacySlotB))
        {
            return Emit(UxFeedback.Error("set_diplomacy",
                "both faction sides must be specified", "set_diplomacy"));
        }
        if (DiplomacyRelation == DiplomacyRelation.Neutral)
        {
            return Emit(UxFeedback.Warning("set_diplomacy",
                "engine API does not support setting Neutral — pick Allied or Hostile",
                "set_diplomacy"));
        }
        var ok = await _dispatcher.SetDiplomacyAsync(DiplomacySlotA, DiplomacySlotB, DiplomacyRelation, ct);
        return Emit(ok
            ? UxFeedback.Success("set_diplomacy",
                $"{DiplomacySlotA} ↔ {DiplomacySlotB} → {DiplomacyRelation}", "set_diplomacy")
            : UxFeedback.Error("set_diplomacy", "bridge rejected", "set_diplomacy"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public sealed record PlanetRow(string PlanetId, string OwnerFaction, int TechLevel);

public enum DiplomacyRelation
{
    Neutral = 0,
    Allied,
    Hostile
}

/// <summary>
/// 2026-04-27 (iter 33) — operator's choice for what happens to garrison
/// units when a planet's owner flips. Mirrors the overlay design's
/// Feature 3 modes 1:1.
/// </summary>
public enum PlanetFlipMode
{
    /// <summary>Engine kick-out queue: foreign units leave the planet but stay alive.</summary>
    Default,
    /// <summary>Re-team via per-unit Switch_Sides: garrison stays, owner flips.</summary>
    Convert,
    /// <summary>Destroy: foreign units removed from the world entirely.</summary>
    PureKick,
}

public interface IGalacticDispatcher
{
    Task<IReadOnlyList<PlanetRow>> GetPlanetsAsync(CancellationToken ct);
    Task<bool> ChangePlanetOwnerAsync(string planetId, string newOwner, CancellationToken ct);

    /// <summary>
    /// 2026-04-27 (iter 33) — Overlay Feature 3 surfaced through the editor.
    /// Routes through <c>SWFOC_ChangePlanetOwnerWithMode(planet, faction, mode)</c>
    /// so the operator can pick what happens to the garrison.
    /// </summary>
    Task<bool> ChangePlanetOwnerWithModeAsync(
        string planetId, string newOwner, PlanetFlipMode mode, CancellationToken ct);

    /// <summary>
    /// 2026-04-27 (iter 34) — Overlay Feature 2 surfaced through the editor.
    /// Spawn a unit on a galactic-mode planet via the engine's "fleet
    /// arrival" / story-event entry point so the unit integrates into
    /// galactic state cleanly. Wire format:
    /// <c>SWFOC_SpawnAsStoryArrival('type', 'planet', 'faction')</c>.
    /// </summary>
    Task<bool> SpawnAsStoryArrivalAsync(
        string typeId, string planetId, string faction, CancellationToken ct);

    Task<bool> SetRevealAllAsync(bool enable, CancellationToken ct);
    Task<bool> SetDiplomacyAsync(string a, string b, DiplomacyRelation rel, CancellationToken ct);
}
