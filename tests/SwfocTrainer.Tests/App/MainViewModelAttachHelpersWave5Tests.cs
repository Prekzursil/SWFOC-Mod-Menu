using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelAttachHelpers:
/// IsStarWarsGProcess all branches, ResolveFallbackProfileRecommendation AOTR path,
/// null fallback, BuildAttachStartStatus variant vs non-variant,
/// HasSteamModId via LaunchContext/CommandLine/Metadata paths,
/// BuildAttachProcessHintSummary with fewer than 3 processes.
/// </summary>
public sealed class MainViewModelAttachHelpersWave5Tests
{
    [Fact]
    public void IsStarWarsGProcess_ByProcessNameExact_ShouldReturnTrue()
    {
        var process = BuildProcess(1, "StarWarsG", ExeTarget.Unknown);
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ByProcessNameWithExe_ShouldReturnTrue()
    {
        var process = BuildProcess(1, "StarWarsG.exe", ExeTarget.Unknown);
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ByMetadataFlag_ShouldReturnTrue()
    {
        var process = BuildProcess(1, "custom.exe", ExeTarget.Unknown,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["isStarWarsG"] = "true"
            },
            path: @"C:\Games\custom.exe");
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ByMetadataFlagFalse_ShouldCheckPath()
    {
        var process = BuildProcess(1, "custom.exe", ExeTarget.Unknown,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["isStarWarsG"] = "false"
            },
            path: @"C:\Games\custom.exe");
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeFalse();
    }

    [Fact]
    public void IsStarWarsGProcess_ByMetadataInvalidBool_ShouldFallToPath()
    {
        var process = BuildProcess(1, "custom.exe", ExeTarget.Unknown,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["isStarWarsG"] = "notabool"
            },
            path: @"C:\Games\custom.exe");
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeFalse();
    }

