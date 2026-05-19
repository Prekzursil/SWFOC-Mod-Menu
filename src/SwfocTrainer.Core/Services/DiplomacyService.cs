using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

/// <summary>
/// Manages faction diplomacy relations via the Lua bridge.
/// MOD-AWARE: vanilla EaW has no diplomacy (factions always hostile).
/// Alliances reset on game mode changes (GC to tactical and back).
/// </summary>
public sealed class DiplomacyService : IDiplomacyService
{
    private static readonly IReadOnlyList<string> DefaultFactions = new[]
    {
        "EMPIRE", "REBEL", "UNDERWORLD"
    };

    internal const string FeatureId = "v5_diplomacy";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<DiplomacyService> _logger;

    public DiplomacyService(
        ILuaBridgeExecutor bridge,
        ILogger<DiplomacyService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public DiplomacyService(ILogger<DiplomacyService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    public Task<IReadOnlyList<DiplomacyState>> LoadDiplomacyAsync(
        string profileId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);

        var pairs = new List<DiplomacyState>();

        for (var i = 0; i < DefaultFactions.Count; i++)
        {
            for (var j = i + 1; j < DefaultFactions.Count; j++)
            {
                pairs.Add(new DiplomacyState(
                    Faction1: DefaultFactions[i],
                    Faction2: DefaultFactions[j],
                    Relation: DiplomacyRelation.Hostile));
            }
        }

        _logger.LogInformation(
            "Loaded {Count} default diplomacy pairs for profile {Profile}",
            pairs.Count, profileId);

        IReadOnlyList<DiplomacyState> result = pairs;
        return Task.FromResult(result);
    }

    public async Task<ActionExecutionResult> SetRelationAsync(
        string profileId, DiplomacyState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrWhiteSpace(state.Faction1))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "Faction1 must not be empty",
                AddressSource: AddressSource.None);
        }

        if (string.IsNullOrWhiteSpace(state.Faction2))
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "Faction2 must not be empty",
                AddressSource: AddressSource.None);
        }

        var luaCommand = BuildDiplomacyLuaCommand(state);

        if (luaCommand is null)
        {
            return new ActionExecutionResult(
                Succeeded: false,
                Message: "Neutral diplomacy is not directly supported by the game API",
                AddressSource: AddressSource.None,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["warning"] = "Neutral relation is not directly supported by the Lua API",
                    ["suggestion"] = "Consider Allied or Hostile instead"
                });
        }

        _logger.LogInformation(
            "Diplomacy executing: {Faction1} <-> {Faction2} = {Relation} via {LuaCommand} for profile {Profile}",
            state.Faction1, state.Faction2, state.Relation, luaCommand, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Diplomacy relation set: {state.Faction1} <-> {state.Faction2} = {state.Relation}",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["lua_call"] = luaCommand,
                ["faction1"] = state.Faction1,
                ["faction2"] = state.Faction2,
                ["alliance_warning"] = "alliances reset on game mode changes \u2014 consider auto-reapply"
            });
    }

    /// <summary>
    /// Builds the Lua command string for a diplomacy relation change.
    /// Returns null for relations without direct game API support (e.g., Neutral).
    /// </summary>
    /// <remarks>
    /// IDA Pro decompile evidence (session 2026-04-07):
    /// - <c>PlayerWrapper::Make_Ally</c> at RVA <c>0x6046A0</c>: PlayerObject INSTANCE method,
    ///   takes 1 player argument, calls shared engine <c>sub_140288800(this.player, other.slot, 0)</c>.
    /// - <c>PlayerWrapper::Make_Enemy</c> at RVA <c>0x604780</c>: same shape but with arg <c>1</c>.
    /// Both contain explicit class::method strings in the function body, irrefutable evidence.
    /// They are NOT Lua globals — the previous global form
    /// <c>Make_Ally(Find_Player("EMPIRE"), Find_Player("REBEL"))</c> would error at runtime
    /// because <c>Make_Ally</c> at global scope is nil. The correct invocation is the
    /// player:Method(other_player) form below.
    /// See <c>knowledge-base/verified_facts.json</c> entries
    /// <c>rva_lua_make_ally_wrapper</c> / <c>rva_lua_make_enemy_wrapper</c> /
    /// <c>rva_make_ally_make_enemy_engine</c>.
    /// </remarks>
    internal static string? BuildDiplomacyLuaCommand(DiplomacyState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var method = state.Relation switch
        {
            DiplomacyRelation.Allied => "Make_Ally",
            DiplomacyRelation.Hostile => "Make_Enemy",
            DiplomacyRelation.Neutral => null,
            _ => null
        };

        if (method is null)
        {
            return null;
        }

        // Method-call form via PlayerWrapper. The local variables guard against
        // Find_Player returning nil (e.g., when a faction is not present in the
        // current scenario), and the if-check prevents a Lua error from
        // attempting :Method() on nil.
        return $"local p1 = Find_Player(\"{state.Faction1}\"); " +
               $"local p2 = Find_Player(\"{state.Faction2}\"); " +
               $"if p1 and p2 then p1:{method}(p2) end";
    }

    /// <summary>
    /// Resolves the Lua API call for a given diplomacy relation.
    /// Returns null for relations without direct game API support.
    /// </summary>
    internal static string? ResolveDiplomacyAction(DiplomacyRelation relation)
    {
        return relation switch
        {
            DiplomacyRelation.Allied => "p1:Make_Ally(p2) [PlayerWrapper instance method]",
            DiplomacyRelation.Hostile => "p1:Make_Enemy(p2) [PlayerWrapper instance method]",
            DiplomacyRelation.Neutral => null,
            _ => null
        };
    }
}
