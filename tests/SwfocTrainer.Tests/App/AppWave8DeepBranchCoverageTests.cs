using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 8 deep branch coverage — targets every remaining untested branch
/// across the App package, pushing toward 100% coverage.
/// </summary>
public sealed class AppWave8DeepBranchCoverageTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelDiagnostics — remaining branches
    // ─────────────────────────────────────────────────────────────────────────

    #region FormatPatchValue edge cases

    [Fact]
    public void FormatPatchValue_JsonElementBoolean_ShouldReturnBoolString()
    {
        var doc = JsonDocument.Parse("true");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("True");
    }

    [Fact]
    public void FormatPatchValue_JsonElementArray_ShouldReturnArrayString()
    {
        var doc = JsonDocument.Parse("[1,2,3]");
        var result = MainViewModelDiagnostics.FormatPatchValue(doc.RootElement);
        result.Should().Contain("1");
    }

    [Fact]
    public void FormatPatchValue_JsonElementObject_ShouldReturnObjectString()
    {
        var doc = JsonDocument.Parse("{\"a\":1}");
        var result = MainViewModelDiagnostics.FormatPatchValue(doc.RootElement);
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FormatPatchValue_JsonElementUndefined_ShouldReturnString()
    {
        // JsonValueKind.False
        var doc = JsonDocument.Parse("false");
        MainViewModelDiagnostics.FormatPatchValue(doc.RootElement).Should().Be("False");
    }

    [Fact]
    public void FormatPatchValue_PlainString_ShouldReturnSameString()
    {
        MainViewModelDiagnostics.FormatPatchValue("hello world").Should().Be("hello world");
    }

    #endregion

    #region ParsePrimitive — all branches

    [Fact]
    public void ParsePrimitive_NullInput_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.ParsePrimitive(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParsePrimitive_Int_ShouldReturnInt()
    {
        MainViewModelDiagnostics.ParsePrimitive("42").Should().Be(42);
    }

    [Fact]
    public void ParsePrimitive_Long_ShouldReturnLong()
    {
        MainViewModelDiagnostics.ParsePrimitive("9999999999999").Should().BeOfType<long>();
    }

    [Fact]
    public void ParsePrimitive_Bool_True_ShouldReturnTrue()
    {
        MainViewModelDiagnostics.ParsePrimitive("true").Should().Be(true);
    }

    [Fact]
    public void ParsePrimitive_Bool_False_ShouldReturnFalse()
    {
        MainViewModelDiagnostics.ParsePrimitive("false").Should().Be(false);
    }

    [Fact]
    public void ParsePrimitive_Float_WithSuffix_ShouldReturnFloat()
    {
        var result = MainViewModelDiagnostics.ParsePrimitive("3.14f");
        result.Should().BeOfType<float>();
    }

    [Fact]
    public void ParsePrimitive_Float_WithUpperF_ShouldReturnFloat()
    {
        var result = MainViewModelDiagnostics.ParsePrimitive("2.5F");
        result.Should().BeOfType<float>();
    }

    [Fact]
    public void ParsePrimitive_Double_ShouldReturnDouble()
    {
        var result = MainViewModelDiagnostics.ParsePrimitive("3.14159");
        result.Should().BeOfType<double>();
    }

    [Fact]
    public void ParsePrimitive_NonParseable_ShouldReturnOriginalString()
    {
        MainViewModelDiagnostics.ParsePrimitive("hello").Should().Be("hello");
    }

    [Fact]
    public void ParsePrimitive_WhitespacePadded_Double_ShouldReturnDouble()
    {
        var result = MainViewModelDiagnostics.ParsePrimitive("  1.5  ");
        result.Should().BeOfType<double>();
    }

    [Fact]
    public void ParsePrimitive_InvalidFloat_Suffix_ShouldFallToDoubleOrString()
    {
        // "xyzf" -> not a float (parse fails), not a double -> returns string
        var result = MainViewModelDiagnostics.ParsePrimitive("xyzf");
        result.Should().Be("xyzf");
    }

    #endregion

    #region BuildDiagnosticsStatusSuffix — multiple segment keys

    [Fact]
    public void BuildDiagnosticsStatusSuffix_AllSegmentsPresent_ShouldJoinAll()
    {
        var diag = new Dictionary<string, object?>
        {
            ["backendRoute"] = "memory",
            ["routeReasonCode"] = "symbol_resolved",
            ["capabilityProbeReasonCode"] = "probe_ok",
            ["hookState"] = "installed",
            ["hybridExecution"] = "false"
        };
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature, diag);
        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

        suffix.Should().Contain("backend=memory");
        suffix.Should().Contain("routeReasonCode=symbol_resolved");
        suffix.Should().Contain("capabilityProbeReasonCode=probe_ok");
        suffix.Should().Contain("hookState=installed");
        suffix.Should().Contain("hybridExecution=false");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_OnlyBackend_ShouldContainBackend()
    {
        var diag = new Dictionary<string, object?> { ["backendRoute"] = "sdk" };
        var result = new ActionExecutionResult(true, "ok", AddressSource.None, diag);
        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

        suffix.Should().Contain("backend=sdk");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_NullValueInDiag_ShouldSkipSegment()
    {
        var diag = new Dictionary<string, object?> { ["backendRoute"] = null };
        var result = new ActionExecutionResult(true, "ok", AddressSource.None, diag);
        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

        suffix.Should().BeEmpty();
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_NonStringDiagValue_ShouldUseToString()
    {
        var diag = new Dictionary<string, object?> { ["backendRoute"] = 42 };
        var result = new ActionExecutionResult(true, "ok", AddressSource.None, diag);
        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

        suffix.Should().Contain("backend=42");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_WhitespaceDiagValue_ShouldSkipSegment()
    {
        var diag = new Dictionary<string, object?> { ["backendRoute"] = "   " };
        var result = new ActionExecutionResult(true, "ok", AddressSource.None, diag);
        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

        suffix.Should().BeEmpty();
    }

    #endregion

    #region BuildQuickActionStatus — with diagnostics suffix

    [Fact]
    public void BuildQuickActionStatus_Succeeded_WithDiagnostics_ShouldIncludeSuffix()
    {
        var diag = new Dictionary<string, object?> { ["backendRoute"] = "memory" };
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature, diag);
        var status = MainViewModelDiagnostics.BuildQuickActionStatus("freeze_timer", result);

        status.Should().StartWith("\u2713"); // checkmark
        status.Should().Contain("freeze_timer");
        status.Should().Contain("backend=memory");
    }

    [Fact]
    public void BuildQuickActionStatus_Failed_WithDiagnostics_ShouldIncludeSuffix()
    {
        var diag = new Dictionary<string, object?> { ["hookState"] = "failed" };
        var result = new ActionExecutionResult(false, "hook error", AddressSource.None, diag);
        var status = MainViewModelDiagnostics.BuildQuickActionStatus("set_credits", result);

        status.Should().StartWith("\u2717"); // cross
        status.Should().Contain("hookState=failed");
    }

    #endregion

    #region ResolveBundleGateResult — all branches

    [Fact]
    public void ResolveBundleGateResult_NullReliability_ShouldReturnUnknown()
    {
        MainViewModelDiagnostics.ResolveBundleGateResult(null, "unknown").Should().Be("unknown");
    }

    [Fact]
    public void ResolveBundleGateResult_Unavailable_ShouldReturnBlocked()
    {
        var item = new ActionReliabilityViewItem("a", "unavailable", "reason", 0, "detail");
        MainViewModelDiagnostics.ResolveBundleGateResult(item, "unknown").Should().Be("blocked");
    }

    [Fact]
    public void ResolveBundleGateResult_Available_ShouldReturnBundlePass()
    {
        var item = new ActionReliabilityViewItem("a", "available", "reason", 1.0, "detail");
        MainViewModelDiagnostics.ResolveBundleGateResult(item, "unknown").Should().Be("bundle_pass");
    }

    #endregion

    #region BuildProcessDependencySegment — all branches

    [Fact]
    public void BuildProcessDependencySegment_PassState_ShouldNotIncludeMessage()
    {
        var result = MainViewModelDiagnostics.BuildProcessDependencySegment("Pass", "some message");
        result.Should().Be("dependency=Pass");
    }

    [Fact]
    public void BuildProcessDependencySegment_FailState_WithEmptyMessage_ShouldNotIncludeParens()
    {
        var result = MainViewModelDiagnostics.BuildProcessDependencySegment("Fail", "");
        result.Should().Be("dependency=Fail");
    }

    [Fact]
    public void BuildProcessDependencySegment_FailState_WithMessage_ShouldIncludeParens()
    {
        var result = MainViewModelDiagnostics.BuildProcessDependencySegment("Fail", "missing dep");
        result.Should().Be("dependency=Fail (missing dep)");
    }

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

    #endregion

    #region ReadProcessMetadata — null metadata

    [Fact]
    public void ReadProcessMetadata_NullMetadata_ShouldReturnFallback()
    {
        var process = new ProcessMetadata(1, "test.exe", @"C:\test.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown, Metadata: null);
        MainViewModelDiagnostics.ReadProcessMetadata(process, "any", "default").Should().Be("default");
    }

    [Fact]
    public void ReadProcessMetadata_NullFallback_ShouldThrow()
    {
        var process = BuildProcess();
        var act = () => MainViewModelDiagnostics.ReadProcessMetadata(process, "key", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ReadProcessMods — with whitespace-only metadata

    [Fact]
    public void ReadProcessMods_WhitespaceMods_ShouldReturnNone()
    {
        var process = BuildProcess(new Dictionary<string, string> { ["steamModIdsDetected"] = "   " });
        MainViewModelDiagnostics.ReadProcessMods(process).Should().Be("none");
    }

    [Fact]
    public void ReadProcessMods_NullProcess_ShouldThrow()
    {
        var act = () => MainViewModelDiagnostics.ReadProcessMods(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region BuildProcessDiagnosticSummary — with MainModuleSize > 0

    [Fact]
    public void BuildProcessDiagnosticSummary_WithMainModuleSize_ShouldShowSize()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Galactic, new Dictionary<string, string>(), MainModuleSize: 1024);
        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("module=1024");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_ZeroMainModuleSize_ShouldShowNA()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Galactic, new Dictionary<string, string>(), MainModuleSize: 0);
        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("module=n/a");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithHostRole_ShouldShowRole()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Galactic, new Dictionary<string, string>(), HostRole: ProcessHostRole.GameHost);
        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("hostRole=gamehost");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithSelectionScore_ShouldShowScore()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Galactic, new Dictionary<string, string>(), SelectionScore: 0.85);
        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("score=0.85");
    }

    [Fact]
    public void BuildProcessDiagnosticSummary_WithWorkshopMatchCount_ShouldShowCount()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Galactic, new Dictionary<string, string>(), WorkshopMatchCount: 5);
        var summary = MainViewModelDiagnostics.BuildProcessDiagnosticSummary(process, "unknown");
        summary.Should().Contain("workshopMatches=5");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelAttachHelpers — deeper branches
    // ─────────────────────────────────────────────────────────────────────────

    #region IsStarWarsGProcess — all branches

    [Fact]
    public void IsStarWarsGProcess_ByName_StarWarsG_ShouldReturnTrue()
    {
        var process = new ProcessMetadata(1, "StarWarsG", @"C:\Games\other.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ByName_StarWarsGExe_ShouldReturnTrue()
    {
        var process = new ProcessMetadata(1, "StarWarsG.exe", @"C:\Games\other.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ByMetadata_True_ShouldReturnTrue()
    {
        var process = new ProcessMetadata(1, "game.exe", @"C:\Games\game.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ByMetadata_False_ShouldReturnFalse()
    {
        var process = new ProcessMetadata(1, "game.exe", @"C:\Games\game.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, new Dictionary<string, string> { ["isStarWarsG"] = "false" });
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeFalse();
    }

    [Fact]
    public void IsStarWarsGProcess_ByMetadata_InvalidParse_ShouldFallToPath()
    {
        var process = new ProcessMetadata(1, "game.exe", @"C:\Games\StarWarsG.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, new Dictionary<string, string> { ["isStarWarsG"] = "notbool" });
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ByPath_ShouldReturnTrue()
    {
        var process = new ProcessMetadata(1, "game.exe", @"C:\Games\StarWarsG.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_NoneMatching_ShouldReturnFalse()
    {
        var process = new ProcessMetadata(1, "game.exe", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeFalse();
    }

    [Fact]
    public void IsStarWarsGProcess_NullProcess_ShouldThrow()
    {
        var act = () => MainViewModelAttachHelpers.IsStarWarsGProcess(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ResolveFallbackProfileRecommendation — all branches

    [Fact]
    public void ResolveFallbackProfileRecommendation_RoeWorkshopId_ShouldReturnRoe()
    {
        var process = BuildProcessWithLaunchContext(new[] { "3447786229" });
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().Be("roe_3447786229_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_AotrWorkshopId_ShouldReturnAotr()
    {
        var process = BuildProcessWithLaunchContext(new[] { "1397421866" });
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().Be("aotr_1397421866_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_SwfocExeTarget_ShouldReturnBase()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic);
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().Be("base_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_StarWarsGProcess_ShouldReturnBase()
    {
        var process = new ProcessMetadata(1, "StarWarsG", @"C:\Games\StarWarsG.exe", null, ExeTarget.Unknown, RuntimeMode.Unknown);
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().Be("base_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_SweawExeTarget_ShouldReturnBaseSweaw()
    {
        var process = new ProcessMetadata(1, "sweaw.exe", @"C:\sweaw.exe", null, ExeTarget.Sweaw, RuntimeMode.Galactic);
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().Be("base_sweaw");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_UnknownExeTarget_ShouldReturnNull()
    {
        var process = new ProcessMetadata(1, "other.exe", @"C:\other.exe", null, ExeTarget.Unknown, RuntimeMode.Unknown);
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_CommandLineModId_ShouldResolve()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", "MODID=3447786229", ExeTarget.Swfoc, RuntimeMode.Galactic);
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().Be("roe_3447786229_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_MetadataModId_ShouldResolve()
    {
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string> { ["steamModIdsDetected"] = "3447786229" });
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            new[] { process }, "base_swfoc");
        result.Should().Be("roe_3447786229_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_EmptyProcessList_ShouldReturnNull()
    {
        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            Array.Empty<ProcessMetadata>(), "base_swfoc");
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_NullProcesses_ShouldThrow()
    {
        var act = () => MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(null!, "base_swfoc");
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region BuildAttachStartStatus

    [Fact]
    public void BuildAttachStartStatus_NullVariant_ShouldUseSimpleMessage()
    {
        var result = MainViewModelAttachHelpers.BuildAttachStartStatus("base_swfoc", null);
        result.Should().Contain("base_swfoc");
        result.Should().NotContain("(");
    }

    [Fact]
    public void BuildAttachStartStatus_WithVariant_ShouldIncludeReasonAndConfidence()
    {
        var variant = new ProfileVariantResolution("universal_auto", "base_swfoc", "fingerprint", 0.95);
        var result = MainViewModelAttachHelpers.BuildAttachStartStatus("base_swfoc", variant);
        result.Should().Contain("fingerprint");
        result.Should().Contain("0.95");
    }

    [Fact]
    public void BuildAttachStartStatus_NullProfileId_ShouldThrow()
    {
        var act = () => MainViewModelAttachHelpers.BuildAttachStartStatus(null!, null);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region BuildAttachProcessHintSummary

    [Fact]
    public void BuildAttachProcessHintSummary_LessThan3_ShouldNotShowMore()
    {
        var processes = new[] { BuildProcess(), BuildProcess() };
        var result = MainViewModelAttachHelpers.BuildAttachProcessHintSummary(processes, "unknown");
        result.Should().NotContain("more");
    }

    [Fact]
    public void BuildAttachProcessHintSummary_MoreThan3_ShouldShowMore()
    {
        var processes = Enumerable.Range(0, 5)
            .Select(i => new ProcessMetadata(i, $"proc{i}.exe", $@"C:\proc{i}.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown))
            .ToArray();
        var result = MainViewModelAttachHelpers.BuildAttachProcessHintSummary(processes, "unknown");
        result.Should().Contain("+2 more");
    }

    [Fact]
    public void BuildAttachProcessHintSummary_WithLaunchContext_ShouldIncludeContextInfo()
    {
        var process = BuildProcessWithLaunchContext(new[] { "12345" });
        var result = MainViewModelAttachHelpers.BuildAttachProcessHintSummary(new[] { process }, "unknown");
        result.Should().Contain("Workshop");
    }

    #endregion

    #region IsActionAvailableForCurrentSession — all branches

    [Fact]
    public void IsActionAvailable_NullActionId_ShouldThrow()
    {
        var act = () => MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            null!, BuildActionSpec(ExecutionKind.Sdk), BuildSession(), new Dictionary<string, string>(), out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsActionAvailable_NonMemoryExecution_ShouldReturnTrue()
    {
        var spec = BuildActionSpec(ExecutionKind.Helper);
        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, BuildSession(), new Dictionary<string, string>(), out var reason);
        available.Should().BeTrue();
        reason.Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public void IsActionAvailable_MemoryExecution_NoRequired_ShouldReturnTrue()
    {
        var spec = BuildActionSpec(ExecutionKind.Memory);
        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, BuildSession(), new Dictionary<string, string>(), out _);
        available.Should().BeTrue();
    }

    [Fact]
    public void IsActionAvailable_MemoryExecution_RequiredSymbolResolved_ShouldReturnTrue()
    {
        var spec = BuildActionSpec(ExecutionKind.Memory, requiresSymbol: true);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", new nint(0x100), SymbolValueType.Int32, AddressSource.Signature)
        };
        var session = BuildSession(symbols: symbols);
        var defaultSymbols = new Dictionary<string, string> { ["test"] = "credits" };

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, session, defaultSymbols, out _);
        available.Should().BeTrue();
    }

    [Fact]
    public void IsActionAvailable_MemoryExecution_RequiredSymbolUnresolved_ShouldReturnFalse()
    {
        var spec = BuildActionSpec(ExecutionKind.Memory, requiresSymbol: true);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", nint.Zero, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Unresolved)
        };
        var session = BuildSession(symbols: symbols);
        var defaultSymbols = new Dictionary<string, string> { ["test"] = "credits" };

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, session, defaultSymbols, out var reason);
        available.Should().BeFalse();
        reason.Should().Contain("unresolved");
    }

    [Fact]
    public void IsActionAvailable_DependencyDisabled_ShouldReturnFalse()
    {
        var spec = BuildActionSpec(ExecutionKind.Memory);
        var metadata = new Dictionary<string, string> { ["dependencyDisabledActions"] = "test_action" };
        var process = new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, metadata);
        var session = new AttachSession("test", process, new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)), DateTimeOffset.UtcNow);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test_action", spec, session, new Dictionary<string, string>(), out var reason);
        available.Should().BeFalse();
        reason.Should().Contain("disabled by dependency");
    }

    [Fact]
    public void IsActionAvailable_MemoryExecution_RequiredSymbol_NoDefaultMapping_ShouldReturnTrue()
    {
        // When no default symbol mapping exists for the action, requiredSymbol is null -> returns true
        var spec = BuildActionSpec(ExecutionKind.Memory, requiresSymbol: true);
        var session = BuildSession();
        var emptyDefaults = new Dictionary<string, string>();

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, session, emptyDefaults, out _);
        available.Should().BeTrue();
    }

    [Fact]
    public void IsActionAvailable_CodePatchExecution_RequiredSymbolResolved_ShouldReturnTrue()
    {
        var spec = BuildActionSpec(ExecutionKind.CodePatch, requiresSymbol: true);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", new nint(0x100), SymbolValueType.Int32, AddressSource.Signature)
        };
        var session = BuildSession(symbols: symbols);
        var defaultSymbols = new Dictionary<string, string> { ["test"] = "credits" };

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, session, defaultSymbols, out _);
        available.Should().BeTrue();
    }

    [Fact]
    public void IsActionAvailable_FreezeExecution_RequiredSymbolMissing_ShouldReturnFalse()
    {
        var spec = BuildActionSpec(ExecutionKind.Freeze, requiresSymbol: true);
        var session = BuildSession(); // no symbols
        var defaultSymbols = new Dictionary<string, string> { ["test"] = "missing_symbol" };

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, session, defaultSymbols, out var reason);
        available.Should().BeFalse();
        reason.Should().Contain("missing_symbol");
    }

    [Fact]
    public void IsActionAvailable_SdkExecution_NullSymbolInfo_ShouldReturnFalse()
    {
        // symbol key exists in map but value properties cause unavailability
        var spec = BuildActionSpec(ExecutionKind.Sdk, requiresSymbol: true);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", nint.Zero, SymbolValueType.Int32, AddressSource.None)
        };
        var session = BuildSession(symbols: symbols);
        var defaultSymbols = new Dictionary<string, string> { ["test"] = "credits" };

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test", spec, session, defaultSymbols, out var reason);
        available.Should().BeFalse();
        reason.Should().Contain("unresolved");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelSelectedUnitDraftHelpers — remaining branches
    // ─────────────────────────────────────────────────────────────────────────

    #region TryParseSelectedUnitFloatValues

    [Fact]
    public void TryParseSelectedUnitFloatValues_AllEmpty_ShouldSucceed()
    {
        var inputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs("", "", "", "", "");
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(inputs, out var values, out var error);
        result.Should().BeTrue();
        values.Hp.Should().BeNull();
        values.Shield.Should().BeNull();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_AllValid_ShouldReturnParsedValues()
    {
        var inputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs("100", "50", "30", "1.5", "0.8");
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(inputs, out var values, out _);
        result.Should().BeTrue();
        values.Hp.Should().Be(100f);
        values.Shield.Should().Be(50f);
        values.Speed.Should().Be(30f);
        values.Damage.Should().Be(1.5f);
        values.Cooldown.Should().Be(0.8f);
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidHp_ShouldFail()
    {
        var inputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs("abc", "", "", "", "");
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(inputs, out _, out var error);
        result.Should().BeFalse();
        error.Should().Contain("HP");
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidShield_ShouldFail()
    {
        var inputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs("100", "abc", "", "", "");
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(inputs, out _, out var error);
        result.Should().BeFalse();
        error.Should().Contain("Shield");
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidSpeed_ShouldFail()
    {
        var inputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs("100", "50", "abc", "", "");
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(inputs, out _, out var error);
        result.Should().BeFalse();
        error.Should().Contain("Speed");
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidDamage_ShouldFail()
    {
        var inputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs("100", "50", "30", "abc", "");
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(inputs, out _, out var error);
        result.Should().BeFalse();
        error.Should().Contain("Damage");
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidCooldown_ShouldFail()
    {
        var inputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs("100", "50", "30", "1.5", "abc");
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(inputs, out _, out var error);
        result.Should().BeFalse();
        error.Should().Contain("Cooldown");
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_NullInputs_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(null!, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region TryParseSelectedUnitIntValues

    [Fact]
    public void TryParseSelectedUnitIntValues_BothEmpty_ShouldSucceed()
    {
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues("", "", out var vet, out var faction, out _);
        result.Should().BeTrue();
        vet.Should().BeNull();
        faction.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_ValidValues_ShouldParse()
    {
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues("3", "1", out var vet, out var faction, out _);
        result.Should().BeTrue();
        vet.Should().Be(3);
        faction.Should().Be(1);
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_InvalidVeterancy_ShouldFail()
    {
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues("abc", "1", out _, out _, out var error);
        result.Should().BeFalse();
        error.Should().Contain("Veterancy");
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_InvalidOwnerFaction_ShouldFail()
    {
        var result = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues("3", "abc", out _, out _, out var error);
        result.Should().BeFalse();
        error.Should().Contain("Owner faction");
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_NullVeterancy_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues(null!, "1", out _, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_NullOwnerFaction_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues("1", null!, out _, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelSelectedUnitParsingHelpers — remaining branches
    // ─────────────────────────────────────────────────────────────────────────

    #region TryParseSelectedUnitFloat

    [Fact]
    public void TryParseSelectedUnitFloat_EmptyInput_ShouldReturnNullValue()
    {
        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat("", "err", out var value, out var error).Should().BeTrue();
        value.Should().BeNull();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_ValidFloat_ShouldReturnValue()
    {
        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat("3.14", "err", out var value, out _).Should().BeTrue();
        value.Should().BeApproximately(3.14f, 0.01f);
    }

    [Fact]
    public void TryParseSelectedUnitFloat_InvalidString_ShouldReturnError()
    {
        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat("xyz", "bad float", out _, out var error).Should().BeFalse();
        error.Should().Be("bad float");
    }

    [Fact]
    public void TryParseSelectedUnitFloat_NullInput_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(null!, "err", out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_NullErrorMessage_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat("1", null!, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region TryParseSelectedUnitInt

    [Fact]
    public void TryParseSelectedUnitInt_EmptyInput_ShouldReturnNullValue()
    {
        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt("", "err", out var value, out _).Should().BeTrue();
        value.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitInt_ValidInt_ShouldReturnValue()
    {
        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt("42", "err", out var value, out _).Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryParseSelectedUnitInt_InvalidString_ShouldReturnError()
    {
        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt("xyz", "bad int", out _, out var error).Should().BeFalse();
        error.Should().Be("bad int");
    }

    [Fact]
    public void TryParseSelectedUnitInt_NullInput_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(null!, "err", out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelCreditsHelpers — remaining branches
    // ─────────────────────────────────────────────────────────────────────────

    #region TryParseCreditsValue

    [Fact]
    public void TryParseCreditsValue_ValidPositive_ShouldSucceed()
    {
        MainViewModelCreditsHelpers.TryParseCreditsValue("500", out var value, out _).Should().BeTrue();
        value.Should().Be(500);
    }

    [Fact]
    public void TryParseCreditsValue_Zero_ShouldSucceed()
    {
        MainViewModelCreditsHelpers.TryParseCreditsValue("0", out var value, out _).Should().BeTrue();
        value.Should().Be(0);
    }

    [Fact]
    public void TryParseCreditsValue_Negative_ShouldFail()
    {
        MainViewModelCreditsHelpers.TryParseCreditsValue("-1", out _, out var error).Should().BeFalse();
        error.Should().Contain("Invalid");
    }

    [Fact]
    public void TryParseCreditsValue_NonNumeric_ShouldFail()
    {
        MainViewModelCreditsHelpers.TryParseCreditsValue("abc", out _, out var error).Should().BeFalse();
        error.Should().Contain("Invalid");
    }

    [Fact]
    public void TryParseCreditsValue_NullInput_ShouldThrow()
    {
        var act = () => MainViewModelCreditsHelpers.TryParseCreditsValue(null!, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ResolveCreditsStateTag

    [Fact]
    public void ResolveCreditsStateTag_WithDiagnosticsTag_ShouldReturnTag()
    {
        var diag = new Dictionary<string, object?> { ["creditsStateTag"] = "CUSTOM_TAG" };
        var result = new ActionExecutionResult(true, "ok", AddressSource.None, diag);
        MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, false).Should().Be("CUSTOM_TAG");
    }

    [Fact]
    public void ResolveCreditsStateTag_NoDiagnosticsTag_Freeze_ShouldReturnHookLock()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, true).Should().Be("HOOK_LOCK");
    }

    [Fact]
    public void ResolveCreditsStateTag_NoDiagnosticsTag_NoFreeze_ShouldReturnHookOneshot()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, false).Should().Be("HOOK_ONESHOT");
    }

    [Fact]
    public void ResolveCreditsStateTag_NullResult_ShouldThrow()
    {
        var act = () => MainViewModelCreditsHelpers.ResolveCreditsStateTag(null!, false);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region BuildCreditsSuccessStatus — all branches

    [Fact]
    public void BuildCreditsSuccessStatus_FreezeModeCorrectTag_ShouldReturnSuccess()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(true, 1000, "HOOK_LOCK", "");
        result.IsValid.Should().BeTrue();
        result.ShouldFreeze.Should().BeTrue();
        result.StatusMessage.Should().Contain("HOOK_LOCK");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_FreezeModeWrongTag_ShouldReturnFailure()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(true, 1000, "HOOK_ONESHOT", "");
        result.IsValid.Should().BeFalse();
        result.StatusMessage.Should().Contain("unexpected state");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_OneshotModeCorrectTag_ShouldReturnSuccess()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 500, "HOOK_ONESHOT", "");
        result.IsValid.Should().BeTrue();
        result.ShouldFreeze.Should().BeFalse();
        result.StatusMessage.Should().Contain("HOOK_ONESHOT");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_OneshotModeWrongTag_ShouldReturnFailure()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 500, "HOOK_LOCK", "");
        result.IsValid.Should().BeFalse();
        result.StatusMessage.Should().Contain("unexpected state");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_NullStateTag_ShouldThrow()
    {
        var act = () => MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 0, null!, "");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCreditsSuccessStatus_WithDiagnosticsSuffix_ShouldInclude()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 500, "HOOK_ONESHOT", " [backend=memory]");
        result.StatusMessage.Should().Contain("[backend=memory]");
    }

    #endregion

    #region CreditsStatusResult

    [Fact]
    public void CreditsStatusResult_Success_ShouldStoreAll()
    {
        var result = CreditsStatusResult.Success(true, "message");
        result.IsValid.Should().BeTrue();
        result.ShouldFreeze.Should().BeTrue();
        result.StatusMessage.Should().Be("message");
    }

    [Fact]
    public void CreditsStatusResult_Failure_ShouldStoreFalseValues()
    {
        var result = CreditsStatusResult.Failure("error");
        result.IsValid.Should().BeFalse();
        result.ShouldFreeze.Should().BeFalse();
        result.StatusMessage.Should().Be("error");
    }

    [Fact]
    public void CreditsStatusResult_Success_NullMessage_ShouldThrow()
    {
        var act = () => CreditsStatusResult.Success(false, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreditsStatusResult_Failure_NullMessage_ShouldThrow()
    {
        var act = () => CreditsStatusResult.Failure(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelPayloadHelpers — remaining branches
    // ─────────────────────────────────────────────────────────────────────────

    #region BuildRequiredPayloadTemplate — different required keys

    [Fact]
    public void BuildRequiredPayloadTemplate_SymbolKey_ShouldResolveFromDefaults()
    {
        var required = new JsonArray(JsonValue.Create("symbol")!);
        var defaults = new Dictionary<string, string> { ["test_action"] = "credits" };
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required, defaults, new Dictionary<string, string>());
        payload["symbol"]!.GetValue<string>().Should().Be("credits");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_IntValueKey_SetCredits_ShouldUseDefault()
    {
        var required = new JsonArray(JsonValue.Create("intValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "set_credits", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["intValue"]!.GetValue<int>().Should().Be(1000000);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_IntValueKey_SetUnitCap_ShouldUseDefault()
    {
        var required = new JsonArray(JsonValue.Create("intValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "set_unit_cap", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["intValue"]!.GetValue<int>().Should().Be(99999);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_IntValueKey_OtherAction_ShouldUseZero()
    {
        var required = new JsonArray(JsonValue.Create("intValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "other_action", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["intValue"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_FloatValueKey_ShouldUseOnePointZero()
    {
        var required = new JsonArray(JsonValue.Create("floatValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["floatValue"]!.GetValue<float>().Should().Be(1.0f);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_BoolValueKey_ShouldUseTrue()
    {
        var required = new JsonArray(JsonValue.Create("boolValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["boolValue"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_EnableKey_ShouldUseTrue()
    {
        var required = new JsonArray(JsonValue.Create("enable")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["enable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_FreezeKey_FreezeAction_ShouldBeTrue()
    {
        var required = new JsonArray(JsonValue.Create("freeze")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "freeze_symbol", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["freeze"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_FreezeKey_UnfreezeAction_ShouldBeFalse()
    {
        var required = new JsonArray(JsonValue.Create("freeze")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unfreeze_symbol", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["freeze"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_PatchBytesKey_ShouldReturnDefaults()
    {
        var required = new JsonArray(JsonValue.Create("patchBytes")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["patchBytes"]!.GetValue<string>().Should().Be("90 90 90 90 90");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_OriginalBytesKey_ShouldReturnDefaults()
    {
        var required = new JsonArray(JsonValue.Create("originalBytes")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["originalBytes"]!.GetValue<string>().Should().Be("48 8B 74 24 68");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_HelperHookIdKey_WithMapping_ShouldUseMapping()
    {
        var required = new JsonArray(JsonValue.Create("helperHookId")!);
        var hookDefaults = new Dictionary<string, string> { ["test"] = "my_hook" };
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), hookDefaults);
        payload["helperHookId"]!.GetValue<string>().Should().Be("my_hook");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_HelperHookIdKey_NoMapping_ShouldUseActionId()
    {
        var required = new JsonArray(JsonValue.Create("helperHookId")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["helperHookId"]!.GetValue<string>().Should().Be("test");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_UnitIdKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("unitId")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["unitId"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_EntryMarkerKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("entryMarker")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["entryMarker"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_FactionKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("faction")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["faction"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_GlobalKeyKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("globalKey")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["globalKey"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_NodePathKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("nodePath")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["nodePath"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ValueKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("value")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["value"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_UnknownKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("customKey123")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload["customKey123"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_SkipsNullNodes()
    {
        var required = new JsonArray(null, JsonValue.Create("enable")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test", required, new Dictionary<string, string>(), new Dictionary<string, string>());
        payload.Should().ContainKey("enable");
        payload.Count.Should().Be(1);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_NullArgs_ShouldThrow()
    {
        var act1 = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(null!, new JsonArray(), new Dictionary<string, string>(), new Dictionary<string, string>());
        act1.Should().Throw<ArgumentNullException>();
        var act2 = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate("a", null!, new Dictionary<string, string>(), new Dictionary<string, string>());
        act2.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ApplyActionSpecificPayloadDefaults

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_SetCredits_ShouldAddLockCredits()
    {
        var payload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("set_credits", payload);
        payload.Should().ContainKey("lockCredits");
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_FreezeSymbol_NoIntValue_ShouldAddDefault()
    {
        var payload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("freeze_symbol", payload);
        payload.Should().ContainKey("intValue");
        payload["intValue"]!.GetValue<int>().Should().Be(1000000);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_FreezeSymbol_WithIntValue_ShouldNotOverwrite()
    {
        var payload = new JsonObject { ["intValue"] = 500 };
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("freeze_symbol", payload);
        payload["intValue"]!.GetValue<int>().Should().Be(500);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_OtherAction_ShouldNotModify()
    {
        var payload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("toggle_fog_reveal", payload);
        payload.Count.Should().Be(0);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_NullArgs_ShouldThrow()
    {
        var act1 = () => MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(null!, new JsonObject());
        act1.Should().Throw<ArgumentNullException>();
        var act2 = () => MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("a", null!);
        act2.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region BuildCreditsPayload

    [Fact]
    public void BuildCreditsPayload_ShouldContainAllFields()
    {
        var payload = MainViewModelPayloadHelpers.BuildCreditsPayload(5000, true);
        payload["symbol"]!.GetValue<string>().Should().Be("credits");
        payload["intValue"]!.GetValue<int>().Should().Be(5000);
        payload["lockCredits"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildCreditsPayload_NoLock_ShouldSetFalse()
    {
        var payload = MainViewModelPayloadHelpers.BuildCreditsPayload(0, false);
        payload["lockCredits"]!.GetValue<bool>().Should().BeFalse();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelQuickActionHelpers — remaining branches
    // ─────────────────────────────────────────────────────────────────────────

    #region PopulateActiveFreezes

    [Fact]
    public void PopulateActiveFreezes_BothEmpty_ShouldShowNone()
    {
        var collection = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(collection, Array.Empty<string>(), Array.Empty<string>());
        collection.Should().ContainSingle().Which.Should().Be("(none)");
    }

    [Fact]
    public void PopulateActiveFreezes_WithFrozenSymbols_ShouldAddSnowflake()
    {
        var collection = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(collection, new[] { "credits" }, Array.Empty<string>());
        collection.Should().Contain(x => x.Contains("credits"));
    }

    [Fact]
    public void PopulateActiveFreezes_WithToggles_ShouldAddLock()
    {
        var collection = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(collection, Array.Empty<string>(), new[] { "fog_reveal" });
        collection.Should().Contain(x => x.Contains("fog_reveal"));
    }

    [Fact]
    public void PopulateActiveFreezes_BothPopulated_ShouldNotShowNone()
    {
        var collection = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(collection, new[] { "a" }, new[] { "b" });
        collection.Should().HaveCount(2);
        collection.Should().NotContain("(none)");
    }

    [Fact]
    public void PopulateActiveFreezes_NullArgs_ShouldThrow()
    {
        var act1 = () => MainViewModelQuickActionHelpers.PopulateActiveFreezes(null!, Array.Empty<string>(), Array.Empty<string>());
        act1.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelSpawnHelpers — remaining branches
    // ─────────────────────────────────────────────────────────────────────────

    #region TryBuildBatchInputs — all branches

    [Fact]
    public void TryBuildBatchInputs_NullRequest_ShouldThrow()
    {
        var act = () => MainViewModelSpawnHelpers.TryBuildBatchInputs(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryBuildBatchInputs_NullProfileId_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(null, new SpawnPresetViewItem("id", "name", "u1", "f", "e", 1, 0, "d"), RuntimeMode.Galactic, "1", "0");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("select profile");
    }

    [Fact]
    public void TryBuildBatchInputs_NullPreset_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest("profile", null, RuntimeMode.Galactic, "1", "0");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("select profile");
    }

    [Fact]
    public void TryBuildBatchInputs_UnknownRuntimeMode_ShouldFail()
    {
        var preset = new SpawnPresetViewItem("id", "name", "u1", "f", "e", 1, 0, "d");
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest("profile", preset, RuntimeMode.Unknown, "1", "0");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("runtime mode");
    }

    [Fact]
    public void TryBuildBatchInputs_InvalidQuantity_ShouldFail()
    {
        var preset = new SpawnPresetViewItem("id", "name", "u1", "f", "e", 1, 0, "d");
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest("profile", preset, RuntimeMode.Galactic, "abc", "0");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_ZeroQuantity_ShouldFail()
    {
        var preset = new SpawnPresetViewItem("id", "name", "u1", "f", "e", 1, 0, "d");
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest("profile", preset, RuntimeMode.Galactic, "0", "0");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_InvalidDelay_ShouldFail()
    {
        var preset = new SpawnPresetViewItem("id", "name", "u1", "f", "e", 1, 0, "d");
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest("profile", preset, RuntimeMode.Galactic, "5", "abc");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("delay");
    }

    [Fact]
    public void TryBuildBatchInputs_NegativeDelay_ShouldFail()
    {
        var preset = new SpawnPresetViewItem("id", "name", "u1", "f", "e", 1, 0, "d");
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest("profile", preset, RuntimeMode.Galactic, "5", "-1");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("delay");
    }

    [Fact]
    public void TryBuildBatchInputs_ValidInputs_ShouldSucceed()
    {
        var preset = new SpawnPresetViewItem("id", "name", "u1", "EMPIRE", "entry", 1, 100, "desc");
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest("profile", preset, RuntimeMode.Galactic, "5", "100");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeTrue();
        result.ProfileId.Should().Be("profile");
        result.SelectedPreset.Should().Be(preset);
        result.Quantity.Should().Be(5);
        result.DelayMs.Should().Be(100);
        result.FailureStatus.Should().BeEmpty();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelHotkeyHelpers — BuildDefaultHotkeyPayload branches
    // ─────────────────────────────────────────────────────────────────────────

    #region BuildDefaultHotkeyPayloadJson — all action IDs

    [Theory]
    [InlineData("set_credits", "symbol")]
    [InlineData("freeze_timer", "symbol")]
    [InlineData("toggle_fog_reveal", "symbol")]
    [InlineData("set_unit_cap", "symbol")]
    [InlineData("toggle_instant_build_patch", "enable")]
    [InlineData("set_game_speed", "symbol")]
    [InlineData("freeze_symbol", "symbol")]
    [InlineData("unfreeze_symbol", "symbol")]
    [InlineData("unknown_action", null)]
    public void BuildDefaultHotkeyPayloadJson_AllActions_ShouldContainExpectedKey(string actionId, string? expectedKey)
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(actionId);
        json.Should().NotBeNullOrWhiteSpace();
        if (expectedKey is not null)
        {
            json.Should().Contain(expectedKey);
        }
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_NullActionId_ShouldThrow()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ParseHotkeyPayload

    [Fact]
    public void ParseHotkeyPayload_ValidJson_ShouldParse()
    {
        var binding = new HotkeyBindingItem { PayloadJson = "{\"symbol\":\"credits\"}" };
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload["symbol"]!.GetValue<string>().Should().Be("credits");
    }

    [Fact]
    public void ParseHotkeyPayload_NullPayloadJson_ShouldReturnDefault()
    {
        var binding = new HotkeyBindingItem { ActionId = "set_credits" };
        // Set PayloadJson to null via reflection since setter coerces
        typeof(HotkeyBindingItem).GetField("_payloadJson", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(binding, null);
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_InvalidJson_ShouldReturnDefault()
    {
        var binding = new HotkeyBindingItem { ActionId = "set_credits", PayloadJson = "not json at all" };
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_ArrayJson_ShouldReturnDefault()
    {
        var binding = new HotkeyBindingItem { ActionId = "set_credits", PayloadJson = "[1,2,3]" };
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_NullBinding_ShouldThrow()
    {
        var act = () => MainViewModelHotkeyHelpers.ParseHotkeyPayload(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region BuildHotkeyStatus

    [Fact]
    public void BuildHotkeyStatus_Succeeded_ShouldContainSucceeded()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+1", "set_credits", result);
        status.Should().Contain("succeeded");
        status.Should().Contain("Ctrl+1");
    }

    [Fact]
    public void BuildHotkeyStatus_Failed_ShouldContainFailed()
    {
        var result = new ActionExecutionResult(false, "error", AddressSource.None);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+2", "freeze_timer", result);
        status.Should().Contain("failed");
        status.Should().Contain("error");
    }

    [Fact]
    public void BuildHotkeyStatus_NullGesture_ShouldThrow()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildHotkeyStatus(null!, "a", new ActionExecutionResult(true, "ok", AddressSource.None));
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelRuntimeModeOverrideHelpers — additional branches
    // ─────────────────────────────────────────────────────────────────────────

    #region Normalize — all branches

    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("auto", "Auto")]
    [InlineData("Galactic", "Galactic")]
    [InlineData("galactic", "Galactic")]
    [InlineData("AnyTactical", "AnyTactical")]
    [InlineData("anytactical", "AnyTactical")]
    [InlineData("TacticalLand", "TacticalLand")]
    [InlineData("tacticalland", "TacticalLand")]
    [InlineData("TacticalSpace", "TacticalSpace")]
    [InlineData("tacticalspace", "TacticalSpace")]
    [InlineData("garbage", "Auto")]
    public void Normalize_ShouldReturnExpected(string? input, string expected)
    {
        MainViewModelRuntimeModeOverrideHelpers.Normalize(input).Should().Be(expected);
    }

    #endregion

    #region ResolveEffectiveRuntimeMode — all overrides

    [Theory]
    [InlineData("Galactic", RuntimeMode.Galactic)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical)]
    [InlineData("TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", RuntimeMode.TacticalSpace)]
    public void ResolveEffectiveRuntimeMode_SpecificOverride_ShouldOverride(string overrideValue, RuntimeMode expected)
    {
        var result = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, overrideValue);
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_AutoOverride_ShouldReturnOriginal()
    {
        var result = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.TacticalLand, "Auto");
        result.Should().Be(RuntimeMode.TacticalLand);
    }

    #endregion

    #region ModeOverrideOptions

    [Fact]
    public void ModeOverrideOptions_ShouldContainAll()
    {
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().HaveCount(5);
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("Auto");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("Galactic");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("AnyTactical");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("TacticalLand");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("TacticalSpace");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // HotkeyBindingItem — setter edge cases
    // ─────────────────────────────────────────────────────────────────────────

    #region HotkeyBindingItem

    [Fact]
    public void HotkeyBindingItem_DefaultValues_ShouldBeSet()
    {
        var item = new HotkeyBindingItem();
        item.Gesture.Should().Be("Ctrl+Shift+1");
        item.ActionId.Should().Be("set_credits");
        item.PayloadJson.Should().Be("{}");
    }

    [Fact]
    public void HotkeyBindingItem_SetNullGesture_ShouldSetEmpty()
    {
        var item = new HotkeyBindingItem { Gesture = null! };
        item.Gesture.Should().BeEmpty();
    }

    [Fact]
    public void HotkeyBindingItem_SetNullActionId_ShouldSetEmpty()
    {
        var item = new HotkeyBindingItem { ActionId = null! };
        item.ActionId.Should().BeEmpty();
    }

    [Fact]
    public void HotkeyBindingItem_SetNullPayloadJson_ShouldSetEmptyObject()
    {
        var item = new HotkeyBindingItem { PayloadJson = null! };
        item.PayloadJson.Should().Be("{}");
    }

    [Fact]
    public void HotkeyBindingItem_SetSameGesture_ShouldNotChange()
    {
        var item = new HotkeyBindingItem();
        item.Gesture = "Ctrl+Shift+1"; // same as default
        item.Gesture.Should().Be("Ctrl+Shift+1");
    }

    [Fact]
    public void HotkeyBindingItem_SetSameActionId_ShouldNotChange()
    {
        var item = new HotkeyBindingItem();
        item.ActionId = "set_credits"; // same as default
        item.ActionId.Should().Be("set_credits");
    }

    [Fact]
    public void HotkeyBindingItem_SetSamePayloadJson_ShouldNotChange()
    {
        var item = new HotkeyBindingItem();
        item.PayloadJson = "{}"; // same as default
        item.PayloadJson.Should().Be("{}");
    }

    [Fact]
    public void HotkeyBindingItem_SetDifferentValues_ShouldUpdate()
    {
        var item = new HotkeyBindingItem
        {
            Gesture = "Ctrl+A",
            ActionId = "freeze_timer",
            PayloadJson = "{\"test\":1}"
        };
        item.Gesture.Should().Be("Ctrl+A");
        item.ActionId.Should().Be("freeze_timer");
        item.PayloadJson.Should().Be("{\"test\":1}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // SpawnPresetViewItem — ToCorePreset
    // ─────────────────────────────────────────────────────────────────────────

    #region SpawnPresetViewItem

    [Fact]
    public void SpawnPresetViewItem_ToCorePreset_ShouldMapAllFields()
    {
        var item = new SpawnPresetViewItem("id1", "Name", "unit1", "EMPIRE", "entry1", 5, 100, "desc");
        var core = item.ToCorePreset();
        core.Id.Should().Be("id1");
        core.Name.Should().Be("Name");
        core.UnitId.Should().Be("unit1");
        core.Faction.Should().Be("EMPIRE");
        core.EntryMarker.Should().Be("entry1");
        core.DefaultQuantity.Should().Be(5);
        core.DefaultDelayMs.Should().Be(100);
        core.Description.Should().Be("desc");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // DraftBuildResult — all factory methods
    // ─────────────────────────────────────────────────────────────────────────

    #region DraftBuildResult

    [Fact]
    public void DraftBuildResult_Failed_ShouldStoreMessage()
    {
        var result = DraftBuildResult.Failed("error message");
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("error message");
        result.Draft.Should().BeNull();
    }

    [Fact]
    public void DraftBuildResult_Failed_NullMessage_ShouldThrow()
    {
        var act = () => DraftBuildResult.Failed(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DraftBuildResult_FromDraft_ShouldStoreOkMessage()
    {
        var draft = new SelectedUnitDraft(100f, null, null, null, null, null, null);
        var result = DraftBuildResult.FromDraft(draft);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("ok");
        result.Draft.Should().Be(draft);
    }

    [Fact]
    public void DraftBuildResult_FromDraft_NullDraft_ShouldThrow()
    {
        var act = () => DraftBuildResult.FromDraft(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelDefaults — constant values
    // ─────────────────────────────────────────────────────────────────────────

    #region MainViewModelDefaults

    [Fact]
    public void MainViewModelDefaults_DefaultSymbolByActionId_ShouldContainKeyActions()
    {
        MainViewModelDefaults.DefaultSymbolByActionId.Should().ContainKey("set_credits");
        MainViewModelDefaults.DefaultSymbolByActionId.Should().ContainKey("freeze_timer");
        MainViewModelDefaults.DefaultSymbolByActionId.Should().ContainKey("toggle_fog_reveal");
        MainViewModelDefaults.DefaultSymbolByActionId.Should().ContainKey("set_game_speed");
        MainViewModelDefaults.DefaultSymbolByActionId.Should().ContainKey("freeze_symbol");
        MainViewModelDefaults.DefaultSymbolByActionId.Should().ContainKey("unfreeze_symbol");
    }

    [Fact]
    public void MainViewModelDefaults_DefaultHelperHookByActionId_ShouldContainExpected()
    {
        MainViewModelDefaults.DefaultHelperHookByActionId.Should().ContainKey("spawn_unit_helper");
        MainViewModelDefaults.DefaultHelperHookByActionId["spawn_unit_helper"].Should().Be("spawn_bridge");
    }

    [Fact]
    public void MainViewModelDefaults_Constants_ShouldHaveExpectedValues()
    {
        MainViewModelDefaults.DefaultCreditsValue.Should().Be(1000000);
        MainViewModelDefaults.DefaultUnitCapValue.Should().Be(99999);
        MainViewModelDefaults.DefaultGameSpeedValue.Should().Be(2.0f);
        MainViewModelDefaults.DefaultLaunchTarget.Should().Be("Swfoc");
        MainViewModelDefaults.DefaultLaunchMode.Should().Be("Vanilla");
        MainViewModelDefaults.BaseSwfocProfileId.Should().Be("base_swfoc");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModelFactories — null guard coverage
    // ─────────────────────────────────────────────────────────────────────────

    #region MainViewModelFactories null guards

    [Fact]
    public void CreateCoreCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateCoreCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateSaveCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateSaveCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateLiveOpsCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateLiveOpsCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateQuickCommands_NullContext_ShouldThrow()
    {
        var act = () => MainViewModelFactories.CreateQuickCommands(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateCollections_ShouldReturnAllNonNull()
    {
        var collections = MainViewModelFactories.CreateCollections();
        collections.Profiles.Should().NotBeNull();
        collections.Actions.Should().NotBeNull();
        collections.CatalogSummary.Should().NotBeNull();
        collections.Updates.Should().NotBeNull();
        collections.SaveDiffPreview.Should().NotBeNull();
        collections.Hotkeys.Should().NotBeNull();
        collections.SaveFields.Should().NotBeNull();
        collections.FilteredSaveFields.Should().NotBeNull();
        collections.SavePatchOperations.Should().NotBeNull();
        collections.SavePatchCompatibility.Should().NotBeNull();
        collections.ActionReliability.Should().NotBeNull();
        collections.SelectedUnitTransactions.Should().NotBeNull();
        collections.SpawnPresets.Should().NotBeNull();
        collections.LiveOpsDiagnostics.Should().NotBeNull();
        collections.ModCompatibilityRows.Should().NotBeNull();
        collections.ActiveFreezes.Should().NotBeNull();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // AsyncCommand — additional branch coverage
    // ─────────────────────────────────────────────────────────────────────────

    #region AsyncCommand

    [Fact]
    public void AsyncCommand_CanExecute_WithNoCanExecuteFunc_ShouldReturnTrue()
    {
        var cmd = new SwfocTrainer.App.Infrastructure.AsyncCommand(() => Task.CompletedTask);
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AsyncCommand_CanExecute_WithFuncReturningFalse_ShouldReturnFalse()
    {
        var cmd = new SwfocTrainer.App.Infrastructure.AsyncCommand(() => Task.CompletedTask, () => false);
        cmd.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AsyncCommand_CanExecute_WithFuncReturningTrue_ShouldReturnTrue()
    {
        var cmd = new SwfocTrainer.App.Infrastructure.AsyncCommand(() => Task.CompletedTask, () => true);
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AsyncCommand_NullExecute_ShouldThrow()
    {
        var act = () => new SwfocTrainer.App.Infrastructure.AsyncCommand(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static ProcessMetadata BuildProcess(IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new ProcessMetadata(1, "swfoc.exe", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, metadata ?? new Dictionary<string, string>());
    }

    private static ProcessMetadata BuildProcessWithLaunchContext(IReadOnlyList<string> steamModIds)
    {
        return new ProcessMetadata(1, "swfoc.exe", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Galactic, new Dictionary<string, string>(),
            new LaunchContext(LaunchKind.Workshop, true, steamModIds, null, null, "probe",
                new ProfileRecommendation("test_profile", "workshop_match", 0.9)));
    }

    private static AttachSession BuildSession(IDictionary<string, SymbolInfo>? symbols = null)
    {
        var symbolMap = new SymbolMap(
            new Dictionary<string, SymbolInfo>(symbols ?? new Dictionary<string, SymbolInfo>(), StringComparer.OrdinalIgnoreCase));
        return new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            symbolMap, DateTimeOffset.UtcNow);
    }

    private static ActionSpec BuildActionSpec(ExecutionKind executionKind, bool requiresSymbol = false)
    {
        var schema = new JsonObject();
        if (requiresSymbol)
        {
            schema["required"] = new JsonArray(JsonValue.Create("symbol")!);
        }
        return new ActionSpec("test", ActionCategory.Global, RuntimeMode.Unknown, executionKind, schema, false, 0);
    }
}
