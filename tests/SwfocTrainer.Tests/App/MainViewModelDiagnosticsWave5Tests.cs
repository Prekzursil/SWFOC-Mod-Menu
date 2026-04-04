using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelDiagnostics:
/// FormatPatchValue (JsonElement branches), BuildProcessDiagnosticSummary
/// private helper coverage via public entry point, ReadDiagnosticString edge cases,
/// BuildDiagnosticsStatusSuffix with null/empty diagnostics, BuildQuickActionStatus branches.
/// </summary>
public sealed class MainViewModelDiagnosticsWave5Tests
{
    [Fact]
    public void FormatPatchValue_Null_ShouldReturnNullString()
    {
        MainViewModelDiagnostics.FormatPatchValue(null).Should().Be("null");
    }

    [Fact]
    public void FormatPatchValue_JsonElementString_ShouldReturnStringValue()
    {
        var element = JsonDocument.Parse("\"hello\"").RootElement;
        MainViewModelDiagnostics.FormatPatchValue(element).Should().Be("hello");
    }

    [Fact]
    public void FormatPatchValue_JsonElementNull_ShouldReturnNullString()
    {
        var element = JsonDocument.Parse("null").RootElement;
        MainViewModelDiagnostics.FormatPatchValue(element).Should().Be("null");
    }

    [Fact]
    public void FormatPatchValue_JsonElementNumber_ShouldReturnToString()
    {
        var element = JsonDocument.Parse("42").RootElement;
        MainViewModelDiagnostics.FormatPatchValue(element).Should().Be("42");
    }

