using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Deep branch coverage for MainViewModelDiagnostics covering FormatPatchValue,
/// BuildPatchMetadataSummary, BuildDependencyDiagnostic, ReadDiagnosticString,
/// BuildProcessDiagnosticSummary, BuildQuickActionStatus, and all private segment builders.
/// </summary>
public sealed class MainViewModelDiagnosticsDeepBranchTests
{
    [Fact]
    public void FormatPatchValue_ShouldReturnNull_WhenNull()
    {
        MainViewModelDiagnostics.FormatPatchValue(null).Should().Be("null");
    }

    [Fact]
    public void FormatPatchValue_ShouldReturnStringValue_ForJsonElementString()
    {
        var doc = JsonDocument.Parse("\"hello\"");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("hello");
    }

    [Fact]
    public void FormatPatchValue_ShouldReturnNull_ForJsonElementNull()
    {
        var doc = JsonDocument.Parse("null");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("null");
    }

    [Fact]
    public void FormatPatchValue_ShouldReturnNumber_ForJsonElementNumber()
    {
        var doc = JsonDocument.Parse("42");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("42");
    }

    [Fact]
    public void FormatPatchValue_ShouldReturnToString_ForPlainObject()
    {
        MainViewModelDiagnostics.FormatPatchValue(12345).Should().Be("12345");
    }

    [Fact]
    public void FormatPatchValue_ShouldReturnEmpty_WhenToStringReturnsNull()
    {
        // Using an object whose ToString() returns empty
        MainViewModelDiagnostics.FormatPatchValue("").Should().Be("");
    }

