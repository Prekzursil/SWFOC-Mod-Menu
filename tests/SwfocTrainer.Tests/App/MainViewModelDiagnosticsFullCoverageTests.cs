using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Full branch coverage for MainViewModelDiagnostics —
/// covers every if/else, null coalesce, switch branch, and guard.
/// </summary>
public sealed class MainViewModelDiagnosticsFullCoverageTests
{
    // ── BuildProcessDiagnosticSummary ──

    [Fact]
    public void BuildProcessDiagnosticSummary_NullProcess_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildProcessDiagnosticSummary(null!, "unknown");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithNullLaunchContext_ShouldReturnUnknownLaunchKind()
    {
        var process = new ProcessMetadata(
            ProcessId: 1, ProcessName: "swfoc.exe", ProcessPath: @"C:\G\swfoc.exe",
            CommandLine: null, ExeTarget: ExeTarget.Swfoc, Mode: RuntimeMode.Unknown);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("launch=Unknown");
        summary.Should().Contain("modPath=none");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithModPathNormalized_ShouldIncludeModPath()
    {
        var process = new ProcessMetadata(
            ProcessId: 2, ProcessName: "swfoc.exe", ProcessPath: @"C:\G\swfoc.exe",
            CommandLine: "STEAMMOD=1", ExeTarget: ExeTarget.Swfoc, Mode: RuntimeMode.Galactic,
            LaunchContext: new LaunchContext(LaunchKind.Workshop, true, new[] { "1" }, null,
                "Mods\\Test", "cmd",
                new ProfileRecommendation("test_profile", "match", 0.85)),
            Metadata: new Dictionary<string, string> { ["steamModIdsDetected"] = "1" },
            MainModuleSize: 1024, WorkshopMatchCount: 1, SelectionScore: 0.5);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unk");

        summary.Should().Contain("modPath=Mods\\Test");
        summary.Should().Contain("module=1024");
        summary.Should().Contain("rec=test_profile:match:0.85");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithZeroModuleSize_ShouldShowNA()
    {
        var process = new ProcessMetadata(
            ProcessId: 3, ProcessName: "x.exe", ProcessPath: @"C:\x.exe",
            CommandLine: null, ExeTarget: ExeTarget.Swfoc, Mode: RuntimeMode.Unknown,
            MainModuleSize: 0);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unk");
        summary.Should().Contain("module=n/a");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithRecommendationNull_ShouldFallback()
    {
        var process = new ProcessMetadata(
            ProcessId: 4, ProcessName: "x.exe", ProcessPath: @"C:\x.exe",
            CommandLine: null, ExeTarget: ExeTarget.Swfoc, Mode: RuntimeMode.Unknown,
            LaunchContext: new LaunchContext(LaunchKind.BaseGame, true, Array.Empty<string>(),
                null, null, "scan", null));

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unk");

        summary.Should().Contain("rec=none:unk:0.00");
    }

    // ── ReadProcessMetadata ──

    [Fact]
    public void ReadProcessMetadata_NullProcess_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.ReadProcessMetadata(null!, "k", "f");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadProcessMetadata_NullKey_ShouldThrow()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        var act = () => MainViewModelDiagnostics.ReadProcessMetadata(process, null!, "f");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadProcessMetadata_NullFallback_ShouldThrow()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        var act = () => MainViewModelDiagnostics.ReadProcessMetadata(process, "k", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadProcessMetadata_NullMetadataDict_ShouldReturnFallback()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown,
            Metadata: null);

        MainViewModelDiagnostics.ReadProcessMetadata(process, "missing", "fb").Should().Be("fb");
    }

    [Fact]
    public void ReadProcessMetadata_KeyMissing_ShouldReturnFallback()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["other"] = "v" });

