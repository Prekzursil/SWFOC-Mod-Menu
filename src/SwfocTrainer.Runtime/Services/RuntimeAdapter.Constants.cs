using System;
using System.Collections.Generic;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class RuntimeAdapter
{
    private const string DiagnosticHookState = "hookState";
    private const string DiagnosticCreditsStateTag = "creditsStateTag";
    private const string DiagnosticState = "state";
    private const string DiagnosticExpertOverrideEnabled = "expertOverrideEnabled";
    private const string DiagnosticOverrideReason = "overrideReason";
    private const string DiagnosticRuntimeModeHint = "runtimeModeHint";
    private const string DiagnosticRuntimeModeProbe = "runtimeModeProbe";
    private const string DiagnosticRuntimeModeTelemetry = "runtimeModeTelemetry";
    private const string DiagnosticRuntimeModeTelemetryReasonCode = "runtimeModeTelemetryReasonCode";
    private const string DiagnosticRuntimeModeTelemetrySource = "runtimeModeTelemetrySource";
    private const string DiagnosticRuntimeModeEffective = "runtimeModeEffective";
    private const string DiagnosticRuntimeModeEffectiveSource = "runtimeModeEffectiveSource";
    private const string RuntimeModeSourceAuto = "auto";
    private const string RuntimeModeSourceManualOverride = "manual_override";
    private const string RuntimeModeSourceTelemetry = "telemetry";
    private const string DiagnosticPanicDisableState = "panicDisableState";
    private const string PanicDisableStateActive = "active";
    private const string PanicDisableStateInactive = "inactive";
    private const string ExpertOverrideEnvVarName = "SWFOC_EXPERT_MUTATION_OVERRIDES";
    private const string ExpertOverridePanicEnvVarName = "SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC";
    private const string ActionIdSetUnitCap = "set_unit_cap";
    private const string ActionIdSetUnitCapPatchFallback = "set_unit_cap_patch_fallback";
    private const string ActionIdToggleInstantBuildPatch = "toggle_instant_build_patch";
    private const string ActionIdToggleFogRevealPatchFallback = "toggle_fog_reveal_patch_fallback";
    private const string ActionIdSetCredits = "set_credits";
    private const string ActionIdSetContextFaction = "set_context_faction";
    private const string ActionIdSetContextAllegiance = "set_context_allegiance";
    private const string ActionIdSpawnContextEntity = "spawn_context_entity";
    private const string ActionIdSpawnTacticalEntity = "spawn_tactical_entity";
    private const string ActionIdSpawnGalacticEntity = "spawn_galactic_entity";
    private const string ActionIdPlacePlanetBuilding = "place_planet_building";
    private const string ActionIdSpawnUnitHelper = "spawn_unit_helper";
    private const string ActionIdSetHeroStateHelper = "set_hero_state_helper";
    private const string ActionIdToggleRoeRespawnHelper = "toggle_roe_respawn_helper";
    private const string ActionIdSetSelectedOwnerFaction = "set_selected_owner_faction";
    private const string ActionIdSetPlanetOwner = "set_planet_owner";
    private const string SymbolCredits = "credits";

    private static readonly string[] ResultHookStateKeys =
    [
        DiagnosticHookState,
        DiagnosticCreditsStateTag,
        DiagnosticState
    ];

    private static readonly HashSet<string> PromotedExtenderActionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "freeze_timer",
        "toggle_fog_reveal",
        "toggle_ai",
        ActionIdSetUnitCap,
        ActionIdToggleInstantBuildPatch
    };
}