    [Fact]
    public void IsStarWarsGProcess_ByPath_ShouldReturnTrue()
    {
        var process = BuildProcess(1, "game.exe", ExeTarget.Unknown,
            path: @"C:\Games\StarWarsG.exe");
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_NoMatch_ShouldReturnFalse()
    {
        var process = BuildProcess(1, "swfoc.exe", ExeTarget.Swfoc,
            path: @"C:\Games\swfoc.exe");
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeFalse();
    }

    [Fact]
    public void IsStarWarsGProcess_NullMetadata_ShouldFallToPath()
    {
        var process = new ProcessMetadata(
            ProcessId: 1, ProcessName: "other.exe",
            ProcessPath: @"C:\Games\other.exe",
            CommandLine: null, ExeTarget: ExeTarget.Unknown,
            Mode: RuntimeMode.Unknown, Metadata: null);
        MainViewModelAttachHelpers.IsStarWarsGProcess(process).Should().BeFalse();
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_AotrWorkshopId_ShouldReturnAotrProfile()
    {
        var processes = new[]
        {
            BuildProcess(1, "swfoc.exe", ExeTarget.Swfoc,
                launchContext: new LaunchContext(
                    LaunchKind.Workshop, true,
                    new[] { "1397421866" }, null, null, "cmd",
                    new ProfileRecommendation("aotr_1397421866_swfoc", "workshop_match", 0.9)))
        };

        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            processes, MainViewModelDefaults.BaseSwfocProfileId);
        result.Should().Be("aotr_1397421866_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_AotrViaCommandLine_ShouldReturnAotrProfile()
    {
        var processes = new[]
        {
            new ProcessMetadata(
                ProcessId: 1, ProcessName: "swfoc.exe",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: "STEAMMOD=1397421866",
                ExeTarget: ExeTarget.Swfoc,
                Mode: RuntimeMode.Unknown)
        };

        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            processes, MainViewModelDefaults.BaseSwfocProfileId);
        result.Should().Be("aotr_1397421866_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_AotrViaMetadata_ShouldReturnAotrProfile()
    {
        var processes = new[]
        {
            BuildProcess(1, "swfoc.exe", ExeTarget.Swfoc,
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["steamModIdsDetected"] = "1397421866"
                })
        };

        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            processes, MainViewModelDefaults.BaseSwfocProfileId);
        result.Should().Be("aotr_1397421866_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_NoMatchingProcesses_ShouldReturnNull()
    {
        var processes = new[]
        {
            BuildProcess(1, "unknown.exe", ExeTarget.Unknown,
                path: @"C:\Games\unknown.exe")
        };

        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            processes, MainViewModelDefaults.BaseSwfocProfileId);
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_SwfocTarget_ShouldReturnBaseSwfoc()
    {
        var processes = new[]
        {
            BuildProcess(1, "swfoc.exe", ExeTarget.Swfoc,
                path: @"C:\Games\swfoc.exe")
        };

        var result = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(
            processes, MainViewModelDefaults.BaseSwfocProfileId);
        result.Should().Be(MainViewModelDefaults.BaseSwfocProfileId);
    }

    [Fact]
    public void BuildAttachStartStatus_NoVariant_ShouldShowSimpleMessage()
    {
        var status = MainViewModelAttachHelpers.BuildAttachStartStatus("base_swfoc", null);
        status.Should().Be("Attaching using profile 'base_swfoc'...");
    }

    [Fact]
    public void BuildAttachStartStatus_WithVariant_ShouldShowResolutionDetails()
    {
        var variant = new ProfileVariantResolution(
            RequestedProfileId: "universal_auto",
            ResolvedProfileId: "aotr_swfoc",
            ReasonCode: "workshop_match",
            Confidence: 0.95);

        var status = MainViewModelAttachHelpers.BuildAttachStartStatus("aotr_swfoc", variant);
        status.Should().Contain("universal profile");
        status.Should().Contain("aotr_swfoc");
        status.Should().Contain("workshop_match");
        status.Should().Contain("0.95");
    }

    [Fact]
    public void BuildAttachProcessHintSummary_FewerThanThreeProcesses_ShouldNotShowMore()
    {
        var processes = new[]
        {
            BuildProcess(1, "swfoc.exe", ExeTarget.Swfoc),
            BuildProcess(2, "sweaw.exe", ExeTarget.Sweaw)
        };

        var summary = MainViewModelAttachHelpers.BuildAttachProcessHintSummary(processes, "unknown");
        summary.Should().StartWith("Detected game processes:");
        summary.Should().Contain("swfoc.exe:1");
        summary.Should().Contain("sweaw.exe:2");
        summary.Should().NotContain("more");
    }

    [Fact]
    public void BuildAttachProcessHintSummary_ExactlyThreeProcesses_ShouldNotShowMore()
    {
        var processes = new[]
        {
            BuildProcess(1, "a.exe", ExeTarget.Swfoc),
            BuildProcess(2, "b.exe", ExeTarget.Swfoc),
            BuildProcess(3, "c.exe", ExeTarget.Sweaw)
        };

        var summary = MainViewModelAttachHelpers.BuildAttachProcessHintSummary(processes, "unknown");
        summary.Should().NotContain("more");
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_NonSymbolExecutionKind_ShouldReturnTrue()
    {
        var spec = new ActionSpec(
            Id: "custom_action",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Helper,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "custom_action", spec, session,
            MainViewModelDefaults.DefaultSymbolByActionId, out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_NoRequiredArray_ShouldReturnTrue()
    {
        var spec = new ActionSpec(
            Id: "test_action",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test_action", spec, session,
            MainViewModelDefaults.DefaultSymbolByActionId, out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_RequiredWithoutSymbolKey_ShouldReturnTrue()
    {
        var required = new JsonArray(JsonValue.Create("intValue")!);
        var spec = new ActionSpec(
            Id: "test_action",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Memory,
            PayloadSchema: new JsonObject { ["required"] = required },
            VerifyReadback: false,
            CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "test_action", spec, session,
            MainViewModelDefaults.DefaultSymbolByActionId, out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_SymbolRequiredButNoDefaultMapping_ShouldReturnTrue()
    {
        var required = new JsonArray(JsonValue.Create("symbol")!);
        var spec = new ActionSpec(
            Id: "unmapped_action",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject { ["required"] = required },
            VerifyReadback: false,
            CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "unmapped_action", spec, session,
            MainViewModelDefaults.DefaultSymbolByActionId, out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_HealthySymbol_ShouldReturnTrue()
    {
        var required = new JsonArray(JsonValue.Create("symbol")!);
        var spec = new ActionSpec(
            Id: "set_credits",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject { ["required"] = required },
            VerifyReadback: false,
            CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic,
            symbols: new[] { new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature) });

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "set_credits", spec, session,
            MainViewModelDefaults.DefaultSymbolByActionId, out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_DegradedSymbol_ShouldReturnTrue()
    {
        var required = new JsonArray(JsonValue.Create("symbol")!);
        var spec = new ActionSpec(
            Id: "set_credits",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Freeze,
            PayloadSchema: new JsonObject { ["required"] = required },
            VerifyReadback: false,
            CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x2000, SymbolValueType.Int32,
                    AddressSource.Fallback, HealthStatus: SymbolHealthStatus.Degraded)
            });

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "set_credits", spec, session,
            MainViewModelDefaults.DefaultSymbolByActionId, out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_CodePatchKind_SymbolUnresolved_ShouldReturnFalse()
    {
        var required = new JsonArray(JsonValue.Create("symbol")!);
        var spec = new ActionSpec(
            Id: "set_credits",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.CodePatch,
            PayloadSchema: new JsonObject { ["required"] = required },
            VerifyReadback: false,
            CooldownMs: 0);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "set_credits", spec, session,
            MainViewModelDefaults.DefaultSymbolByActionId, out var reason);

        available.Should().BeFalse();
        reason.Should().Contain("unresolved");
    }

    private static ProcessMetadata BuildProcess(
        int pid,
        string name,
        ExeTarget target,
        IReadOnlyDictionary<string, string>? metadata = null,
        LaunchContext? launchContext = null,
        string? path = null)
    {
        return new ProcessMetadata(
            ProcessId: pid,
            ProcessName: name,
            ProcessPath: path ?? $@"C:\Games\{name}",
            CommandLine: null,
            ExeTarget: target,
            Mode: RuntimeMode.Unknown,
            Metadata: metadata,
            LaunchContext: launchContext);
    }

    private static AttachSession BuildSession(
        RuntimeMode mode,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<SymbolInfo>? symbols = null)
    {
        var symbolMap = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols ?? Array.Empty<SymbolInfo>())
        {
            symbolMap[symbol.Name] = symbol;
        }

        return new AttachSession(
            ProfileId: "test_profile",
            Process: new ProcessMetadata(
                ProcessId: 100, ProcessName: "StarWarsG.exe",
                ProcessPath: @"C:\Games\StarWarsG.exe",
                CommandLine: "STEAMMOD=1397421866",
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode, Metadata: metadata),
            Build: new ProfileBuild("test_profile", "build", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(symbolMap),
            AttachedAt: DateTimeOffset.UtcNow);
    }
}