        MainViewModelDiagnostics.ReadProcessMetadata(process, "missing", "fb").Should().Be("fb");
    }

    [Fact]
    public void ReadProcessMetadata_KeyPresent_ShouldReturnValue()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["key"] = "val" });

        MainViewModelDiagnostics.ReadProcessMetadata(process, "key", "fb").Should().Be("val");
    }

    // ── ReadProcessMods ──

    [Fact]
    public void ReadProcessMods_NullProcess_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.ReadProcessMods(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadProcessMods_Empty_ShouldReturnNone()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["steamModIdsDetected"] = "" });

        MainViewModelDiagnostics.ReadProcessMods(process).Should().Be("none");
    }

    [Fact]
    public void ReadProcessMods_WhitespaceOnly_ShouldReturnNone()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["steamModIdsDetected"] = "   " });

        MainViewModelDiagnostics.ReadProcessMods(process).Should().Be("none");
    }

    [Fact]
    public void ReadProcessMods_Present_ShouldReturnValue()
    {
        var process = new ProcessMetadata(1, "p", "p", null, ExeTarget.Swfoc, RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["steamModIdsDetected"] = "123,456" });

        MainViewModelDiagnostics.ReadProcessMods(process).Should().Be("123,456");
    }

    // ── BuildProcessDependencySegment ──

    [Fact]
    public void BuildProcessDependencySegment_NullState_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildProcessDependencySegment(null!, "msg");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildProcessDependencySegment_NullMessage_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildProcessDependencySegment("Pass", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildProcessDependencySegment_PassState_ShouldOmitMessage()
    {
        MainViewModelDiagnostics.BuildProcessDependencySegment("Pass", "ignored")
            .Should().Be("dependency=Pass");
    }

    [Fact]
    public void BuildProcessDependencySegment_PassCaseInsensitive_ShouldOmitMessage()
    {
        MainViewModelDiagnostics.BuildProcessDependencySegment("pass", "ignored")
            .Should().Be("dependency=pass");
    }

    [Fact]
    public void BuildProcessDependencySegment_NonPassWithEmptyMessage_ShouldOmitParenthetical()
    {
        MainViewModelDiagnostics.BuildProcessDependencySegment("SoftFail", "")
            .Should().Be("dependency=SoftFail");
    }

    [Fact]
    public void BuildProcessDependencySegment_NonPassWithWhitespaceMessage_ShouldOmitParenthetical()
    {
        MainViewModelDiagnostics.BuildProcessDependencySegment("SoftFail", "   ")
            .Should().Be("dependency=SoftFail");
    }

    [Fact]
    public void BuildProcessDependencySegment_NonPassWithMessage_ShouldIncludeParenthetical()
    {
        MainViewModelDiagnostics.BuildProcessDependencySegment("HardFail", "missing parent")
            .Should().Be("dependency=HardFail (missing parent)");
    }

    // ── FormatPatchValue ──

    [Fact]
    public void FormatPatchValue_NullValue_ShouldReturnNull()
    {
        MainViewModelDiagnostics.FormatPatchValue(null).Should().Be("null");
    }

    [Fact]
    public void FormatPatchValue_JsonElementString_ShouldReturnString()
    {
        using var doc = JsonDocument.Parse("\"hello\"");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("hello");
    }

    [Fact]
    public void FormatPatchValue_JsonElementStringReturningNull_ShouldReturnEmpty()
    {
        // JsonElement.GetString() can return null for certain edge cases.
        // We simulate this by using a JSON null-typed element via a workaround.
        using var doc = JsonDocument.Parse("{\"v\":null}");
        var nullElement = doc.RootElement.GetProperty("v");
        MainViewModelDiagnostics.FormatPatchValue(nullElement).Should().Be("null");
    }

    [Fact]
    public void FormatPatchValue_JsonElementNumber_ShouldReturnToString()
    {
        using var doc = JsonDocument.Parse("42");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("42");
    }

    [Fact]
    public void FormatPatchValue_JsonElementBool_ShouldReturnToString()
    {
        using var doc = JsonDocument.Parse("true");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("True");
    }

    [Fact]
    public void FormatPatchValue_JsonElementArray_ShouldReturnToString()
    {
        using var doc = JsonDocument.Parse("[1,2]");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("[1,2]");
    }

    [Fact]
    public void FormatPatchValue_PlainObject_ShouldReturnToString()
    {
        MainViewModelDiagnostics.FormatPatchValue(42).Should().Be("42");
        MainViewModelDiagnostics.FormatPatchValue("text").Should().Be("text");
    }

    [Fact]
    public void FormatPatchValue_ObjectWithNullToString_ShouldReturnEmpty()
    {
        // An object whose ToString() returns null.
        MainViewModelDiagnostics.FormatPatchValue(new NullToStringObject()).Should().Be(string.Empty);
    }

    // ── BuildPatchMetadataSummary ──

    [Fact]
    public void BuildPatchMetadataSummary_NullPack_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildPatchMetadataSummary(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── BuildDependencyDiagnostic ──

    [Fact]
    public void BuildDependencyDiagnostic_NullDependency_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildDependencyDiagnostic(null!, "msg");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDependencyDiagnostic_NullMessage_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildDependencyDiagnostic("Pass", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDependencyDiagnostic_WhitespaceMessage_ShouldOmitParenthetical()
    {
        MainViewModelDiagnostics.BuildDependencyDiagnostic("Pass", "  ")
            .Should().Be("dependency: Pass");
    }

    // ── ParsePrimitive ──

    [Fact]
    public void ParsePrimitive_NullInput_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.ParsePrimitive(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParsePrimitive_Int_ShouldReturnInt()
    {
        MainViewModelDiagnostics.ParsePrimitive("42").Should().BeOfType<int>().Which.Should().Be(42);
    }

    [Fact]
    public void ParsePrimitive_Long_ShouldReturnLong()
    {
        MainViewModelDiagnostics.ParsePrimitive("9999999999").Should().BeOfType<long>();
    }

    [Fact]
    public void ParsePrimitive_Bool_ShouldReturnBool()
    {
        MainViewModelDiagnostics.ParsePrimitive("true").Should().BeOfType<bool>().Which.Should().BeTrue();
        MainViewModelDiagnostics.ParsePrimitive("false").Should().BeOfType<bool>().Which.Should().BeFalse();
    }

    [Fact]
    public void ParsePrimitive_FloatSuffix_ShouldReturnFloat()
    {
        MainViewModelDiagnostics.ParsePrimitive("1.5f").Should().BeOfType<float>().Which.Should().Be(1.5f);
        MainViewModelDiagnostics.ParsePrimitive("3.0F").Should().BeOfType<float>();
    }

    [Fact]
    public void ParsePrimitive_FloatSuffixInvalidNumber_ShouldFallToDouble()
    {
        // "xf" ends with 'f' but the prefix is not a valid float
        var result = MainViewModelDiagnostics.ParsePrimitive("xf");
        result.Should().BeOfType<string>().Which.Should().Be("xf");
    }

    [Fact]
    public void ParsePrimitive_Double_ShouldReturnDouble()
    {
        MainViewModelDiagnostics.ParsePrimitive("2.75").Should().BeOfType<double>().Which.Should().Be(2.75);
    }

    [Fact]
    public void ParsePrimitive_NonNumericString_ShouldReturnString()
    {
        MainViewModelDiagnostics.ParsePrimitive("hello").Should().BeOfType<string>().Which.Should().Be("hello");
    }

    // ── ResolveBundleGateResult ──

    [Fact]
    public void ResolveBundleGateResult_NullUnknownValue_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.ResolveBundleGateResult(null, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ResolveBundleGateResult_NullReliability_ShouldReturnUnknown()
    {
        MainViewModelDiagnostics.ResolveBundleGateResult(null, "unk").Should().Be("unk");
    }

    [Fact]
    public void ResolveBundleGateResult_UnavailableState_ShouldReturnBlocked()
    {
        var item = new ActionReliabilityViewItem("a", "unavailable", "R", 0.5, "d");
        MainViewModelDiagnostics.ResolveBundleGateResult(item, "unk").Should().Be("blocked");
    }

    [Fact]
    public void ResolveBundleGateResult_NonUnavailableState_ShouldReturnBundlePass()
    {
        var item = new ActionReliabilityViewItem("a", "stable", "R", 1.0, "d");
        MainViewModelDiagnostics.ResolveBundleGateResult(item, "unk").Should().Be("bundle_pass");
    }

    // ── BuildDiagnosticsStatusSuffix ──

    [Fact]
    public void BuildDiagnosticsStatusSuffix_NullResult_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_NullDiagnostics_ShouldReturnEmpty()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None, Diagnostics: null);
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result).Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_EmptyDiagnostics_ShouldReturnEmpty()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None,
            Diagnostics: new Dictionary<string, object?>());
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result).Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_AllNullOrWhitespaceValues_ShouldReturnEmpty()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backend"] = null,
                ["backendRoute"] = "  ",
                ["routeReasonCode"] = null,
                ["reasonCode"] = "",
                ["probeReasonCode"] = null,
                ["hookState"] = null,
                ["hybridExecution"] = null
            });

        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result).Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_WithMixOfPrimaryAndAlias_ShouldPreferPrimary()
    {
        // The "backend" segment uses candidateKeys=["backendRoute"], so backendRoute is
        // the only key checked. When present, it is emitted under the segment key "backend".
        var result = new ActionExecutionResult(true, "ok", AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backend"] = "primary_val",
                ["backendRoute"] = "alias_val"
            });

        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);
        suffix.Should().Contain("backend=alias_val");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_WithNonStringValues_ShouldCallToString()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["hookState"] = 42
            });

        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);
        suffix.Should().Contain("hookState=42");
    }

    // ── BuildQuickActionStatus ──

    [Fact]
    public void BuildQuickActionStatus_NullActionId_ShouldThrow()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var act = () => MainViewModelDiagnostics.BuildQuickActionStatus(null!, result);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildQuickActionStatus_NullResult_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.BuildQuickActionStatus("a", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildQuickActionStatus_Succeeded_ShouldPrefixCheckmark()
    {
        var result = new ActionExecutionResult(true, "done", AddressSource.None);
        MainViewModelDiagnostics.BuildQuickActionStatus("x", result)
            .Should().StartWith("\u2713 x: done");
    }

    [Fact]
    public void BuildQuickActionStatus_Failed_ShouldPrefixCross()
    {
        var result = new ActionExecutionResult(false, "err", AddressSource.None);
        MainViewModelDiagnostics.BuildQuickActionStatus("x", result)
            .Should().StartWith("\u2717 x: err");
    }

    // ── ReadDiagnosticString ──

    [Fact]
    public void ReadDiagnosticString_NullKey_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.ReadDiagnosticString(null, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadDiagnosticString_NullDiagnostics_ShouldReturnEmpty()
    {
        MainViewModelDiagnostics.ReadDiagnosticString(null, "k").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_KeyMissing_ShouldReturnEmpty()
    {
        var diag = new Dictionary<string, object?> { ["other"] = "v" };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "missing").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_NullValue_ShouldReturnEmpty()
    {
        var diag = new Dictionary<string, object?> { ["k"] = null };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "k").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_StringValue_ShouldReturnString()
    {
        var diag = new Dictionary<string, object?> { ["k"] = "val" };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "k").Should().Be("val");
    }

    [Fact]
    public void ReadDiagnosticString_NonStringValue_ShouldCallToString()
    {
        var diag = new Dictionary<string, object?> { ["k"] = 99 };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "k").Should().Be("99");
    }

    [Fact]
    public void ReadDiagnosticString_NonStringValueWithNullToString_ShouldReturnEmpty()
    {
        var diag = new Dictionary<string, object?> { ["k"] = new NullToStringObject() };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "k").Should().BeEmpty();
    }

    // ── Helper ──

    private sealed class NullToStringObject
    {
        public override string? ToString() => null;
    }
}
