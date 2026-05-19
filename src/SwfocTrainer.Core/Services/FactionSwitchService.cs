using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class FactionSwitchService : IFactionSwitchService
{
    internal const string FeatureId = "set_context_allegiance";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<FactionSwitchService> _logger;

    public FactionSwitchService(
        ILuaBridgeExecutor bridge,
        ILogger<FactionSwitchService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public FactionSwitchService(ILogger<FactionSwitchService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    /// <summary>
    /// Maps a faction name to a player-list slot index. SWFOC's engine uses
    /// a fixed slot ordering within the playable-player vector that matches
    /// the legacy CE trainer's faction tables and the verified_facts.json
    /// ledger entry <c>struct_player_list_current_slot_offset</c>.
    /// </summary>
    /// <remarks>
    /// This is a convenience mapping for the common case. Callers that need
    /// a specific slot (e.g. for 4-player skirmishes where slot ordering is
    /// scenario-dependent) should use <see cref="BuildSetHumanPlayerSlotLuaCommand"/>
    /// directly with a numeric slot instead.
    /// </remarks>
    internal static int FactionNameToSlot(string factionName) => factionName.ToUpperInvariant() switch
    {
        "REBEL" or "REBELS" or "REBEL_ALLIANCE" => 0,
        "EMPIRE" or "EMPIRE_FACTION" or "GALACTIC_EMPIRE" => 1,
        "UNDERWORLD" or "ZANN" or "ZANN_CONSORTIUM" => 2,
        _ => -1,
    };

    /// <summary>
    /// Builds a Lua command for the faction switch operation via the
    /// <c>SWFOC_SetHumanPlayer_v2</c> bridge helper.
    /// </summary>
    /// <remarks>
    /// <para><b>2026-04-11 galactic-mode fix:</b> The previous helper
    /// <c>SWFOC_SetHumanPlayer</c> wrapped <c>PlayerListClass::Switch_Sides</c>
    /// (RVA 0x297E80) in a bounded rotation loop, which works in tactical
    /// modes but is silently guarded out in galactic mode by
    /// <c>sub_14028AF60</c>. The live-game test on 2026-04-10 exposed this:
    /// the bridge returned success (1) but the local-player byte stayed
    /// pinned to the original slot. See
    /// <c>knowledge-base/faction_switch_full_anatomy_2026-04-11.md</c>.</para>
    ///
    /// <para>The v2 helper is mode-agnostic: it does a manual sweep of
    /// <c>PlayerObject+0x62</c> across all players, writes
    /// <c>PlayerListClass+0x30</c> directly, and calls the subsystem refresh
    /// path (<c>sub_1402B59B0</c> at RVA 0x2B59B0) to notify camera / HUD /
    /// selection / input router. The v1 helper is kept registered for
    /// diagnostic fallback but all faction-switch callers now go through
    /// v2.</para>
    /// </remarks>
    internal static string BuildFactionSwitchLuaCommand(string targetFaction)
    {
        ArgumentNullException.ThrowIfNull(targetFaction);
        if (string.IsNullOrWhiteSpace(targetFaction))
        {
            throw new ArgumentException("Target faction must not be empty or whitespace.", nameof(targetFaction));
        }

        int slot = FactionNameToSlot(targetFaction);
        if (slot < 0)
        {
            // Unknown faction name — surface a clear diagnostic rather than
            // passing -1 to the bridge (which would short-circuit to an
            // out-of-range error anyway but without the faction name context).
            return $"error(\"FactionSwitch: unknown faction '{targetFaction}'. " +
                   $"Expected REBEL, EMPIRE, or UNDERWORLD.\")";
        }

        return BuildSetHumanPlayerSlotLuaCommand(slot);
    }

    /// <summary>
    /// Builds a Lua command that invokes <c>SWFOC_SetHumanPlayer_v3(slot)</c>
    /// directly. v3 extends v2 (mode-agnostic byte sweep + subsystem refresh)
    /// with an AIPlayerClass-pointer swap at <c>PlayerObject+0x360</c>, so
    /// the AI no longer drives the faction the operator switched to.
    /// Defaults to v3 since the 2026-04-25 live test confirmed v2's
    /// dual-control bug. Callers that need v2 explicitly (diagnostic
    /// fallback) can call <c>SWFOC_SetHumanPlayer_v2</c> directly.
    /// </summary>
    internal static string BuildSetHumanPlayerSlotLuaCommand(int slot)
    {
        return $"return SWFOC_SetHumanPlayer_v3({slot})";
    }

    public async Task<ActionExecutionResult> SwitchFactionAsync(
        string profileId, FactionSwitchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TargetFaction))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "Target faction must not be empty",
                AddressSource: AddressSource.None);
        }

        var luaCommand = BuildFactionSwitchLuaCommand(request.TargetFaction);

        _logger.LogInformation(
            "Faction switch executing: -> {TargetFaction} ({FeatureId}) for profile {ProfileId}",
            request.TargetFaction, FeatureId, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Faction switch prepared: -> {request.TargetFaction}",
            AddressSource: AddressSource.None);
    }
}
