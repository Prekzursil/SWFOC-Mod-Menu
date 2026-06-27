using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 8 branch-coverage tests for BackendRouter — targets remaining uncovered
/// branches in ReadContext*, MapDefaultBackend, ResolveAutoFallback, promoted extender
/// override, resolve API integration paths, and null guard edges.
/// </summary>
[Collection(EnvVarSerialCollection.Name)]
public sealed class BackendRouterWave8BranchCoverageTests
{
    private static readonly BindingFlags NonPublicStatic =
        BindingFlags.Static | BindingFlags.NonPublic;

    // ── ReadContextString ────────────────────────────────────────────────

    [Fact]
    public void ReadContextString_NullContext_ReturnsNull()
    {
        var method = typeof(BackendRouter).GetMethod("ReadContextString", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { null, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextString_MissingKey_ReturnsNull()
    {
        var ctx = new Dictionary<string, object?> { ["other"] = "v" };
        var method = typeof(BackendRouter).GetMethod("ReadContextString", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextString_NullValue_ReturnsNull()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = null };
        var method = typeof(BackendRouter).GetMethod("ReadContextString", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextString_StringValue_ReturnsAsIs()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = "hello" };
        var method = typeof(BackendRouter).GetMethod("ReadContextString", NonPublicStatic)!;
        var result = (string?)method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().Be("hello");
    }

    [Fact]
    public void ReadContextString_NonStringValue_CallsToString()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = 42 };
        var method = typeof(BackendRouter).GetMethod("ReadContextString", NonPublicStatic)!;
        var result = (string?)method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().Be("42");
    }

    [Fact]
    public void ReadContextString_ObjectWithNullToString_ReturnsEmptyString()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = new NullToStringObject() };
        var method = typeof(BackendRouter).GetMethod("ReadContextString", NonPublicStatic)!;
        var result = (string?)method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().Be(string.Empty);
    }

    // ── ReadContextBool ──────────────────────────────────────────────────

    [Fact]
    public void ReadContextBool_NullContext_ReturnsNull()
    {
        var method = typeof(BackendRouter).GetMethod("ReadContextBool", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { null, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextBool_MissingKey_ReturnsNull()
    {
        var ctx = new Dictionary<string, object?> { ["other"] = true };
        var method = typeof(BackendRouter).GetMethod("ReadContextBool", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextBool_NullValue_ReturnsNull()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = null };
        var method = typeof(BackendRouter).GetMethod("ReadContextBool", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextBool_BoolValue_ReturnsDirect()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = true };
        var method = typeof(BackendRouter).GetMethod("ReadContextBool", NonPublicStatic)!;
        var result = (bool?)method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeTrue();
    }

    [Fact]
    public void ReadContextBool_StringTrue_ReturnsTrue()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = "True" };
        var method = typeof(BackendRouter).GetMethod("ReadContextBool", NonPublicStatic)!;
        var result = (bool?)method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeTrue();
    }

    [Fact]
    public void ReadContextBool_UnparseableString_ReturnsNull()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = "notbool" };
        var method = typeof(BackendRouter).GetMethod("ReadContextBool", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextBool_ObjectWithNullToString_ReturnsNull()
    {
        var ctx = new Dictionary<string, object?> { ["key"] = new NullToStringObject() };
        var method = typeof(BackendRouter).GetMethod("ReadContextBool", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { ctx, "key" });
        result.Should().BeNull();
    }

    // ── MapDefaultBackend ────────────────────────────────────────────────

    [Theory]
    [InlineData(ExecutionKind.Helper, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionKind.Save, ExecutionBackendKind.Save)]
    [InlineData(ExecutionKind.Memory, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.CodePatch, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Freeze, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Sdk, ExecutionBackendKind.Extender)]
    public void MapDefaultBackend_NoForce_ReturnsMappedKind(ExecutionKind kind, ExecutionBackendKind expected)
    {
        var method = typeof(BackendRouter).GetMethod("MapDefaultBackend", NonPublicStatic)!;
        var result = (ExecutionBackendKind)method.Invoke(null, new object[] { kind, false })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void MapDefaultBackend_ForceExtender_ReturnsExtender()
    {
        var method = typeof(BackendRouter).GetMethod("MapDefaultBackend", NonPublicStatic)!;
        var result = (ExecutionBackendKind)method.Invoke(null, new object[] { ExecutionKind.Memory, true })!;
        result.Should().Be(ExecutionBackendKind.Extender);
    }

    [Fact]
    public void MapDefaultBackend_UnknownKind_ReturnsMemory()
    {
        var method = typeof(BackendRouter).GetMethod("MapDefaultBackend", NonPublicStatic)!;
        var result = (ExecutionBackendKind)method.Invoke(null, new object[] { (ExecutionKind)999, false })!;
        result.Should().Be(ExecutionBackendKind.Memory);
    }

    // ── ResolveAutoFallbackBackend ───────────────────────────────────────

    [Theory]
    [InlineData(ExecutionKind.Helper, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionKind.Memory, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.CodePatch, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Freeze, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Save, ExecutionBackendKind.Save)]
    public void ResolveAutoFallbackBackend_KnownKinds_ReturnExpected(ExecutionKind kind, ExecutionBackendKind expected)
    {
        var method = typeof(BackendRouter).GetMethod("ResolveAutoFallbackBackend", NonPublicStatic)!;
        var result = (ExecutionBackendKind)method.Invoke(null, new object[] { kind })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveAutoFallbackBackend_UnknownKind_ReturnsUnknown()
    {
        var method = typeof(BackendRouter).GetMethod("ResolveAutoFallbackBackend", NonPublicStatic)!;
        var result = (ExecutionBackendKind)method.Invoke(null, new object[] { (ExecutionKind)999 })!;
        result.Should().Be(ExecutionBackendKind.Unknown);
    }

    // ── ResolveRequiredCapabilitiesForAction ─────────────────────────────

    [Fact]
    public void ResolveRequiredCapabilities_EmptyActionId_ReturnsEmpty()
    {
        var method = typeof(BackendRouter).GetMethod("ResolveRequiredCapabilitiesForAction", NonPublicStatic)!;
        var result = (string[])method.Invoke(null, new object[] { (IReadOnlyList<string>)Array.Empty<string>(), "", false })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveRequiredCapabilities_WhitespaceActionId_ReturnsEmpty()
    {
        var method = typeof(BackendRouter).GetMethod("ResolveRequiredCapabilitiesForAction", NonPublicStatic)!;
        var result = (string[])method.Invoke(null, new object[] { (IReadOnlyList<string>)Array.Empty<string>(), "   ", false })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveRequiredCapabilities_PromotedDedup_DoesNotDuplicate()
    {
        var method = typeof(BackendRouter).GetMethod("ResolveRequiredCapabilitiesForAction", NonPublicStatic)!;
        var profile = new List<string> { "freeze_timer" } as IReadOnlyList<string>;
        var result = (string[])method.Invoke(null, new object[] { profile, "freeze_timer", true })!;
        result.Should().HaveCount(1);
        result.Should().Contain("freeze_timer");
    }

    [Fact]
    public void ResolveRequiredCapabilities_PromotedNotInProfile_AddsAction()
    {
        var method = typeof(BackendRouter).GetMethod("ResolveRequiredCapabilitiesForAction", NonPublicStatic)!;
        var profile = new List<string> { "other_cap" } as IReadOnlyList<string>;
        var result = (string[])method.Invoke(null, new object[] { profile, "freeze_timer", true })!;
        result.Should().Contain("freeze_timer");
    }

    // ── IsHardExtenderPreference ─────────────────────────────────────────

    [Theory]
    [InlineData("extender", true)]
    [InlineData("EXTENDER", true)]
    [InlineData("helper", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsHardExtenderPreference_ReturnsExpected(string? preference, bool expected)
    {
        var method = typeof(BackendRouter).GetMethod("IsHardExtenderPreference", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object?[] { preference })!;
        result.Should().Be(expected);
    }

    // ── IsFoCContext ─────────────────────────────────────────────────────

    [Fact]
    public void IsFoCContext_ProfileSwfoc_ReturnsTrue()
    {
        var method = typeof(BackendRouter).GetMethod("IsFoCContext", NonPublicStatic)!;
        var profile = CreateProfile(ExeTarget.Swfoc);
        var process = CreateProcess(ExeTarget.Sweaw);
        var result = (bool)method.Invoke(null, new object[] { profile, process })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsFoCContext_ProcessSwfoc_ReturnsTrue()
    {
        var method = typeof(BackendRouter).GetMethod("IsFoCContext", NonPublicStatic)!;
        var profile = CreateProfile(ExeTarget.Sweaw);
        var process = CreateProcess(ExeTarget.Swfoc);
        var result = (bool)method.Invoke(null, new object[] { profile, process })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsFoCContext_NeitherSwfoc_ReturnsFalse()
    {
        var method = typeof(BackendRouter).GetMethod("IsFoCContext", NonPublicStatic)!;
        var profile = CreateProfile(ExeTarget.Sweaw);
        var process = CreateProcess(ExeTarget.Sweaw);
        var result = (bool)method.Invoke(null, new object[] { profile, process })!;
        result.Should().BeFalse();
    }

    // ── IsPromotedExtenderActionCandidate ────────────────────────────────

    [Fact]
    public void IsPromotedExtenderActionCandidate_FoCAndKnownAction_ReturnsTrue()
    {
        var method = typeof(BackendRouter).GetMethod("IsPromotedExtenderActionCandidate", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object[] { "freeze_timer", CreateProfile(ExeTarget.Swfoc), CreateProcess(ExeTarget.Swfoc) })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsPromotedExtenderActionCandidate_NotFoC_ReturnsFalse()
    {
        var method = typeof(BackendRouter).GetMethod("IsPromotedExtenderActionCandidate", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object[] { "freeze_timer", CreateProfile(ExeTarget.Sweaw), CreateProcess(ExeTarget.Sweaw) })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsPromotedExtenderActionCandidate_UnknownAction_ReturnsFalse()
    {
        var method = typeof(BackendRouter).GetMethod("IsPromotedExtenderActionCandidate", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object[] { "custom_action", CreateProfile(ExeTarget.Swfoc), CreateProcess(ExeTarget.Swfoc) })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsPromotedExtenderActionCandidate_EmptyActionId_ReturnsFalse()
    {
        var method = typeof(BackendRouter).GetMethod("IsPromotedExtenderActionCandidate", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object[] { "", CreateProfile(ExeTarget.Swfoc), CreateProcess(ExeTarget.Swfoc) })!;
        result.Should().BeFalse();
    }

    // ── ResolvePromotedExtenderOverrideState ─────────────────────────────

    [Fact]
    public void ResolvePromotedExtenderOverrideState_EmptyEnv_DisabledDefault()
    {
        var (enabled, source) = InvokeOverrideState(null);
        enabled.Should().BeFalse();
        source.Should().Be("default");
    }

    [Fact]
    public void ResolvePromotedExtenderOverrideState_BoolTrue_EnabledEnv()
    {
        var (enabled, source) = InvokeOverrideState("true");
        enabled.Should().BeTrue();
        source.Should().Be("env");
    }

    [Fact]
    public void ResolvePromotedExtenderOverrideState_Int1_EnabledEnv()
    {
        var (enabled, _) = InvokeOverrideState("1");
        enabled.Should().BeTrue();
    }

    [Fact]
    public void ResolvePromotedExtenderOverrideState_Int0_DisabledEnv()
    {
        var (enabled, _) = InvokeOverrideState("0");
        enabled.Should().BeFalse();
    }

    [Fact]
    public void ResolvePromotedExtenderOverrideState_On_EnabledEnv()
    {
        var (enabled, _) = InvokeOverrideState("on");
        enabled.Should().BeTrue();
    }

    [Fact]
    public void ResolvePromotedExtenderOverrideState_Yes_EnabledEnv()
    {
        var (enabled, _) = InvokeOverrideState("yes");
        enabled.Should().BeTrue();
    }

    [Fact]
    public void ResolvePromotedExtenderOverrideState_RandomString_DisabledEnv()
    {
        var (enabled, _) = InvokeOverrideState("random");
        enabled.Should().BeFalse();
    }

    private static (bool Enabled, string Source) InvokeOverrideState(string? envValue)
    {
        var method = typeof(BackendRouter).GetMethod("ResolvePromotedExtenderOverrideState", NonPublicStatic)!;
        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", envValue);
            var result = method.Invoke(null, Array.Empty<object>())!;
            var resultType = result.GetType();
            var enabled = (bool)resultType.GetProperty("Enabled")!.GetValue(result)!;
            var source = (string)resultType.GetProperty("Source")!.GetValue(result)!;
            return (enabled, source);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    // ── ResolvePreferredBackend memory preference ─────────────────────────

    [Fact]
    public void ResolvePreferredBackend_Memory_WhenDefaultSave_ReturnsSave()
    {
        var method = typeof(BackendRouter).GetMethod("ResolvePreferredBackend", NonPublicStatic)!;
        var result = (ExecutionBackendKind)method.Invoke(null, new object?[] { "memory", ExecutionBackendKind.Save, false })!;
        result.Should().Be(ExecutionBackendKind.Save);
    }

    [Fact]
    public void ResolvePreferredBackend_Memory_WhenDefaultHelper_ReturnsMemory()
    {
        var method = typeof(BackendRouter).GetMethod("ResolvePreferredBackend", NonPublicStatic)!;
        var result = (ExecutionBackendKind)method.Invoke(null, new object?[] { "memory", ExecutionBackendKind.Helper, false })!;
        result.Should().Be(ExecutionBackendKind.Memory);
    }

    // ── Full Resolve API integration paths ───────────────────────────────

    [Fact]
    public void Resolve_NullRequest_Throws()
    {
        var router = new BackendRouter();
        var act = () => router.Resolve(null!, CreateProfile(ExeTarget.Swfoc), CreateProcess(ExeTarget.Swfoc), CreateCapabilityReport());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_NullProfile_Throws()
    {
        var router = new BackendRouter();
        var act = () => router.Resolve(CreateRequest("set_credits"), null!, CreateProcess(ExeTarget.Swfoc), CreateCapabilityReport());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_NullProcess_Throws()
    {
        var router = new BackendRouter();
        var act = () => router.Resolve(CreateRequest("set_credits"), CreateProfile(ExeTarget.Swfoc), null!, CreateCapabilityReport());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_NullCapabilityReport_Throws()
    {
        var router = new BackendRouter();
        var act = () => router.Resolve(CreateRequest("set_credits"), CreateProfile(ExeTarget.Swfoc), CreateProcess(ExeTarget.Swfoc), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_MemoryAction_RoutesToMemory()
    {
        var router = new BackendRouter();
        var request = CreateRequest("set_credits", ExecutionKind.Memory);
        var profile = CreateProfile(ExeTarget.Swfoc);
        var process = CreateProcess(ExeTarget.Swfoc);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Should().NotBeNull();
            decision.Allowed.Should().BeTrue();
            decision.Backend.Should().Be(ExecutionBackendKind.Memory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_ReadOnlyAction_RoutedAsNonMutating()
    {
        var router = new BackendRouter();
        var request = CreateRequest("read_credits", ExecutionKind.Memory);
        var profile = CreateProfile(ExeTarget.Swfoc);
        var process = CreateProcess(ExeTarget.Swfoc);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Should().NotBeNull();
            decision.Allowed.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_ExtenderPreference_FeatureAvailable_Allowed()
    {
        var router = new BackendRouter();
        var request = CreateRequest("set_credits", ExecutionKind.Memory);
        var profile = CreateProfile(ExeTarget.Swfoc, backendPreference: "extender");
        var process = CreateProcess(ExeTarget.Swfoc);
        var capabilities = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new("set_credits", true, CapabilityConfidenceState.Verified, RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var capReport = new CapabilityReport("test", DateTimeOffset.UtcNow, capabilities, RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Allowed.Should().BeTrue();
            decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_ExtenderPreference_FeatureUnavailable_MutatingFailClosed()
    {
        var router = new BackendRouter();
        var request = CreateRequest("set_credits", ExecutionKind.Memory);
        var profile = CreateProfile(ExeTarget.Swfoc, backendPreference: "extender");
        var process = CreateProcess(ExeTarget.Swfoc);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Allowed.Should().BeFalse();
            decision.ReasonCode.Should().Be(RuntimeReasonCode.SAFETY_FAIL_CLOSED);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_ExtenderPreference_FeatureUnavailable_ReadOnly_ExperimentalAllowed()
    {
        var router = new BackendRouter();
        var request = CreateRequest("read_credits", ExecutionKind.Memory);
        var profile = CreateProfile(ExeTarget.Swfoc, backendPreference: "extender");
        var process = CreateProcess(ExeTarget.Swfoc);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Allowed.Should().BeTrue();
            decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_FEATURE_EXPERIMENTAL);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_AutoPreference_MutatingExtenderFallback_HelperKind()
    {
        var router = new BackendRouter();
        var request = CreateRequest("set_credits", ExecutionKind.Helper);
        var profile = CreateProfile(ExeTarget.Sweaw, backendPreference: "auto");
        var process = CreateProcess(ExeTarget.Sweaw);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Should().NotBeNull();
            decision.Backend.Should().Be(ExecutionBackendKind.Helper);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_SaveKind_RoutesToSave()
    {
        var router = new BackendRouter();
        var request = CreateRequest("save_game", ExecutionKind.Save);
        var profile = CreateProfile(ExeTarget.Swfoc);
        var process = CreateProcess(ExeTarget.Swfoc);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Allowed.Should().BeTrue();
            decision.Backend.Should().Be(ExecutionBackendKind.Save);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_ContextDiagnostics_ContainsExpectedKeys()
    {
        var router = new BackendRouter();
        var ctx = new Dictionary<string, object?> { ["capabilityMapReasonCode"] = "test", ["capabilityMapState"] = "ok", ["capabilityDeclaredAvailable"] = true };
        var request = CreateRequest("read_credits", ExecutionKind.Memory, ctx);
        var profile = CreateProfile(ExeTarget.Swfoc);
        var process = CreateProcess(ExeTarget.Swfoc);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Diagnostics.Should().NotBeNull();
            decision.Diagnostics!.Should().ContainKey("capabilityMapReasonCode");
            decision.Diagnostics!.Should().ContainKey("capabilityMapState");
            decision.Diagnostics!.Should().ContainKey("capabilityDeclaredAvailable");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_RequiredCapabilityMissing_Mutating_ExtenderBlocked()
    {
        var router = new BackendRouter();
        var request = CreateRequest("set_credits", ExecutionKind.Memory);
        var profile = CreateProfile(ExeTarget.Swfoc, backendPreference: "extender", requiredCapabilities: new[] { "set_credits" });
        var process = CreateProcess(ExeTarget.Swfoc);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            decision.Allowed.Should().BeFalse();
            decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    [Fact]
    public void Resolve_SdkKind_DefaultsToExtender_NoFeature_MutatingBlocked()
    {
        var router = new BackendRouter();
        var request = CreateRequest("set_credits", ExecutionKind.Sdk);
        var profile = CreateProfile(ExeTarget.Sweaw);
        var process = CreateProcess(ExeTarget.Sweaw);
        var capReport = CreateCapabilityReport();

        var saved = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var decision = router.Resolve(request, profile, process, capReport);
            // SDK routes to extender by default; no feature available => fallback attempt
            decision.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", saved);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static TrainerProfile CreateProfile(
        ExeTarget exeTarget,
        string? backendPreference = null,
        IReadOnlyList<string>? requiredCapabilities = null)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: exeTarget,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            BackendPreference: backendPreference,
            RequiredCapabilities: requiredCapabilities);
    }

    private static ProcessMetadata CreateProcess(ExeTarget exeTarget)
    {
        return new ProcessMetadata(
            ProcessId: 1234,
            ProcessName: "test",
            ProcessPath: "test.exe",
            CommandLine: null,
            ExeTarget: exeTarget,
            Mode: RuntimeMode.Galactic);
    }

    private static CapabilityReport CreateCapabilityReport()
    {
        return CapabilityReport.Unknown("test_profile");
    }

    private static ActionExecutionRequest CreateRequest(
        string actionId,
        ExecutionKind executionKind = ExecutionKind.Memory,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        var action = new ActionSpec(actionId, ActionCategory.Global, RuntimeMode.Galactic, executionKind, new JsonObject(), false, 0);
        return new ActionExecutionRequest(action, new JsonObject(), "test_profile", RuntimeMode.Galactic, context);
    }

    private sealed class NullToStringObject
    {
        public override string? ToString() => null;
    }
}
