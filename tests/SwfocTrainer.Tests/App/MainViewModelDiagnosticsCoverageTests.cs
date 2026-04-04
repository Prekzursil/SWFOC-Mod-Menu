using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelDiagnosticsCoverageTests
{
    [Fact]
    public void BuildDiagnosticsStatusSuffix_ShouldReturnEmpty_WhenDiagnosticsMissingOrUnmapped()
    {
        var withoutDiagnostics = new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.None,
            Diagnostics: null);
        var withUnmappedDiagnostics = new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backend"] = "  ",
                ["unrelated"] = "value"
            });

        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(withoutDiagnostics).Should().BeEmpty();
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(withUnmappedDiagnostics).Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_ShouldPreferAliasKeysAndSkipWhitespaceValues()
    {
        var result = new ActionExecutionResult(
            Succeeded: false,
            Message: "failed",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backend"] = " ",
                ["backendRoute"] = "runtime",
                ["routeReasonCode"] = "",
                ["reasonCode"] = "CAPABILITY_REQUIRED_MISSING",
                ["probeReasonCode"] = "CAPABILITY_PROBE_PASS",
                ["hookState"] = 2,
                ["hybridExecution"] = false
            });

        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

        suffix.Should().Contain("backend=runtime");
        suffix.Should().Contain("routeReasonCode=CAPABILITY_REQUIRED_MISSING");
        suffix.Should().Contain("capabilityProbeReasonCode=CAPABILITY_PROBE_PASS");
        suffix.Should().Contain("hookState=2");
        suffix.Should().Contain("hybridExecution=False");
    }

    [Fact]
    public void BuildQuickActionStatus_ShouldPrefixOutcomeAndAppendDiagnostics()
    {
        var success = new ActionExecutionResult(
            Succeeded: true,
            Message: "applied",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backend"] = "sdk"
            });
        var failure = new ActionExecutionResult(
            Succeeded: false,
            Message: "blocked",
            AddressSource: AddressSource.None);

        var successStatus = MainViewModelDiagnostics.BuildQuickActionStatus("set_credits", success);
        var failureStatus = MainViewModelDiagnostics.BuildQuickActionStatus("set_credits", failure);

        successStatus.Should().Be("✓ set_credits: applied [backend=sdk]");
        failureStatus.Should().Be("✗ set_credits: blocked");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldIncludeRecommendationAndDependencyContext()
    {
        var process = new ProcessMetadata(
            ProcessId: 101,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: "STEAMMOD=3447786229",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["commandLineAvailable"] = "True",
                ["steamModIdsDetected"] = "3447786229,1397421866",
                ["detectedVia"] = "cmd",
                ["resolvedVariant"] = "roe",
                ["resolvedVariantReasonCode"] = "workshop_match",
                ["resolvedVariantConfidence"] = "0.95",
                ["dependencyValidation"] = "SoftFail",
                ["dependencyValidationMessage"] = "missing parent",
                ["fallbackHitRate"] = "22%",
                ["unresolvedSymbolRate"] = "5%"
            },
            LaunchContext: new LaunchContext(
                LaunchKind.Workshop,
                CommandLineAvailable: true,
                SteamModIds: new[] { "3447786229" },
                ModPathRaw: null,
                ModPathNormalized: "Mods\\ROE",
                DetectedVia: "cmd",
                Recommendation: new ProfileRecommendation("roe_3447786229_swfoc", "workshop_match", 0.91)),
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 4096,
            WorkshopMatchCount: 2,
            SelectionScore: 0.875);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("target=Swfoc");
        summary.Should().Contain("launch=Workshop");
        summary.Should().Contain("hostRole=gamehost");
        summary.Should().Contain("score=0.88");
        summary.Should().Contain("module=4096");
        summary.Should().Contain("workshopMatches=2");
        summary.Should().Contain("mods=3447786229,1397421866");
        summary.Should().Contain("modPath=Mods\\ROE");
        summary.Should().Contain("rec=roe_3447786229_swfoc:workshop_match:0.91");
        summary.Should().Contain("variant=roe:workshop_match:0.95");
        summary.Should().Contain("dependency=SoftFail (missing parent)");
        summary.Should().Contain("fallbackRate=22%");
        summary.Should().Contain("unresolvedRate=5%");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldUseFallbackValues_WhenMetadataIsMissing()
    {
        var process = new ProcessMetadata(
            ProcessId: 202,
            ProcessName: "StarWarsG.exe",
            ProcessPath: @"C:\Games\StarWarsG.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("launch=Unknown");
        summary.Should().Contain("hostRole=unknown");
        summary.Should().Contain("module=n/a");
        summary.Should().Contain("mods=none");
        summary.Should().Contain("modPath=none");
        summary.Should().Contain("rec=none:unknown:0.00");
        summary.Should().Contain("variant=n/a:n/a:0.00");
        summary.Should().Contain("via=unknown");
        summary.Should().Contain("dependency=Pass");
        summary.Should().Contain("fallbackRate=n/a");
        summary.Should().Contain("unresolvedRate=n/a");
    }

    [Fact]
    public void FormatPatchValue_ShouldHandleNullAndJsonElementKinds()
    {
        using var doc = JsonDocument.Parse("{\"text\":\"credits\",\"count\":42,\"empty\":null}");

        MainViewModelDiagnostics.FormatPatchValue(null).Should().Be("null");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement.GetProperty("text")).Should().Be("credits");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement.GetProperty("count")).Should().Be("42");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement.GetProperty("empty")).Should().Be("null");
        MainViewModelDiagnostics.FormatPatchValue(13).Should().Be("13");
    }

    [Fact]
    public void BuildPatchMetadataSummary_ShouldIncludeSchemaProfileAndOperationCount()
    {
        var pack = new SavePatchPack(
            Metadata: new SavePatchMetadata(
                SchemaVersion: "v1",
                ProfileId: "roe_3447786229_swfoc",
                SchemaId: "save_schema",
                SourceHash: "ABC123",
                CreatedAtUtc: DateTimeOffset.Parse("2026-03-09T10:00:00Z")),
            Compatibility: new SavePatchCompatibility(
                AllowedProfileIds: new[] { "roe_3447786229_swfoc" },
                RequiredSchemaId: "save_schema"),
            Operations: new[]
            {
                new SavePatchOperation(SavePatchOperationKind.SetValue, "credits", "credits", "Int32", 1000, 1500, 0x12),
                new SavePatchOperation(SavePatchOperationKind.SetValue, "fog", "fog", "Bool", false, true, 0x28)
            });

        var summary = MainViewModelDiagnostics.BuildPatchMetadataSummary(pack);

        summary.Should().Be("Patch v1 | profile=roe_3447786229_swfoc | schema=save_schema | ops=2");
    }

    [Fact]
    public void BuildDependencyDiagnostic_ShouldIncludeMessageOnlyWhenPresent()
    {
        MainViewModelDiagnostics.BuildDependencyDiagnostic("Pass", string.Empty)
            .Should().Be("dependency: Pass");
        MainViewModelDiagnostics.BuildDependencyDiagnostic("SoftFail", "missing files")
            .Should().Be("dependency: SoftFail (missing files)");
    }

    [Fact]
    public void ReadDiagnosticString_ShouldHandleNullMissingAndNonStringValues()
    {
        var diagnostics = new Dictionary<string, object?>
        {
            ["route"] = "sdk",
            ["attempt"] = 3,
            ["empty"] = null
        };

        MainViewModelDiagnostics.ReadDiagnosticString(null, "route").Should().BeEmpty();
        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "missing").Should().BeEmpty();
        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "empty").Should().BeEmpty();
        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "route").Should().Be("sdk");
        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "attempt").Should().Be("3");
    }
}