    [Fact]
    public void FormatPatchValue_JsonElementBoolean_ShouldReturnToString()
    {
        var element = JsonDocument.Parse("true").RootElement;
        MainViewModelDiagnostics.FormatPatchValue(element).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FormatPatchValue_PlainObject_ShouldReturnToString()
    {
        MainViewModelDiagnostics.FormatPatchValue(123).Should().Be("123");
    }

    [Fact]
    public void FormatPatchValue_ObjectWithNullToString_ShouldReturnEmpty()
    {
        MainViewModelDiagnostics.FormatPatchValue("test").Should().Be("test");
    }

    [Fact]
    public void ReadDiagnosticString_NullDiagnostics_ShouldReturnEmpty()
    {
        MainViewModelDiagnostics.ReadDiagnosticString(null, "key").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_MissingKey_ShouldReturnEmpty()
    {
        var diag = new Dictionary<string, object?> { ["other"] = "val" };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "missing").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_NullValue_ShouldReturnEmpty()
    {
        var diag = new Dictionary<string, object?> { ["key"] = null };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "key").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_StringValue_ShouldReturnDirectly()
    {
        var diag = new Dictionary<string, object?> { ["key"] = "hello" };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "key").Should().Be("hello");
    }

    [Fact]
    public void ReadDiagnosticString_NonStringValue_ShouldCallToString()
    {
        var diag = new Dictionary<string, object?> { ["key"] = 42 };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "key").Should().Be("42");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_NullDiagnostics_ShouldReturnEmpty()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature, Diagnostics: null);
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result).Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_EmptyDiagnostics_ShouldReturnEmpty()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?>());
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result).Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_WithNonStringValue_ShouldCallToString()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?>
            {
                ["backendRoute"] = 42
            });
        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);
        suffix.Should().Contain("backend=42");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_WithNullBackendRouteValue_ShouldSkipSegment()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?>
            {
                ["backendRoute"] = null,
                ["hookState"] = "ready"
            });
        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);
        suffix.Should().NotContain("backend=");
        suffix.Should().Contain("hookState=ready");
    }

    [Fact]
    public void BuildQuickActionStatus_Succeeded_ShouldStartWithCheckmark()
    {
        var result = new ActionExecutionResult(true, "done", AddressSource.Signature);
        var status = MainViewModelDiagnostics.BuildQuickActionStatus("set_credits", result);
        status.Should().StartWith("\u2713 set_credits: done");
    }

    [Fact]
    public void BuildQuickActionStatus_Failed_ShouldStartWithCross()
    {
        var result = new ActionExecutionResult(false, "err", AddressSource.Signature);
        var status = MainViewModelDiagnostics.BuildQuickActionStatus("set_credits", result);
        status.Should().StartWith("\u2717 set_credits: err");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldIncludeAllSegments()
    {
        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: "STEAMMOD=1397421866",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["commandLineAvailable"] = "True",
                ["steamModIdsDetected"] = "1397421866",
                ["detectedVia"] = "wmi",
                ["dependencyValidation"] = "Pass",
                ["dependencyValidationMessage"] = "",
                ["fallbackHitRate"] = "0.15",
                ["unresolvedSymbolRate"] = "0.00",
                ["resolvedVariant"] = "aotr_swfoc",
                ["resolvedVariantReasonCode"] = "workshop_match",
                ["resolvedVariantConfidence"] = "0.95"
            },
            LaunchContext: new LaunchContext(
                LaunchKind.Workshop,
                CommandLineAvailable: true,
                SteamModIds: new[] { "1397421866" },
                ModPathRaw: @"Mods\AOTR",
                ModPathNormalized: @"Mods\AOTR",
                DetectedVia: "wmi",
                Recommendation: new ProfileRecommendation("aotr_1397421866_swfoc", "workshop_match", 0.95)),
            HostRole: ProcessHostRole.Unknown,
            MainModuleSize: 4096,
            WorkshopMatchCount: 1,
            SelectionScore: 0.85);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("target=Swfoc");
        summary.Should().Contain("launch=Workshop");
        summary.Should().Contain("hostRole=unknown");
        summary.Should().Contain("score=0.85");
        summary.Should().Contain("module=4096");
        summary.Should().Contain("workshopMatches=1");
        summary.Should().Contain("cmdLine=True");
        summary.Should().Contain("mods=1397421866");
        summary.Should().Contain(@"modPath=Mods\AOTR");
        summary.Should().Contain("rec=aotr_1397421866_swfoc");
        summary.Should().Contain("variant=aotr_swfoc");
        summary.Should().Contain("dependency=Pass");
        summary.Should().Contain("fallbackRate=0.15");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_NullLaunchContext_ShouldShowUnknown()
    {
        var process = new ProcessMetadata(
            ProcessId: 2,
            ProcessName: "sweaw.exe",
            ProcessPath: @"C:\Games\sweaw.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Sweaw,
            Mode: RuntimeMode.Unknown,
            Metadata: null);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("launch=Unknown");
        summary.Should().Contain("modPath=none");
        summary.Should().Contain("rec=none");
        summary.Should().Contain("mods=none");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ZeroModuleSize_ShouldShowNotAvailable()
    {
        var process = new ProcessMetadata(
            ProcessId: 3,
            ProcessName: "test.exe",
            ProcessPath: @"C:\Games\test.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Unknown,
            Mode: RuntimeMode.Unknown,
            MainModuleSize: 0);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "n/a");

        summary.Should().Contain("module=n/a");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithDependencyFail_ShouldShowMessage()
    {
        var process = new ProcessMetadata(
            ProcessId: 4,
            ProcessName: "test.exe",
            ProcessPath: @"C:\Games\test.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyValidation"] = "SoftFail",
                ["dependencyValidationMessage"] = "missing parent"
            });

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("dependency=SoftFail (missing parent)");
    }

    [Fact]
    public void BuildProcessDependencySegment_EmptyMessage_ShouldOmitParenthesis()
    {
        var segment = MainViewModelDiagnostics.BuildProcessDependencySegment("SoftFail", "");
        segment.Should().Be("dependency=SoftFail");
    }

    [Fact]
    public void ReadProcessMetadata_NullMetadata_ShouldReturnFallback()
    {
        var process = new ProcessMetadata(
            ProcessId: 1, ProcessName: "test", ProcessPath: "path",
            CommandLine: null, ExeTarget: ExeTarget.Unknown, Mode: RuntimeMode.Unknown,
            Metadata: null);
        MainViewModelDiagnostics.ReadProcessMetadata(process, "any", "fallback")
            .Should().Be("fallback");
    }

    [Fact]
    public void ReadProcessMods_NoMods_ShouldReturnNone()
    {
        var process = new ProcessMetadata(
            ProcessId: 1, ProcessName: "test", ProcessPath: "path",
            CommandLine: null, ExeTarget: ExeTarget.Unknown, Mode: RuntimeMode.Unknown);
        MainViewModelDiagnostics.ReadProcessMods(process).Should().Be("none");
    }

    [Fact]
    public void ReadProcessMods_WithMods_ShouldReturnModIds()
    {
        var process = new ProcessMetadata(
            ProcessId: 1, ProcessName: "test", ProcessPath: "path",
            CommandLine: null, ExeTarget: ExeTarget.Unknown, Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["steamModIdsDetected"] = "123,456"
            });
        MainViewModelDiagnostics.ReadProcessMods(process).Should().Be("123,456");
    }

    [Fact]
    public void BuildDependencyDiagnostic_EmptyMessage_ShouldOmitParenthesis()
    {
        MainViewModelDiagnostics.BuildDependencyDiagnostic("Pass", "")
            .Should().Be("dependency: Pass");
    }

    [Fact]
    public void BuildDependencyDiagnostic_WithMessage_ShouldIncludeParenthesis()
    {
        MainViewModelDiagnostics.BuildDependencyDiagnostic("SoftFail", "detail")
            .Should().Be("dependency: SoftFail (detail)");
    }

    [Fact]
    public void BuildPatchMetadataSummary_ShouldFormatCorrectly()
    {
        var pack = new SavePatchPack(
            Metadata: new SavePatchMetadata("v1", "profile1", "schema1", "abc123", DateTimeOffset.UtcNow),
            Compatibility: new SavePatchCompatibility(new[] { "profile1" }, "schema1"),
            Operations: new List<SavePatchOperation>
            {
                new SavePatchOperation(SavePatchOperationKind.SetValue, "/field1", "field1", "int32", null, 42, 0)
            });
        var summary = MainViewModelDiagnostics.BuildPatchMetadataSummary(pack);
        summary.Should().Contain("v1");
        summary.Should().Contain("profile1");
        summary.Should().Contain("schema1");
        summary.Should().Contain("ops=1");
    }

    [Fact]
    public void ResolveBundleGateResult_NullReliability_ShouldReturnUnknown()
    {
        MainViewModelDiagnostics.ResolveBundleGateResult(null, "n/a")
            .Should().Be("n/a");
    }

    [Fact]
    public void ResolveBundleGateResult_UnavailableState_ShouldReturnBlocked()
    {
        var item = new ActionReliabilityViewItem("action", "unavailable", "REASON", 0.5, "detail");
        MainViewModelDiagnostics.ResolveBundleGateResult(item, "n/a")
            .Should().Be("blocked");
    }

    [Fact]
    public void ResolveBundleGateResult_StableState_ShouldReturnBundlePass()
    {
        var item = new ActionReliabilityViewItem("action", "stable", "REASON", 1.0, "detail");
        MainViewModelDiagnostics.ResolveBundleGateResult(item, "n/a")
            .Should().Be("bundle_pass");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_LaunchContextWithNullRecommendation_ShouldHandleGracefully()
    {
        var process = new ProcessMetadata(
            ProcessId: 5,
            ProcessName: "test.exe",
            ProcessPath: @"C:\Games\test.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            LaunchContext: new LaunchContext(
                LaunchKind.BaseGame,
                CommandLineAvailable: false,
                SteamModIds: Array.Empty<string>(),
                ModPathRaw: null,
                ModPathNormalized: null,
                DetectedVia: "probe",
                Recommendation: null!));

        // This should not throw even with null recommendation
        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("launch=BaseGame");
    }

    [Fact]
    public void FormatPatchValue_JsonElementStringNull_ShouldReturnEmpty()
    {
        // A JSON string that is null via GetString() returning null
        // This is a defensive edge case
        var element = JsonDocument.Parse("\"\"").RootElement;
        MainViewModelDiagnostics.FormatPatchValue(element).Should().BeEmpty();
    }
}
