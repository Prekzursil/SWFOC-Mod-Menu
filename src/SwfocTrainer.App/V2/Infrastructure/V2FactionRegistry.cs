using System.Collections.ObjectModel;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27: shared, live-merged faction-name registry. Replaces the
/// per-tab hardcoded <c>{"REBEL","EMPIRE","UNDERWORLD"}</c> lists in
/// <see cref="ViewModels.UnitControlTabViewModel"/>,
/// <see cref="ViewModels.WorldStateTabViewModel"/>,
/// <see cref="ViewModels.GalacticTabViewModel"/>,
/// <see cref="ViewModels.EconomyTabViewModel"/>, etc.
/// </summary>
/// <remarks>
/// <para>
/// The auth source is the live game's <c>SWFOC_GetAllPlayers</c> response,
/// which <see cref="ViewModels.PlayerStateTabViewModel.RefreshSlotMapAsync"/>
/// already parses. That call now also feeds this registry via
/// <see cref="MergeFactions"/>; every other tab that needs a faction
/// dropdown binds to the same <see cref="Factions"/> collection.
/// </para>
/// <para>
/// We seed the registry with the three vanilla-playable strings so a
/// not-yet-connected operator has something to pick. After auto-connect
/// fires the slot-map refresh, AOTR / ROE / ROTR / Thrawn's Revenge / etc.
/// each populate their own faction strings without code changes — the
/// registry is purely additive (never removes entries the operator may
/// have typed manually via <c>IsEditable=True</c> ComboBoxes).
/// </para>
/// <para>
/// Registered as a singleton in DI so every tab gets the same
/// <see cref="ObservableCollection{T}"/> instance. WPF data-binding
/// surfaces the collection's CollectionChanged events automatically, so
/// the dropdowns refresh live across tabs the moment a new faction lands.
/// </para>
/// </remarks>
public sealed class V2FactionRegistry
{
    private static readonly string[] s_vanillaSeed =
    {
        "EMPIRE", "REBEL", "UNDERWORLD",
    };

    public V2FactionRegistry()
    {
        Factions = new ObservableCollection<string>(s_vanillaSeed);
    }

    /// <summary>
    /// The shared faction-name collection. Bind this to ItemsSource on
    /// every faction ComboBox in V2 so they all update the moment a live
    /// probe surfaces a new faction string.
    /// </summary>
    public ObservableCollection<string> Factions { get; }

    /// <summary>
    /// Append-only merge: any string in <paramref name="liveFactions"/>
    /// that doesn't already exist (case-insensitive) gets added. Returns
    /// the count of new entries so callers can log the merge result.
    /// </summary>
    /// <remarks>
    /// Empty or whitespace-only strings are silently skipped. The merge
    /// preserves insertion order — the seed (EMPIRE/REBEL/UNDERWORLD)
    /// stays at the top, modded factions append below in the order the
    /// live game first reports them.
    /// </remarks>
    public int MergeFactions(IEnumerable<string> liveFactions)
    {
        ArgumentNullException.ThrowIfNull(liveFactions);
        var added = 0;
        foreach (var live in liveFactions)
        {
            if (string.IsNullOrWhiteSpace(live)) continue;
            var trimmed = live.Trim();
            var present = false;
            foreach (var existing in Factions)
            {
                if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    present = true;
                    break;
                }
            }
            if (!present)
            {
                Factions.Add(trimmed);
                added++;
            }
        }
        return added;
    }
}
