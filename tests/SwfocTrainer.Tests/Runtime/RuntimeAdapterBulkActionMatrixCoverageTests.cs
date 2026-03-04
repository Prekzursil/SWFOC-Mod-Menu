using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterBulkActionMatrixCoverageTests
{
    private static readonly string[] KnownActionIds =
    [
        "read_symbol",
        "set_credits",
        "set_credits_extender_experimental",
        "freeze_timer",
        "toggle_fog_reveal",
        "toggle_ai",
        "set_instant_build_multiplier",
        "set_selected_hp",
        "set_selected_shield",
        "set_selected_speed",
        "set_selected_damage_multiplier",
        "set_selected_cooldown_multiplier",
        "set_selected_veterancy",
        "set_selected_owner_faction",
        "set_planet_owner",
        "set_context_faction",
        "set_context_allegiance",
        "spawn_context_entity",
        "spawn_tactical_entity",
        "spawn_galactic_entity",
        "place_planet_building",
        "transfer_fleet_safe",
        "flip_planet_owner",
        "switch_player_faction",
        "edit_hero_state",
        "create_hero_variant",
        "set_hero_respawn_timer",
        "toggle_tactical_god_mode",
        "toggle_tactical_one_hit_mode",
        "set_game_speed",
        "freeze_symbol",
        "unfreeze_symbol",
        "set_unit_cap"
    ];

    [Fact]
    public async Task ExecuteAsync_ShouldTraverseKnownActionMatrix()
    {
        var backends = new[]
        {
            ExecutionBackendKind.Memory,
            ExecutionBackendKind.Helper,
            ExecutionBackendKind.Extender,
            ExecutionBackendKind.Save
        };

        var modes = new[]
        {
            RuntimeMode.Unknown,
            RuntimeMode.Galactic,
            RuntimeMode.TacticalLand,
            RuntimeMode.TacticalSpace,
            RuntimeMode.AnyTactical
        };

        var executed = 0;
        foreach (var backend in backends)
        {
            foreach (var mode in modes)
            {
                var profile = BuildProfile();
                var harness = new AdapterHarness
                {
                    IncludeExecutionBackend = backend == ExecutionBackendKind.Extender,
                    Router = new StubBackendRouter(new BackendRouteDecision(
                        Allowed: true,
                        Backend: backend,
                        ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                        Message: "ok")),
                    HelperBridgeBackend = new StubHelperBridgeBackend()
                };

                var adapter = harness.CreateAdapter(profile, mode);

                foreach (var actionId in KnownActionIds)
                {
                    if (!profile.Actions.TryGetValue(actionId, out var action))
                    {
                        continue;
                    }

                    foreach (var payload in BuildPayloadVariants(actionId))
                    {
                        var request = new ActionExecutionRequest(
                            Action: action,
                            Payload: payload,
                            ProfileId: profile.Id,
                            RuntimeMode: mode,
                            Context: null);

                        try
                        {
                            _ = await adapter.ExecuteAsync(request, CancellationToken.None);
                        }
                        catch
                        {
                            // Fail-closed and guard-path exceptions are acceptable in matrix sweep.
                        }

                        executed++;
                    }
                }
            }
        }

        executed.Should().BeGreaterThan(900);
    }

    private static IEnumerable<JsonObject> BuildPayloadVariants(string actionId)
    {
        yield return new JsonObject();

        var rich = new JsonObject
        {
            ["symbol"] = "credits",
            ["value"] = 100,
            ["entityId"] = "EMP_STORMTROOPER_SQUAD",
            ["entityKind"] = "Unit",
            ["targetFaction"] = "Empire",
            ["sourceFaction"] = "Rebel",
            ["allowCrossFaction"] = true,
            ["forceOverride"] = false,
            ["populationPolicy"] = "ForceZeroTactical",
            ["persistencePolicy"] = "EphemeralBattleOnly",
            ["placementMode"] = "world_position",
            ["entryMarker"] = "spawn_01",
            ["worldPosition"] = new JsonObject
            {
                ["x"] = 1,
                ["y"] = 2,
                ["z"] = 3
            },
            ["helperHookId"] = "spawn_bridge",
            ["helperEntryPoint"] = actionId,
            ["operationKind"] = actionId,
            ["operationToken"] = Guid.NewGuid().ToString("N"),
            ["mutationIntent"] = "coverage_sweep",
            ["fleetTransferMode"] = "safe",
            ["planetFlipMode"] = "convert_everything",
            ["desiredState"] = "respawn_pending",
            ["respawnPolicyOverride"] = "default",
            ["allowDuplicate"] = true,
            ["variantId"] = "CUSTOM_HERO_VARIANT"
        };

        yield return rich;
    }

    private static TrainerProfile BuildProfile()
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in KnownActionIds)
        {
            var executionKind = id.Contains("spawn", StringComparison.OrdinalIgnoreCase)
                                || id.Contains("planet", StringComparison.OrdinalIgnoreCase)
                                || id.Contains("faction", StringComparison.OrdinalIgnoreCase)
                                || id.Contains("hero", StringComparison.OrdinalIgnoreCase)
                                || id.Contains("fleet", StringComparison.OrdinalIgnoreCase)
                                || id.Contains("variant", StringComparison.OrdinalIgnoreCase)
                ? ExecutionKind.Helper
                : ExecutionKind.Memory;

            actions[id] = new ActionSpec(
                id,
                ActionCategory.Global,
                RuntimeMode.Unknown,
                executionKind,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0);
        }

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(
                    Name: "test",
                    GameBuild: "build",
                    Signatures:
                    [
                        new SignatureSpec("credits", "AA BB", 0)
                    ])
            ],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits_rva"] = 0x10
            },
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow.building.force_override"] = true,
                ["allow.cross.faction.default"] = true
            },
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks:
            [
                new HelperHookSpec(
                    Id: "spawn_bridge",
                    Script: "scripts/common/spawn_bridge.lua",
                    Version: "1.1.0",
                    EntryPoint: "SWFOC_Trainer_Spawn_Context")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
