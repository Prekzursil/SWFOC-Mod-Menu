using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Diagnostics;

/// <summary>
/// Task #121 — unit tests for <see cref="ExecutionPathRouter"/>.
/// Pins the "memory + hook/AOB as first-class" matrix so a future
/// refactor can't collapse paths back into "Lua-only" by accident.
/// </summary>
public sealed class ExecutionPathRouterTests
{
    private static CapabilityReport EmptyReport() =>
        CapabilityReport.Unknown("test_profile", RuntimeReasonCode.CAPABILITY_PROBE_PASS);

    private static CapabilityReport ReportWithFeature(string featureId) =>
        new(
            ProfileId: "test_profile",
            ProbedAtUtc: DateTimeOffset.UtcNow,
            Capabilities: new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                [featureId] = new BackendCapability(
                    featureId,
                    Available: true,
                    Confidence: CapabilityConfidenceState.Verified,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS),
            },
            ProbeReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS);

    // ─── FromExecutionKind mapping ───────────────────────────────

    [Theory]
    [InlineData(ExecutionKind.Memory, ExecutionPath.MemoryWrite)]
    [InlineData(ExecutionKind.Helper, ExecutionPath.LuaBridge)]
    [InlineData(ExecutionKind.Save, ExecutionPath.Save)]
    [InlineData(ExecutionKind.CodePatch, ExecutionPath.Aob)]
    [InlineData(ExecutionKind.Freeze, ExecutionPath.Freeze)]
    [InlineData(ExecutionKind.Sdk, ExecutionPath.Sdk)]
    public void FromExecutionKind_MapsEveryKnownKind(ExecutionKind kind, ExecutionPath expected)
    {
        ExecutionPathRouter.FromExecutionKind(kind).Should().Be(expected);
    }

    // ─── RequiredBackends matrix ────────────────────────────────

    [Fact]
    public void RequiredBackends_LuaBridge_AllowsExtenderOrHelper()
    {
        var backends = ExecutionPathRouter.RequiredBackends(ExecutionPath.LuaBridge);
        backends.Should().Contain(ExecutionBackendKind.Extender);
        backends.Should().Contain(ExecutionBackendKind.Helper);
        backends.Should().HaveCount(2);
    }

    [Fact]
    public void RequiredBackends_LuaEngine_AllowsEitherBridgeBackend()
    {
        var backends = ExecutionPathRouter.RequiredBackends(ExecutionPath.LuaEngine);
        backends.Should().Contain(new[] { ExecutionBackendKind.Extender, ExecutionBackendKind.Helper });
    }

    [Theory]
    [InlineData(ExecutionPath.MemoryRead)]
    [InlineData(ExecutionPath.MemoryWrite)]
    [InlineData(ExecutionPath.Hook)]
    [InlineData(ExecutionPath.Aob)]
    [InlineData(ExecutionPath.Freeze)]
    public void RequiredBackends_MemoryLikePaths_GoThroughMemoryBackend(ExecutionPath path)
    {
        var backends = ExecutionPathRouter.RequiredBackends(path);
        backends.Should().ContainSingle();
        backends.Should().Contain(ExecutionBackendKind.Memory);
    }

    [Fact]
    public void RequiredBackends_Sdk_GoesThroughExtenderOnly()
    {
        var backends = ExecutionPathRouter.RequiredBackends(ExecutionPath.Sdk);
        backends.Should().ContainSingle();
        backends.Should().Contain(ExecutionBackendKind.Extender);
    }

    [Fact]
    public void RequiredBackends_Save_GoesThroughSaveOnly()
    {
        var backends = ExecutionPathRouter.RequiredBackends(ExecutionPath.Save);
        backends.Should().ContainSingle();
        backends.Should().Contain(ExecutionBackendKind.Save);
    }

    [Fact]
    public void RequiredBackends_Unknown_HasNoRequirement()
    {
        ExecutionPathRouter.RequiredBackends(ExecutionPath.Unknown).Should().BeEmpty();
    }

    // ─── Validate: explicit availableBackends override ───────────

    [Fact]
    public void Validate_LuaBridge_WithExtenderAvailable_Reachable()
    {
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.LuaBridge,
            EmptyReport(),
            new[] { ExecutionBackendKind.Extender });
        decision.Reachable.Should().BeTrue();
        decision.ConsideredBackends.Should().Contain(ExecutionBackendKind.Extender);
        decision.Reason.Should().Contain("reachable via Extender");
    }

    [Fact]
    public void Validate_LuaBridge_WithNoBackends_NotReachable()
    {
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.LuaBridge,
            EmptyReport(),
            Array.Empty<ExecutionBackendKind>());
        decision.Reachable.Should().BeFalse();
        decision.Reason.Should().Contain("requires one of");
    }

    [Fact]
    public void Validate_LuaBridge_WithSaveOnly_NotReachable()
    {
        // Save backend doesn't satisfy LuaBridge.
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.LuaBridge,
            EmptyReport(),
            new[] { ExecutionBackendKind.Save });
        decision.Reachable.Should().BeFalse();
    }

    [Fact]
    public void Validate_Hook_WithMemoryAvailable_Reachable()
    {
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.Hook,
            EmptyReport(),
            new[] { ExecutionBackendKind.Memory });
        decision.Reachable.Should().BeTrue();
        decision.Reason.Should().Contain("reachable via Memory");
    }

    [Fact]
    public void Validate_Aob_WithMemoryAvailable_Reachable()
    {
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.Aob,
            EmptyReport(),
            new[] { ExecutionBackendKind.Memory });
        decision.Reachable.Should().BeTrue();
    }

    [Fact]
    public void Validate_LuaBridge_FallbacksListed_WhenBothAvailable()
    {
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.LuaBridge,
            EmptyReport(),
            new[] { ExecutionBackendKind.Extender, ExecutionBackendKind.Helper });
        decision.Reachable.Should().BeTrue();
        decision.ConsideredBackends.Should().HaveCount(2);
        decision.Reason.Should().Contain("fallbacks");
    }

    [Fact]
    public void Validate_Unknown_ReturnsUnreachable()
    {
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.Unknown,
            EmptyReport(),
            new[] { ExecutionBackendKind.Extender, ExecutionBackendKind.Memory });
        decision.Reachable.Should().BeFalse();
        decision.Reason.Should().Contain("no known required backend");
    }

    // ─── Validate: capability-report inference ──────────────────

    [Fact]
    public void Validate_InferFromCapabilityReport_GivesMemoryAlways()
    {
        // Even an empty report implies we're attached (got a report),
        // which means Memory is always available.
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.MemoryWrite,
            EmptyReport());
        decision.Reachable.Should().BeTrue();
    }

    [Fact]
    public void Validate_InferFromReport_LuaBridge_NeedsAvailableCapability()
    {
        // Empty report (no available capabilities) → Extender not reachable → LuaBridge fails.
        var noCapDecision = ExecutionPathRouter.Validate(ExecutionPath.LuaBridge, EmptyReport());
        noCapDecision.Reachable.Should().BeFalse();

        // Report with at least one available capability → Extender backend inferred available.
        var withCap = ExecutionPathRouter.Validate(ExecutionPath.LuaBridge, ReportWithFeature("god_mode"));
        withCap.Reachable.Should().BeTrue();
    }

    [Fact]
    public void Validate_Save_NotReachableWithoutExplicitSaveBackend()
    {
        // Save is the only path the inference can't find for us —
        // callers explicitly opt-in by passing Save in availableBackends.
        var decision = ExecutionPathRouter.Validate(
            ExecutionPath.Save,
            ReportWithFeature("anything"));  // inferred backends won't include Save
        decision.Reachable.Should().BeFalse();

        var opted = ExecutionPathRouter.Validate(
            ExecutionPath.Save,
            ReportWithFeature("anything"),
            new[] { ExecutionBackendKind.Save });
        opted.Reachable.Should().BeTrue();
    }

    // ─── ValidateAll ───────────────────────────────────────────

    [Fact]
    public void ValidateAll_RunsEveryPath_ReturnsDecisions()
    {
        var paths = new[]
        {
            ExecutionPath.LuaBridge, ExecutionPath.MemoryWrite,
            ExecutionPath.Hook, ExecutionPath.Aob, ExecutionPath.Freeze,
        };
        var decisions = ExecutionPathRouter.ValidateAll(
            paths,
            EmptyReport(),
            new[] { ExecutionBackendKind.Memory, ExecutionBackendKind.Extender });
        decisions.Should().HaveCount(paths.Length);
        decisions.Select(d => d.Path).Should().BeEquivalentTo(paths);
        decisions.All(d => d.Reachable).Should().BeTrue();
    }

    [Fact]
    public void ValidateAll_EmptyPaths_ReturnsEmpty()
    {
        var decisions = ExecutionPathRouter.ValidateAll(
            Array.Empty<ExecutionPath>(),
            EmptyReport());
        decisions.Should().BeEmpty();
    }

    // ─── Sanity: every enum value has a handled branch ──────────

    [Fact]
    public void Every_ExecutionPath_Value_Is_Handled_By_RequiredBackends()
    {
        // Drift guard: if someone adds a new ExecutionPath value and
        // forgets to wire it into RequiredBackends, this test catches it.
        var paths = Enum.GetValues<ExecutionPath>();
        foreach (var path in paths)
        {
            var backends = ExecutionPathRouter.RequiredBackends(path);
            if (path == ExecutionPath.Unknown)
            {
                backends.Should().BeEmpty("Unknown is the sentinel default");
            }
            else
            {
                backends.Should().NotBeEmpty(
                    $"ExecutionPath.{path} must declare at least one required backend");
            }
        }
    }
}
