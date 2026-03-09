using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelDiagnosticsCoverageTests
{
    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldRenderRichProcessMetadata()
    {
        var process = new ProcessMetadata(
            ProcessId: 42,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: "swfoc.exe STEAMMOD=1397421866",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyValidation"] = "SoftFail",
                ["dependencyValidationMessage"] = "parent missing",
                ["commandLineAvailable"] = "True",
                ["steamModIdsDetected"] = "1397421866,3447786229",
                ["detectedVia"] = "snapshot",
                ["fallbackHitRate"] = "0.15",
                ["unresolvedSymbolRate"] = "0.35",
                ["resolvedVariant"] = "roe_chain",
                ["resolvedVariantReasonCode"] = "workshop_chain",
                ["resolvedVariantConfidence"] = "0.91"
            },
            LaunchContext: new LaunchContext(
                LaunchKind.Workshop,
                CommandLineAvailable: true,
                SteamModIds: new[] { "1397421866", "3447786229" },
                ModPathRaw: null,
                ModPathNormalized: @"Mods\ROE",
                DetectedVia: "command_line",
                Recommendation: new ProfileRecommendation("roe_3447786229_swfoc", "workshop_match", 0.82)),
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 123456,
            WorkshopMatchCount: 2,
            SelectionScore: 0.875);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("target=Swfoc");
        summary.Should().Contain("launch=Workshop");
        summary.Should().Contain("hostRole=gamehost");
        summary.Should().Contain("score=0.88");
        summary.Should().Contain("module=123456");
        summary.Should().Contain("workshopMatches=2");
        summary.Should().Contain("cmdLine=True");
        summary.Should().Contain("mods=1397421866,3447786229");
        summary.Should().Contain(@"modPath=Mods\ROE");
        summary.Should().Contain("rec=roe_3447786229_swfoc:workshop_match:0.82");
        summary.Should().Contain("via=snapshot");
        summary.Should().Contain("dependency=SoftFail (parent missing)");
        summary.Should().Contain("variant=roe_chain:workshop_chain:0.91");
        summary.Should().Contain("fallbackRate=0.15");
        summary.Should().Contain("unresolvedRate=0.35");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ShouldRenderFallbackSegments_WhenMetadataMissing()
    {
        var process = new ProcessMetadata(
            ProcessId: 7,
            ProcessName: "sweaw.exe",
            ProcessPath: @"C:\Games\sweaw.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Sweaw,
            Mode: RuntimeMode.Menu,
            Metadata: null,
            LaunchContext: null,
            HostRole: ProcessHostRole.Launcher,
            MainModuleSize: 0,
            WorkshopMatchCount: 0,
            SelectionScore: 0d);

        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");

        summary.Should().Contain("launch=Unknown");
        summary.Should().Contain("hostRole=launcher");
        summary.Should().Contain("score=0.00");
        summary.Should().Contain("module=n/a");
        summary.Should().Contain("mods=none");
        summary.Should().Contain("modPath=none");
        summary.Should().Contain("rec=none:unknown:0.00");
        summary.Should().Contain("via=unknown");
        summary.Should().Contain("dependency=Pass");
        summary.Should().Contain("variant=n/a:n/a:0.00");
        summary.Should().Contain("fallbackRate=n/a");
        summary.Should().Contain("unresolvedRate=n/a");
    }

    [Fact]
    public void ReadProcessMetadata_And_ReadProcessMods_ShouldHonorFallbacks()
    {
        var emptyProcess = new ProcessMetadata(
            1,
            "game.exe",
            "/tmp/game.exe",
            null,
            ExeTarget.Unknown,
            RuntimeMode.Unknown,
            Metadata: null);

        var whitespaceModsProcess = emptyProcess with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["steamModIdsDetected"] = "   "
            }
        };

        MainViewModelDiagnostics.ReadProcessMetadata(emptyProcess, "missing", "fallback").Should().Be("fallback");
        MainViewModelDiagnostics.ReadProcessMods(emptyProcess).Should().Be("none");
        MainViewModelDiagnostics.ReadProcessMods(whitespaceModsProcess).Should().Be("none");
    }

    [Theory]
    [InlineData("Pass", "", "dependency=Pass")]
    [InlineData("SoftFail", "parent missing", "dependency=SoftFail (parent missing)")]
    public void BuildProcessDependencySegment_ShouldFormatExpectedValue(string state, string message, string expected)
    {
        MainViewModelDiagnostics.BuildProcessDependencySegment(state, message).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("\"alpha\"", "alpha")]
    [InlineData("null", "null")]
    [InlineData("123", "123")]
    public void FormatPatchValue_ShouldHandleJsonElementBranches(string jsonLiteral, string expected)
    {
        object? value = jsonLiteral is null
            ? null
            : JsonDocument.Parse(jsonLiteral).RootElement.Clone();

        MainViewModelDiagnostics.FormatPatchValue(value).Should().Be(expected);
    }

    [Fact]
    public void FormatPatchValue_ShouldHandlePlainObjects()
    {
        MainViewModelDiagnostics.FormatPatchValue(123).Should().Be("123");
        MainViewModelDiagnostics.FormatPatchValue(true).Should().Be("True");
    }

    [Fact]
    public void PatchSummaryAndDependencyDiagnostic_ShouldIncludeMetadata()
    {
        var pack = new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "schema_v1", "hash123", DateTimeOffset.Parse("2026-03-09T10:00:00Z")),
            new SavePatchCompatibility(new[] { "base_swfoc" }, "schema_v1"),
            new[]
            {
                new SavePatchOperation(SavePatchOperationKind.SetValue, "/credits", "credits", "Int32", 10, 20, 4)
            });

        MainViewModelDiagnostics.BuildPatchMetadataSummary(pack).Should().Be("Patch 1.0 | profile=base_swfoc | schema=schema_v1 | ops=1");
        MainViewModelDiagnostics.BuildDependencyDiagnostic("Pass", string.Empty).Should().Be("dependency: Pass");
        MainViewModelDiagnostics.BuildDependencyDiagnostic("SoftFail", "missing parent").Should().Be("dependency: SoftFail (missing parent)");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_And_QuickActionStatus_ShouldRenderKnownDiagnosticKeys()
    {
        var result = new ActionExecutionResult(
            Succeeded: true,
            Message: "ok",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backendRoute"] = "helper",
                ["reasonCode"] = "ROUTE_OK",
                ["capabilityProbeReasonCode"] = "PROBE_OK",
                ["hookState"] = "active",
                ["helperVerifyState"] = "Applied",
                ["operationKind"] = "spawn_tactical_entity",
                ["hybridExecution"] = true
            });

        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);
        var status = MainViewModelDiagnostics.BuildQuickActionStatus("spawn_tactical_entity", result);

        suffix.Should().Be(" [backend=helper, routeReasonCode=ROUTE_OK, probeReasonCode=PROBE_OK, hookState=active, helperVerify=Applied, operationKind=spawn_tactical_entity, hybridExecution=True]");
        status.Should().Be("✓ spawn_tactical_entity: ok [backend=helper, routeReasonCode=ROUTE_OK, probeReasonCode=PROBE_OK, hookState=active, helperVerify=Applied, operationKind=spawn_tactical_entity, hybridExecution=True]");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_ShouldReturnEmpty_WhenNoDiagnosticsExist()
    {
        MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(
            new ActionExecutionResult(false, "boom", AddressSource.None, Diagnostics: null)).Should().BeEmpty();
    }

    [Fact]
    public void ReadDiagnosticString_ShouldReturnStringifiedValues_AndEmptyForMissingKeys()
    {
        var diagnostics = new Dictionary<string, object?>
        {
            ["count"] = 3,
            ["name"] = "token",
            ["nullValue"] = null
        };

        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "count").Should().Be("3");
        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "name").Should().Be("token");
        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "nullValue").Should().BeEmpty();
        MainViewModelDiagnostics.ReadDiagnosticString(diagnostics, "missing").Should().BeEmpty();
        MainViewModelDiagnostics.ReadDiagnosticString(null, "missing").Should().BeEmpty();
    }

    [Fact]
    public void ResolveBundleGateResult_ShouldReturnUnknownBlockedAndPassVariants()
    {
        MainViewModelDiagnostics.ResolveBundleGateResult(null, "unknown").Should().Be("unknown");
        MainViewModelDiagnostics.ResolveBundleGateResult(
                new ActionReliabilityViewItem("set_credits", "unavailable", "HOOK_MISSING", 0.2, "hook missing"),
                "unknown")
            .Should().Be("blocked");
        MainViewModelDiagnostics.ResolveBundleGateResult(
                new ActionReliabilityViewItem("set_credits", "stable", "OK", 1.0, "ready"),
                "unknown")
            .Should().Be("bundle_pass");
    }
}