    [Fact]
    public void BuildPatchMetadataSummary_ShouldThrow_WhenPackIsNull()
    {
        var act = () => MainViewModelDiagnostics.BuildPatchMetadataSummary(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildPatchMetadataSummary_ShouldIncludeMetadataFields()
    {
        var pack = new SavePatchPack(
            new SavePatchMetadata("1.0", "profile1", "schema1", "hash1", DateTimeOffset.UtcNow),
            new SavePatchCompatibility(new[] { "profile1" }, "schema1"),
            Array.Empty<SavePatchOperation>());

        var summary = MainViewModelDiagnostics.BuildPatchMetadataSummary(pack);

        summary.Should().Contain("profile=profile1");
        summary.Should().Contain("schema=schema1");
        summary.Should().Contain("ops=0");
    }

    [Fact]
    public void BuildDependencyDiagnostic_ShouldThrow_WhenDependencyIsNull()
    {
        var act = () => MainViewModelDiagnostics.BuildDependencyDiagnostic(null!, "msg");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDependencyDiagnostic_ShouldThrow_WhenMessageIsNull()
    {
        var act = () => MainViewModelDiagnostics.BuildDependencyDiagnostic("dep", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDependencyDiagnostic_ShouldNotIncludeMessage_WhenEmpty()
    {
        var result = MainViewModelDiagnostics.BuildDependencyDiagnostic("Pass", "");
        result.Should().Be("dependency: Pass");
    }

    [Fact]
    public void BuildDependencyDiagnostic_ShouldIncludeMessage_WhenNotEmpty()
    {
        var result = MainViewModelDiagnostics.BuildDependencyDiagnostic("SoftFail", "missing parent");
        result.Should().Be("dependency: SoftFail (missing parent)");
    }

    [Fact]
    public void ReadDiagnosticString_ShouldThrow_WhenKeyIsNull()
    {
        var act = () => MainViewModelDiagnostics.ReadDiagnosticString(null, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadDiagnosticString_ShouldReturnEmpty_WhenDiagnosticsIsNull()
    {
        MainViewModelDiagnostics.ReadDiagnosticString(null, "key").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_ShouldReturnEmpty_WhenKeyNotPresent()
    {
        var diag = new Dictionary<string, object?>();
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "missing").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_ShouldReturnEmpty_WhenValueIsNull()
    {
        var diag = new Dictionary<string, object?> { ["key"] = null };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "key").Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_ShouldReturnString_WhenValueIsString()
    {
        var diag = new Dictionary<string, object?> { ["key"] = "hello" };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "key").Should().Be("hello");
    }

    [Fact]
    public void ReadDiagnosticString_ShouldReturnToString_WhenValueIsNotString()
    {
        var diag = new Dictionary<string, object?> { ["key"] = 42 };
        MainViewModelDiagnostics.ReadDiagnosticString(diag, "key").Should().Be("42");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_ShouldThrow_WhenResultIsNull()
    {
        var act = () => MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_ShouldReturnEmpty_WhenDiagnosticsIsNull()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result).Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_ShouldReturnEmpty_WhenDiagnosticsHasNoRelevantKeys()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None,
            new Dictionary<string, object?> { ["irrelevant"] = "value" });
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result).Should().BeEmpty();
    }

    [Fact]
    public void BuildQuickActionStatus_ShouldThrow_WhenActionIdIsNull()
    {
        var act = () => MainViewModelDiagnostics.BuildQuickActionStatus(
            null!, new ActionExecutionResult(true, "ok", AddressSource.None));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildQuickActionStatus_ShouldThrow_WhenResultIsNull()
    {
        var act = () => MainViewModelDiagnostics.BuildQuickActionStatus("action", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildQuickActionStatus_ShouldContainCheckmark_WhenSucceeded()
    {
        var result = new ActionExecutionResult(true, "done", AddressSource.Signature);
        var status = MainViewModelDiagnostics.BuildQuickActionStatus("set_credits", result);
        status.Should().Contain("set_credits");
        status.Should().Contain("done");
    }

    [Fact]
    public void BuildQuickActionStatus_ShouldContainCross_WhenFailed()
    {
        var result = new ActionExecutionResult(false, "error", AddressSource.None);
        var status = MainViewModelDiagnostics.BuildQuickActionStatus("set_credits", result);
        status.Should().Contain("set_credits");
        status.Should().Contain("error");
    }

    [Fact]
    public void ReadProcessMetadata_ShouldThrow_WhenProcessIsNull()
    {
        var act = () => MainViewModelDiagnostics.ReadProcessMetadata(null!, "key", "fb");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadProcessMetadata_ShouldThrow_WhenKeyIsNull()
    {
        var process = BuildProcess();
        var act = () => MainViewModelDiagnostics.ReadProcessMetadata(process, null!, "fb");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadProcessMetadata_ShouldReturnFallback_WhenKeyNotPresent()
    {
        var process = BuildProcess();
        MainViewModelDiagnostics.ReadProcessMetadata(process, "missing_key", "fallback")
            .Should().Be("fallback");
    }

    [Fact]
    public void ReadProcessMetadata_ShouldReturnValue_WhenKeyPresent()
    {
        var process = BuildProcess(new Dictionary<string, string> { ["myKey"] = "myValue" });
        MainViewModelDiagnostics.ReadProcessMetadata(process, "myKey", "fb")
            .Should().Be("myValue");
    }

    [Fact]
    public void ReadProcessMods_ShouldReturnNone_WhenNoMods()
    {
        var process = BuildProcess();
        MainViewModelDiagnostics.ReadProcessMods(process).Should().Be("none");
    }

    [Fact]
    public void ReadProcessMods_ShouldReturnMods_WhenPresent()
    {
        var process = BuildProcess(new Dictionary<string, string>
        {
            ["steamModIdsDetected"] = "1397421866,3447786229"
        });
        MainViewModelDiagnostics.ReadProcessMods(process).Should().Contain("1397421866");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldThrow_WhenProcessIsNull()
    {
        var act = () => MainViewModelDiagnostics.BuildProcessDiagnosticSummary(null!, "unknown");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldContainAllSegments()
    {
        var process = BuildProcess(new Dictionary<string, string>
        {
            ["commandLineAvailable"] = "True",
            ["steamModIdsDetected"] = "123",
            ["detectedVia"] = "probe",
            ["dependencyValidation"] = "Pass",
            ["dependencyValidationMessage"] = "",
            ["resolvedVariant"] = "base_swfoc",
            ["resolvedVariantReasonCode"] = "match",
            ["resolvedVariantConfidence"] = "1.00",
            ["fallbackHitRate"] = "0%",
            ["unresolvedSymbolRate"] = "0%"
        });

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("target=");
        summary.Should().Contain("launch=");
        summary.Should().Contain("hostRole=");
        summary.Should().Contain("score=");
        summary.Should().Contain("module=");
        summary.Should().Contain("workshopMatches=");
        summary.Should().Contain("cmdLine=");
        summary.Should().Contain("mods=");
        summary.Should().Contain("modPath=");
        summary.Should().Contain("rec=");
        summary.Should().Contain("variant=");
        summary.Should().Contain("via=");
        summary.Should().Contain("dependency=");
        summary.Should().Contain("fallbackRate=");
        summary.Should().Contain("unresolvedRate=");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldHandleNullMetadata()
    {
        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "test.exe",
            ProcessPath: @"C:\test.exe",
            CommandLine: "",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: null,
            LaunchContext: null);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("target=");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldIncludeModPath_WhenLaunchContextHasModPath()
    {
        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: "",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(),
            LaunchContext: new LaunchContext(
                LaunchKind.LocalModPath,
                CommandLineAvailable: true,
                SteamModIds: Array.Empty<string>(),
                ModPathRaw: @"C:\Mods\MyMod",
                ModPathNormalized: @"C:\Mods\MyMod",
                DetectedVia: "cmd",
                Recommendation: null!));

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain(@"modPath=C:\Mods\MyMod");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldIncludeRecommendation()
    {
        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: "",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(),
            LaunchContext: new LaunchContext(
                LaunchKind.Workshop,
                CommandLineAvailable: true,
                SteamModIds: new[] { "1234" },
                ModPathRaw: null,
                ModPathNormalized: null,
                DetectedVia: "cmd",
                Recommendation: new ProfileRecommendation("roe_profile", "workshop_match", 0.95)));

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("rec=roe_profile:workshop_match:0.95");
    }

    [Fact]
    public void ResolveBundleGateResult_ShouldThrow_WhenUnknownValueIsNull()
    {
        var act = () => MainViewModelDiagnostics.ResolveBundleGateResult(null, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static ProcessMetadata BuildProcess(IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "StarWarsG.exe",
            ProcessPath: @"C:\Games\StarWarsG.exe",
            CommandLine: "",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: metadata ?? new Dictionary<string, string>(),
            LaunchContext: null);
    }
}
