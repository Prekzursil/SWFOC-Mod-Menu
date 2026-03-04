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
        "spawn_tactical_entity",
        "spawn_galactic_entity",
        "place_planet_building",
        "set_context_allegiance",
        "transfer_fleet_safe",
        "flip_planet_owner",
        "switch_player_faction",
        "edit_hero_state",
        "create_hero_variant"
    ];

    private static readonly IReadOnlyDictionary<string, Func<int, JsonObject>> ActionPayloadBuilders =
        new Dictionary<string, Func<int, JsonObject>>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = BuildSetCreditsPayload,
            ["spawn_tactical_entity"] = BuildSpawnTacticalPayload,
            ["spawn_galactic_entity"] = BuildSpawnGalacticPayload,
            ["place_planet_building"] = BuildPlacePlanetBuildingPayload,
            ["set_context_allegiance"] = BuildSetContextAllegiancePayload,
            ["transfer_fleet_safe"] = BuildTransferFleetPayload,
            ["flip_planet_owner"] = BuildFlipPlanetPayload,
            ["switch_player_faction"] = BuildSwitchPlayerFactionPayload,
            ["edit_hero_state"] = BuildEditHeroStatePayload,
            ["create_hero_variant"] = BuildCreateHeroVariantPayload
        };

    public static ActionExecutionRequest BuildActionExecutionRequest(int variant)
    {
        var actionId = ResolveVariantActionId(variant);
        var action = BuildActionMap()[actionId];
        var payload = BuildActionPayload(actionId, variant);
        var context = BuildActionContext(variant);
        var mode = action.Mode switch
        {
            RuntimeMode.AnyTactical => variant % 2 == 0 ? RuntimeMode.TacticalLand : RuntimeMode.TacticalSpace,
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
        => new() { ["symbol"] = "credits", ["intValue"] = 1000 + variant };

    private static JsonObject BuildSpawnTacticalPayload(int variant)
        => new()
        {
            ["entityId"] = "EMP_STORMTROOPER",
            ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Rebel",
            ["worldPosition"] = "12,0,24",
            ["placementMode"] = "reinforcement_zone"
        };

    private static JsonObject BuildSpawnGalacticPayload(int _)
        => new() { ["entityId"] = "ACC_ACCLAMATOR_1", ["targetFaction"] = "Empire", ["planetId"] = "Coruscant" };

    private static JsonObject BuildPlacePlanetBuildingPayload(int variant)
        => new()
        {
            ["entityId"] = "E_GROUND_LIGHT_FACTORY",
            ["targetFaction"] = "Empire",
            ["placementMode"] = variant % 2 == 0 ? "safe_rules" : "force_override"
        };

    private static JsonObject BuildSetContextAllegiancePayload(int variant)
        => new() { ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Pirates", ["allowCrossFaction"] = true };

    private static JsonObject BuildTransferFleetPayload(int _)
        => new() { ["targetFaction"] = "Rebel", ["destinationPlanetId"] = "Kuat", ["safeTransfer"] = true };

    private static JsonObject BuildFlipPlanetPayload(int variant)
        => new()
        {
            ["planetId"] = "Kuat",
            ["targetFaction"] = "Rebel",
            ["modePolicy"] = variant % 2 == 0 ? "empty_and_retreat" : "convert_everything"
        };

    private static JsonObject BuildSwitchPlayerFactionPayload(int _)
        => new() { ["targetFaction"] = "Rebel" };

    private static JsonObject BuildEditHeroStatePayload(int variant)
        => new() { ["entityId"] = "DARTH_VADER", ["desiredState"] = variant % 2 == 0 ? "alive" : "respawn_pending" };

    private static JsonObject BuildCreateHeroVariantPayload(int variant)
        => new()
        {
            ["entityId"] = "MACE_WINDU",
            ["variantId"] = $"MACE_WINDU_VARIANT_{variant}",
            ["allowDuplicate"] = variant % 2 == 0,
            ["modifiers"] = new JsonObject { ["healthMultiplier"] = 1.25, ["damageMultiplier"] = 1.1 }
        };

    private static IReadOnlyDictionary<string, object?>? BuildActionContext(int variant)
    {
        return variant switch
        {
            2 => new Dictionary<string, object?> { ["runtimeModeOverride"] = "Unknown" },
            5 => new Dictionary<string, object?> { ["selectedPlanetId"] = "Kuat", ["requestedBy"] = "coverage" },
            7 => new Dictionary<string, object?> { ["runtimeModeOverride"] = "Galactic", ["allowCrossFaction"] = true },
            _ => null
        };
    }

    public static IReadOnlyDictionary<string, ActionSpec> BuildActionMap()
    {
        return new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new ActionSpec("set_credits", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_context_entity"] = new ActionSpec("spawn_context_entity", ActionCategory.Global, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_tactical_entity"] = new ActionSpec("spawn_tactical_entity", ActionCategory.Tactical, RuntimeMode.TacticalLand, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
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
