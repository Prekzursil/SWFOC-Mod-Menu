using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage sweep for BackendRouter — targets the ~66 uncovered branches
/// in route resolution, backend preference, promoted extender override, mutating checks,
/// required capabilities, and fallback logic.
/// </summary>
public sealed class BackendRouterBranchCoverageTests
{
    // ── IsMutating branches ────────────────────────────────────────────────

    [Theory]
    [InlineData("read_credits", false)]
    [InlineData("list_units", false)]
    [InlineData("get_status", false)]
    [InlineData("set_credits", true)]
    [InlineData("toggle_fog", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void IsMutating_ShouldClassifyCorrectly(string actionId, bool expectedMutating)
    {
        var method = typeof(BackendRouter).GetMethod(
            "IsMutating",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { actionId })!;
        result.Should().Be(expectedMutating);
    }

    // ── ResolvePreferredBackend branches ────────────────────────────────────

    [Fact]
    public void ResolvePreferredBackend_ShouldReturnExtender_WhenForceExtenderGate()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolvePreferredBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object?[] { "auto", ExecutionBackendKind.Memory, true })!;
        result.Should().Be(ExecutionBackendKind.Extender);
    }

    [Fact]
    public void ResolvePreferredBackend_ShouldReturnExtender_WhenBackendPreferenceIsExtender()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolvePreferredBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object?[] { "extender", ExecutionBackendKind.Memory, false })!;
        result.Should().Be(ExecutionBackendKind.Extender);
    }

    [Theory]
    [InlineData(ExecutionBackendKind.Save, ExecutionBackendKind.Save)]
    [InlineData(ExecutionBackendKind.Memory, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionBackendKind.Helper, ExecutionBackendKind.Helper)]
    public void ResolvePreferredBackend_ShouldResolveHelperPreference(ExecutionBackendKind defaultBackend, ExecutionBackendKind expected)
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolvePreferredBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object?[] { "helper", defaultBackend, false })!;
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ExecutionBackendKind.Save, ExecutionBackendKind.Save)]
    [InlineData(ExecutionBackendKind.Memory, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionBackendKind.Helper, ExecutionBackendKind.Memory)]
    public void ResolvePreferredBackend_ShouldResolveMemoryPreference(ExecutionBackendKind defaultBackend, ExecutionBackendKind expected)
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolvePreferredBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object?[] { "memory", defaultBackend, false })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolvePreferredBackend_ShouldReturnDefault_WhenAutoPreference()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolvePreferredBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object?[] { "auto", ExecutionBackendKind.Memory, false })!;
        result.Should().Be(ExecutionBackendKind.Memory);
    }

    // ── MapDefaultBackend branches ─────────────────────────────────────────

    [Fact]
    public void MapDefaultBackend_ShouldReturnExtender_WhenForceExtenderGate()
    {
        var method = typeof(BackendRouter).GetMethod(
            "MapDefaultBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object[] { ExecutionKind.Memory, true })!;
        result.Should().Be(ExecutionBackendKind.Extender);
    }

    [Theory]
    [InlineData(ExecutionKind.Helper, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionKind.Save, ExecutionBackendKind.Save)]
    [InlineData(ExecutionKind.Memory, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.CodePatch, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Freeze, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Sdk, ExecutionBackendKind.Extender)]
    public void MapDefaultBackend_ShouldMapExecutionKindCorrectly(ExecutionKind kind, ExecutionBackendKind expected)
    {
        var method = typeof(BackendRouter).GetMethod(
            "MapDefaultBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object[] { kind, false })!;
        result.Should().Be(expected);
    }

    // ── ResolveAutoFallbackBackend branches ────────────────────────────────

    [Theory]
    [InlineData(ExecutionKind.Helper, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionKind.Memory, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.CodePatch, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Freeze, ExecutionBackendKind.Memory)]
    [InlineData(ExecutionKind.Save, ExecutionBackendKind.Save)]
    public void ResolveAutoFallbackBackend_ShouldMapCorrectly(ExecutionKind kind, ExecutionBackendKind expected)
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolveAutoFallbackBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object[] { kind })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveAutoFallbackBackend_ShouldReturnUnknown_ForSdkKind()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolveAutoFallbackBackend",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExecutionBackendKind)method!.Invoke(null, new object[] { ExecutionKind.Sdk })!;
        result.Should().Be(ExecutionBackendKind.Unknown);
    }

    // ── IsHardExtenderPreference branches ──────────────────────────────────

    [Theory]
    [InlineData("extender", true)]
    [InlineData("EXTENDER", true)]
    [InlineData("auto", false)]
    [InlineData("helper", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsHardExtenderPreference_ShouldClassifyCorrectly(string? preference, bool expected)
    {
        var method = typeof(BackendRouter).GetMethod(
            "IsHardExtenderPreference",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object?[] { preference })!;
        result.Should().Be(expected);
    }

    // ── ReadContextString branches ─────────────────────────────────────────

    [Fact]
    public void ReadContextString_ShouldReturnNull_WhenContextIsNull()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextString",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { null, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextString_ShouldReturnNull_WhenKeyMissing()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextString",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["other"] = "value" };
        var result = method!.Invoke(null, new object?[] { context, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextString_ShouldReturnNull_WhenValueIsNull()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextString",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = null };
        var result = method!.Invoke(null, new object?[] { context, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextString_ShouldReturnStringValue_WhenPresent()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextString",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = "hello" };
        var result = (string?)method!.Invoke(null, new object?[] { context, "key" });
        result.Should().Be("hello");
    }

    [Fact]
    public void ReadContextString_ShouldCallToString_WhenNotAString()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextString",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = 42 };
        var result = (string?)method!.Invoke(null, new object?[] { context, "key" });
        result.Should().Be("42");
    }

    // ── ReadContextBool branches ───────────────────────────────────────────

    [Fact]
    public void ReadContextBool_ShouldReturnNull_WhenContextIsNull()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextBool",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { null, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextBool_ShouldReturnBoolDirectly_WhenValueIsBool()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextBool",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = true };
        var result = (bool?)method!.Invoke(null, new object?[] { context, "key" });
        result.Should().BeTrue();
    }

    [Fact]
    public void ReadContextBool_ShouldParseTrueString()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextBool",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = "true" };
        var result = (bool?)method!.Invoke(null, new object?[] { context, "key" });
        result.Should().BeTrue();
    }

    [Fact]
    public void ReadContextBool_ShouldReturnNull_ForUnparsableString()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextBool",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = "maybe" };
        var result = (bool?)method!.Invoke(null, new object?[] { context, "key" });
        result.Should().BeNull();
    }

    [Fact]
    public void ReadContextBool_ShouldReturnNull_WhenValueIsNull()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ReadContextBool",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = null };
        var result = (bool?)method!.Invoke(null, new object?[] { context, "key" });
        result.Should().BeNull();
    }

    // ── ResolvePromotedExtenderOverrideState branches ──────────────────────

    [Fact]
    public void ResolvePromotedExtenderOverrideState_ShouldReturnDefault_WhenEnvNotSet()
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", null);
            var method = typeof(BackendRouter).GetMethod(
                "ResolvePromotedExtenderOverrideState",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)result.GetType().GetProperty("Enabled")!.GetValue(result)!;
            var source = (string)result.GetType().GetProperty("Source")!.GetValue(result)!;
            enabled.Should().BeFalse();
            source.Should().Be("default");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", prev);
        }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ResolvePromotedExtenderOverrideState_ShouldParseBool(string value, bool expectedEnabled)
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", value);
            var method = typeof(BackendRouter).GetMethod(
                "ResolvePromotedExtenderOverrideState",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)result.GetType().GetProperty("Enabled")!.GetValue(result)!;
            var source = (string)result.GetType().GetProperty("Source")!.GetValue(result)!;
            enabled.Should().Be(expectedEnabled);
            source.Should().Be("env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", prev);
        }
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void ResolvePromotedExtenderOverrideState_ShouldParseInt(string value, bool expectedEnabled)
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", value);
            var method = typeof(BackendRouter).GetMethod(
                "ResolvePromotedExtenderOverrideState",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)result.GetType().GetProperty("Enabled")!.GetValue(result)!;
            enabled.Should().Be(expectedEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", prev);
        }
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("yes", true)]
    [InlineData("off", false)]
    [InlineData("no", false)]
    [InlineData("something", false)]
    public void ResolvePromotedExtenderOverrideState_ShouldParseTextValues(string value, bool expectedEnabled)
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", value);
            var method = typeof(BackendRouter).GetMethod(
                "ResolvePromotedExtenderOverrideState",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)result.GetType().GetProperty("Enabled")!.GetValue(result)!;
            enabled.Should().Be(expectedEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_FORCE_PROMOTED_EXTENDER", prev);
        }
    }

    // ── IsFoCContext branches ──────────────────────────────────────────────

    [Fact]
    public void IsFoCContext_ShouldReturnTrue_WhenProfileExeTargetIsSwfoc()
    {
        var method = typeof(BackendRouter).GetMethod(
            "IsFoCContext",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = BuildProfile("auto", exeTarget: ExeTarget.Swfoc);
        var process = BuildProcess(exeTarget: ExeTarget.Sweaw);
        var result = (bool)method!.Invoke(null, new object[] { profile, process })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsFoCContext_ShouldReturnTrue_WhenProcessExeTargetIsSwfoc()
    {
        var method = typeof(BackendRouter).GetMethod(
            "IsFoCContext",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = BuildProfile("auto", exeTarget: ExeTarget.Sweaw);
        var process = BuildProcess(exeTarget: ExeTarget.Swfoc);
        var result = (bool)method!.Invoke(null, new object[] { profile, process })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsFoCContext_ShouldReturnFalse_WhenBothAreSweaw()
    {
        var method = typeof(BackendRouter).GetMethod(
            "IsFoCContext",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = BuildProfile("auto", exeTarget: ExeTarget.Sweaw);
        var process = BuildProcess(exeTarget: ExeTarget.Sweaw);
        var result = (bool)method!.Invoke(null, new object[] { profile, process })!;
        result.Should().BeFalse();
    }

    // ── ResolveRequiredCapabilitiesForAction branches ──────────────────────

    [Fact]
    public void ResolveRequiredCapabilitiesForAction_ShouldReturnEmpty_WhenActionIdIsBlank()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolveRequiredCapabilitiesForAction",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object[] { Array.Empty<string>(), "  ", false })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveRequiredCapabilitiesForAction_ShouldAddActionId_WhenPromotedAndNotAlreadyRequired()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolveRequiredCapabilitiesForAction",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object[] { new[] { "other_cap" }, "freeze_timer", true })!;
        result.Should().Contain("freeze_timer");
    }

    [Fact]
    public void ResolveRequiredCapabilitiesForAction_ShouldNotDuplicate_WhenAlreadyInRequired()
    {
        var method = typeof(BackendRouter).GetMethod(
            "ResolveRequiredCapabilitiesForAction",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object[] { new[] { "freeze_timer" }, "freeze_timer", true })!;
        result.Should().HaveCount(1);
    }

    // ── Resolve integration: extender route with read-only action ──────────

    [Fact]
    public void Resolve_ShouldAllowExperimentalReadOnly_WhenCapabilityMissing()
    {
        using var _ = new EnvScope("SWFOC_FORCE_PROMOTED_EXTENDER", null);
        var router = new BackendRouter();
        var request = BuildRequest("read_credits", ExecutionKind.Sdk);
        var profile = BuildProfile("extender");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_FEATURE_EXPERIMENTAL);
    }

    // ── Resolve integration: fallback when extender unavailable ────────────

    [Fact]
    public void Resolve_ShouldFallbackToHelper_WhenExtenderCapabilityUnavailable_AndExecutionKindIsHelper()
    {
        using var _ = new EnvScope("SWFOC_FORCE_PROMOTED_EXTENDER", null);
        var router = new BackendRouter();
        var request = BuildRequest("set_hero_state_helper", ExecutionKind.Helper, profileId: "profile");
        var profile = BuildProfile("auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Helper);
    }

    // ── Resolve integration: required capability contract ──────────────────

    [Fact]
    public void Resolve_ShouldReturn_Null_FromRequiredCapabilityContract_WhenNoMissingAndNoMutation()
    {
        var method = typeof(BackendRouter).GetMethod(
            "TryResolveRequiredCapabilityContract",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var result = method!.Invoke(null, new object?[] {
            ExecutionBackendKind.Extender,
            "extender",
            true,
            Array.Empty<string>(),
            diagnostics,
            false
        });
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturn_Null_FromRequiredCapabilityContract_WhenNotMutating()
    {
        var method = typeof(BackendRouter).GetMethod(
            "TryResolveRequiredCapabilityContract",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var result = method!.Invoke(null, new object?[] {
            ExecutionBackendKind.Extender,
            "extender",
            false,
            new[] { "missing_cap" },
            diagnostics,
            false
        });
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturn_Null_FromRequiredCapabilityContract_WhenNotEnforcedBackend()
    {
        var method = typeof(BackendRouter).GetMethod(
            "TryResolveRequiredCapabilityContract",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var result = method!.Invoke(null, new object?[] {
            ExecutionBackendKind.Memory,
            "auto",
            true,
            new[] { "missing_cap" },
            diagnostics,
            false
        });
        result.Should().BeNull();
    }

    // ── Resolve integration: non-extender preferred route ─────────────────

    [Fact]
    public void Resolve_ShouldRouteToMemory_WhenPreferredBackendIsMemory()
    {
        using var _ = new EnvScope("SWFOC_FORCE_PROMOTED_EXTENDER", null);
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Memory);
        var profile = BuildProfile("memory");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
    }

    // ── Resolve: mutating action blocked with no safe fallback ─────────────

    [Fact]
    public void Resolve_ShouldBlockMutatingAction_WhenNoSafeFallback()
    {
        using var _ = new EnvScope("SWFOC_FORCE_PROMOTED_EXTENDER", null);
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Sdk);
        var profile = BuildProfile("auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(RuntimeReasonCode.SAFETY_MUTATION_BLOCKED);
    }

    // ── Resolve: mutating with fallback to memory ─────────────────────────

    [Fact]
    public void Resolve_ShouldFallbackToMemory_WhenExtenderUnavailable_AndExecutionKindIsMemory()
    {
        using var _ = new EnvScope("SWFOC_FORCE_PROMOTED_EXTENDER", null);
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Memory);
        var profile = BuildProfile("auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
    }

    // ── Helper builders ────────────────────────────────────────────────────

    private static ActionExecutionRequest BuildRequest(
        string actionId,
        ExecutionKind executionKind,
        string profileId = "roe_3447786229_swfoc",
        IReadOnlyDictionary<string, object?>? context = null)
    {
        var action = new ActionSpec(
            Id: actionId,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: executionKind,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0,
            Description: "test");
        return new ActionExecutionRequest(action, new JsonObject(), profileId, RuntimeMode.Galactic, context);
    }

    private static TrainerProfile BuildProfile(
        string backendPreference,
        IReadOnlyList<string>? requiredCapabilities = null,
        string id = "roe_3447786229_swfoc",
        ExeTarget exeTarget = ExeTarget.Swfoc)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: "ROE",
            Inherits: null,
            ExeTarget: exeTarget,
            SteamWorkshopId: "3447786229",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(),
            BackendPreference: backendPreference,
            RequiredCapabilities: requiredCapabilities,
            HostPreference: "starwarsg_preferred",
            ExperimentalFeatures: Array.Empty<string>());
    }

    private static ProcessMetadata BuildProcess(
        ExeTarget exeTarget = ExeTarget.Swfoc,
        string processName = "StarWarsG.exe",
        string processPath = "C:/Games/Corruption/StarWarsG.exe",
        string commandLine = "StarWarsG.exe STEAMMOD=3447786229")
    {
        return new ProcessMetadata(
            ProcessId: 1234,
            ProcessName: processName,
            ProcessPath: processPath,
            CommandLine: commandLine,
            ExeTarget: exeTarget,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(),
            LaunchContext: null,
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 321_000_000,
            WorkshopMatchCount: 1,
            SelectionScore: 1311.0d);
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
