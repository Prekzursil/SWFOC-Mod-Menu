#pragma warning disable CA1014
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Tests.Runtime;

internal static class ReflectionCoverageActionFactory
{
    private static readonly string[] VariantActionIds =
    [
        "set_credits",
        "set_unit_cap",
        "toggle_instant_build_patch",
        "toggle_fog_reveal_patch_fallback",
        "spawn_context_entity",
        "spawn_tactical_entity",
        "spawn_galactic_entity",
        "place_planet_building",
        "set_context_faction",
        "set_context_allegiance",
        "transfer_fleet_safe",
        "flip_planet_owner",
        "switch_player_faction",
        "edit_hero_state",
        "create_hero_variant",
        "set_selected_owner_faction",
        "set_planet_owner",
        "spawn_unit_helper",
        "set_hero_state_helper",
        "toggle_roe_respawn_helper"
    ];

    private static readonly IReadOnlyDictionary<string, Func<int, JsonObject>> ActionPayloadBuilders =
        new Dictionary<string, Func<int, JsonObject>>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = BuildSetCreditsPayload,
            ["set_unit_cap"] = BuildSetUnitCapPayload,
            ["toggle_instant_build_patch"] = BuildToggleInstantBuildPayload,
            ["toggle_fog_reveal_patch_fallback"] = BuildToggleFogFallbackPayload,
            ["spawn_context_entity"] = BuildSpawnContextPayload,
            ["spawn_tactical_entity"] = BuildSpawnTacticalPayload,
            ["spawn_galactic_entity"] = BuildSpawnGalacticPayload,
            ["place_planet_building"] = BuildPlacePlanetBuildingPayload,
            ["set_context_faction"] = BuildSetContextFactionPayload,
            ["set_context_allegiance"] = BuildSetContextAllegiancePayload,
            ["transfer_fleet_safe"] = BuildTransferFleetPayload,
            ["flip_planet_owner"] = BuildFlipPlanetPayload,
            ["switch_player_faction"] = BuildSwitchPlayerFactionPayload,
            ["edit_hero_state"] = BuildEditHeroStatePayload,
            ["create_hero_variant"] = BuildCreateHeroVariantPayload,
            ["set_selected_owner_faction"] = BuildSetSelectedOwnerFactionPayload,
            ["set_planet_owner"] = BuildSetPlanetOwnerPayload,
            ["spawn_unit_helper"] = BuildSpawnUnitHelperPayload,
            ["set_hero_state_helper"] = BuildSetHeroStateHelperPayload,
            ["toggle_roe_respawn_helper"] = BuildToggleRoeRespawnPayload
        };

    public static ActionExecutionRequest BuildActionExecutionRequest(int variant)
    {
        var actionId = ResolveVariantActionId(variant);
        var action = BuildActionMap()[actionId];
        var payload = BuildActionPayload(actionId, variant);
        var context = BuildActionContext(variant);
        var mode = action.Mode switch
        {
            RuntimeMode.AnyTactical => (variant % 3) switch
            {
                1 => RuntimeMode.TacticalLand,
                2 => RuntimeMode.TacticalSpace,
                _ => RuntimeMode.AnyTactical
            },
            _ => action.Mode
        };

        return new ActionExecutionRequest(action, payload, "profile", mode, context);
    }

    private static string ResolveVariantActionId(int variant)
    {
        var index = Math.Abs(variant) % VariantActionIds.Length;
        return VariantActionIds[index];
    }

    private static JsonObject BuildActionPayload(string actionId, int variant)
    {
        if (ActionPayloadBuilders.TryGetValue(actionId, out var builder))
        {
            return builder(variant);
        }

        return new JsonObject();
    }

    private static JsonObject BuildSetCreditsPayload(int variant)
        => new() { ["symbol"] = variant % 2 == 0 ? "credits" : "CREDITS", ["intValue"] = 1000 + variant };

    private static JsonObject BuildSetUnitCapPayload(int variant)
        => new()
        {
            ["symbol"] = "unit_cap",
            ["intValue"] = variant % 2 == 0 ? 220 : 140,
            ["enable"] = variant % 3 != 1
        };

    private static JsonObject BuildToggleInstantBuildPayload(int variant)
        => new()
        {
            ["symbol"] = "instant_build_patch",
            ["enable"] = variant % 2 == 0,
            ["patchBytes"] = "90 90 90",
            ["originalBytes"] = "89 44 24"
        };

    private static JsonObject BuildToggleFogFallbackPayload(int variant)
        => new()
        {
            ["symbol"] = "fog_reveal",
            ["enable"] = variant % 2 == 0,
            ["runtimeMode"] = variant % 3 == 0 ? "TacticalLand" : "TacticalSpace"
        };

    private static JsonObject BuildSpawnContextPayload(int variant)
        => new()
        {
            ["entityId"] = variant % 2 == 0 ? "EMP_STORMTROOPER" : "REB_SOLDIER",
            ["targetFaction"] = variant % 3 == 0 ? "Empire" : "Rebel",
            ["worldPosition"] = "12,0,24",
            ["placementMode"] = variant % 2 == 0 ? "reinforcement_zone" : "anywhere"
        };

    private static JsonObject BuildSpawnTacticalPayload(int variant)
        => new()
        {
            ["entityId"] = "EMP_STORMTROOPER",
            ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Pirates",
            ["worldPosition"] = variant % 2 == 0 ? "12,0,24" : "0,0,0",
            ["placementMode"] = variant % 2 == 0 ? "reinforcement_zone" : "anywhere"
        };

    private static JsonObject BuildSpawnGalacticPayload(int variant)
        => new() { ["entityId"] = "ACC_ACCLAMATOR_1", ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Rebel", ["planetId"] = variant % 3 == 0 ? "Coruscant" : "Kuat" };

    private static JsonObject BuildPlacePlanetBuildingPayload(int variant)
        => new()
        {
            ["entityId"] = "E_GROUND_LIGHT_FACTORY",
            ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Rebel",
            ["placementMode"] = variant % 2 == 0 ? "safe_rules" : "force_override",
            ["forceOverride"] = variant % 4 == 0
        };

    private static JsonObject BuildSetContextFactionPayload(int variant)
        => new()
        {
            ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Pirates",
            ["sourceFaction"] = variant % 2 == 0 ? "Rebel" : "Empire",
            ["allowCrossFaction"] = true
        };

    private static JsonObject BuildSetContextAllegiancePayload(int variant)
        => new()
        {
            ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Pirates",
            ["sourceFaction"] = variant % 2 == 0 ? "Rebel" : "Empire",
            ["allowCrossFaction"] = true
        };

    private static JsonObject BuildTransferFleetPayload(int variant)
        => new()
        {
            ["entityId"] = "fleet_kuat_01",
            ["sourceFaction"] = variant % 2 == 0 ? "Empire" : "Rebel",
            ["targetFaction"] = variant % 2 == 0 ? "Rebel" : "Empire",
            ["safePlanetId"] = variant % 3 == 0 ? "Kuat" : "Coruscant",
            ["safeTransfer"] = true
        };

    private static JsonObject BuildFlipPlanetPayload(int variant)
        => new()
        {
            ["entityId"] = "Kuat",
            ["planetId"] = "Kuat",
            ["targetFaction"] = variant % 2 == 0 ? "Rebel" : "Empire",
            ["modePolicy"] = variant % 2 == 0 ? "empty_and_retreat" : "convert_everything"
        };

    private static JsonObject BuildSwitchPlayerFactionPayload(int variant)
        => new() { ["targetFaction"] = variant % 2 == 0 ? "Rebel" : "Empire" };

    private static JsonObject BuildEditHeroStatePayload(int variant)
        => new() { ["entityId"] = "DARTH_VADER", ["globalKey"] = "AOTR_HERO_KEY", ["desiredState"] = (variant % 3) switch { 0 => "alive", 1 => "respawn_pending", _ => "dead" } };

    private static JsonObject BuildCreateHeroVariantPayload(int variant)
        => new()
        {
            ["entityId"] = "MACE_WINDU",
            ["unitId"] = $"MACE_WINDU_VARIANT_{variant}",
            ["allowDuplicate"] = variant % 2 == 0,
            ["modifiers"] = new JsonObject { ["healthMultiplier"] = 1.25, ["damageMultiplier"] = 1.1 }
        };

    private static JsonObject BuildSetSelectedOwnerFactionPayload(int variant)
        => new() { ["ownerFaction"] = variant % 2 == 0 ? "Empire" : "Rebel" };

    private static JsonObject BuildSetPlanetOwnerPayload(int variant)
        => new() { ["planetId"] = variant % 2 == 0 ? "Kuat" : "Coruscant", ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Rebel" };

    private static JsonObject BuildSpawnUnitHelperPayload(int variant)
        => new()
        {
            ["unitId"] = variant % 2 == 0 ? "EMP_STORMTROOPER" : "REB_SOLDIER",
            ["entryMarker"] = variant % 2 == 0 ? "Land_Reinforcement_Point" : "Space_Reinforcement_Point",
            ["faction"] = variant % 2 == 0 ? "Empire" : "Rebel"
        };

    private static JsonObject BuildSetHeroStateHelperPayload(int variant)
        => new() { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = variant % 2 == 0 ? 1 : 0 };

    private static JsonObject BuildToggleRoeRespawnPayload(int variant)
        => new() { ["globalKey"] = "ROE_RESPAWN", ["boolValue"] = variant % 2 == 0 };

    private static IReadOnlyDictionary<string, object?>? BuildActionContext(int variant)
    {
        return (variant % 8) switch
        {
            1 => new Dictionary<string, object?> { ["runtimeModeOverride"] = "Unknown" },
            2 => new Dictionary<string, object?> { ["selectedPlanetId"] = "Kuat", ["requestedBy"] = "coverage" },
            3 => new Dictionary<string, object?> { ["runtimeModeOverride"] = "Galactic", ["allowCrossFaction"] = true },
            4 => new Dictionary<string, object?> { ["runtimeModeOverride"] = "TacticalLand", ["forceOverride"] = true },
            5 => new Dictionary<string, object?> { ["resolvedVariant"] = "base_swfoc", ["dependencyValidation"] = "Pass" },
            6 => new Dictionary<string, object?> { ["helperHookId"] = "spawn_bridge", ["helperEntryPoint"] = "SWFOC_Trainer_Spawn_Context" },
            7 => new Dictionary<string, object?> { ["targetFaction"] = "Pirates", ["sourceFaction"] = "Empire" },
            _ => null
        };
    }

    public static IReadOnlyDictionary<string, ActionSpec> BuildActionMap()
    {
        return new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new ActionSpec("set_credits", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_unit_cap"] = new ActionSpec("set_unit_cap", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Sdk, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["toggle_instant_build_patch"] = new ActionSpec("toggle_instant_build_patch", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Sdk, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["toggle_fog_reveal_patch_fallback"] = new ActionSpec("toggle_fog_reveal_patch_fallback", ActionCategory.Tactical, RuntimeMode.AnyTactical, ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_context_entity"] = new ActionSpec("spawn_context_entity", ActionCategory.Global, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_tactical_entity"] = new ActionSpec("spawn_tactical_entity", ActionCategory.Tactical, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_galactic_entity"] = new ActionSpec("spawn_galactic_entity", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["place_planet_building"] = new ActionSpec("place_planet_building", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_context_faction"] = new ActionSpec("set_context_faction", ActionCategory.Global, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_context_allegiance"] = new ActionSpec("set_context_allegiance", ActionCategory.Global, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["transfer_fleet_safe"] = new ActionSpec("transfer_fleet_safe", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["flip_planet_owner"] = new ActionSpec("flip_planet_owner", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["switch_player_faction"] = new ActionSpec("switch_player_faction", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["edit_hero_state"] = new ActionSpec("edit_hero_state", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["create_hero_variant"] = new ActionSpec("create_hero_variant", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_selected_owner_faction"] = new ActionSpec("set_selected_owner_faction", ActionCategory.Tactical, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_planet_owner"] = new ActionSpec("set_planet_owner", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_unit_helper"] = new ActionSpec("spawn_unit_helper", ActionCategory.Tactical, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_hero_state_helper"] = new ActionSpec("set_hero_state_helper", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["toggle_roe_respawn_helper"] = new ActionSpec("toggle_roe_respawn_helper", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0)
        };
    }

    public static IReadOnlyDictionary<string, bool> BuildFeatureFlags()
    {
        return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow.building.force_override"] = true,
            ["allow.cross.faction.default"] = true
        };
    }

    public static LaunchContext BuildLaunchContext()
    {
        return new LaunchContext(
            LaunchKind.Workshop,
            CommandLineAvailable: true,
            SteamModIds: ["1397421866"],
            ModPathRaw: null,
            ModPathNormalized: null,
            DetectedVia: "cmdline",
            Recommendation: new ProfileRecommendation("base_swfoc", "workshop_match", 0.9),
            Source: "detected");
    }
}
#pragma warning restore CA1014

