using System;
using System.Collections.Generic;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class RuntimeAdapter
{
    private const string DiagnosticKeyHookState = "hookState";
    private const string DiagnosticKeyCreditsStateTag = "creditsStateTag";
    private const string DiagnosticKeyState = "state";
    private const string DiagnosticKeyExpertOverrideEnabled = "expertOverrideEnabled";
    private const string DiagnosticKeyOverrideReason = "overrideReason";
    private const string DiagnosticKeyRuntimeModeHint = "runtimeModeHint";
    private const string DiagnosticKeyRuntimeModeProbe = "runtimeModeProbe";
    private const string DiagnosticKeyRuntimeModeTelemetry = "runtimeModeTelemetry";
    private const string DiagnosticKeyRuntimeModeTelemetryReasonCode = "runtimeModeTelemetryReasonCode";
    private const string DiagnosticKeyRuntimeModeTelemetrySource = "runtimeModeTelemetrySource";
    private const string DiagnosticKeyRuntimeModeEffective = "runtimeModeEffective";
    private const string DiagnosticKeyRuntimeModeEffectiveSource = "runtimeModeEffectiveSource";
    private const string RuntimeModeSourceAuto = "auto";
    private const string RuntimeModeSourceManualOverride = "manual_override";
    private const string RuntimeModeSourceTelemetry = "telemetry";
    private const string DiagnosticKeyPanicDisableState = "panicDisableState";
    private const string PanicDisableStateActive = "active";
    private const string PanicDisableStateInactive = "inactive";
    private const string ExpertOverrideEnvVarName = "SWFOC_EXPERT_MUTATION_OVERRIDES";
    private const string ExpertOverridePanicEnvVarName = "SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC";
    private const string ActionIdSetUnitCap = "set_unit_cap";
    private const string ActionIdSetUnitCapPatchFallback = "set_unit_cap_patch_fallback";
    private const string ActionIdToggleInstantBuildPatch = "toggle_instant_build_patch";
    private const string ActionIdToggleFogRevealPatchFallback = "toggle_fog_reveal_patch_fallback";
    private const string ActionIdSetCredits = "set_credits";
    private const string SymbolCredits = "credits";

    private static readonly string[] ResultHookStateKeys =
    [
        DiagnosticKeyHookState,
        DiagnosticKeyCreditsStateTag,
        DiagnosticKeyState
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
