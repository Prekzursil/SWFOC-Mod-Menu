using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// 2026-04-27 (iter 38): xUnit collection definition that serialises every
/// test class touching process-global environment variables (e.g.
/// <c>SWFOC_FORCE_PROMOTED_EXTENDER</c>, <c>SWFOC_FORCE_PROFILE_ID</c>,
/// <c>SWFOC_GAME_ROOT</c>, <c>APPDATA</c> overrides). Without this, xUnit's
/// default per-class parallel runner can let one class set an env var while
/// another class is reading it — same race the runtime-mode-settings.json
/// pattern fixed in iter 19, but for env-var state instead of file state.
/// </summary>
/// <remarks>
/// First applied to <c>BackendRouterBranchCoverageTests</c> after the
/// `ResolvePromotedExtenderOverrideState_ShouldParseInt(value: "0")` test
/// flaked under full-suite load in iter 37. Pattern: any class that
/// touches <c>Environment.SetEnvironmentVariable</c> on a SWFOC_* or
/// APPDATA-style variable goes here.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvVarSerialCollection
{
    public const string Name = "Process env vars (SWFOC_FORCE_*, APPDATA)";
}
