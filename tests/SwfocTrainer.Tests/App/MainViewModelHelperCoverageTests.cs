using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelHelperCoverageTests
{
    [Fact]
    public void BuildAttachProcessHintSummary_ShouldIncludeFirstThreeEntriesAndMoreSuffix()
    {
        var processes = new[]
        {
            BuildProcess(1, "StarWarsG.exe", ExeTarget.Swfoc),
            BuildProcess(2, "swfoc.exe", ExeTarget.Swfoc),
            BuildProcess(3, "sweaw.exe", ExeTarget.Sweaw),
            BuildProcess(4, "another.exe", ExeTarget.Swfoc)
        };

        var summary = MainViewModelAttachHelpers.BuildAttachProcessHintSummary(processes, "unknown");

        summary.Should().StartWith("Detected game processes:");
        summary.Should().Contain("StarWarsG.exe:1");
        summary.Should().Contain("swfoc.exe:2");
        summary.Should().Contain("sweaw.exe:3");
        summary.Should().Contain(", +1 more");
        summary.Should().NotContain("another.exe:4");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_ShouldReturnRoeProfile_WhenRoeWorkshopIdPresent()
    {
        var processes = new[]
        {
            BuildProcess(
                11,
                "swfoc.exe",
                ExeTarget.Swfoc,
                launchContext: new LaunchContext(
                    LaunchKind.Workshop,
                    CommandLineAvailable: true,
                    SteamModIds: new[] { "3447786229" },
                    ModPathRaw: null,
                    ModPathNormalized: null,
                    DetectedVia: "cmd",
                    Recommendation: new ProfileRecommendation("roe_3447786229_swfoc", "workshop_match", 0.9)))
        };

        var profileId = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(processes, MainViewModelDefaults.BaseSwfocProfileId);

        profileId.Should().Be("roe_3447786229_swfoc");
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_ShouldReturnBaseSwfoc_ForStarWarsGProcess()
    {
        var processes = new[]
        {
            BuildProcess(
                22,
                "custom",
                ExeTarget.Unknown,
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["isStarWarsG"] = "true"
                })
        };

        var profileId = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(processes, MainViewModelDefaults.BaseSwfocProfileId);

        profileId.Should().Be(MainViewModelDefaults.BaseSwfocProfileId);
    }

    [Fact]
    public void ResolveFallbackProfileRecommendation_ShouldReturnBaseSweaw_WhenOnlySweawDetected()
    {
        var processes = new[]
        {
            BuildProcess(33, "sweaw.exe", ExeTarget.Sweaw)
        };

        var profileId = MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(processes, MainViewModelDefaults.BaseSwfocProfileId);

        profileId.Should().Be("base_sweaw");
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_ShouldReturnFalse_WhenDependencyDisablesAction()
    {
        var action = BuildAction(MainViewModelDefaults.ActionSetCredits, ExecutionKind.Sdk, MainViewModelDefaults.PayloadKeySymbol, MainViewModelDefaults.PayloadKeyIntValue);
        var session = BuildSession(
            RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = MainViewModelDefaults.ActionSetCredits
            });

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            MainViewModelDefaults.ActionSetCredits,
            action,
            session,
            MainViewModelDefaults.DefaultSymbolByActionId,
            out var reason);

        available.Should().BeFalse();
        reason.Should().Be("action is disabled by dependency validation for this attachment.");
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_ShouldReturnFalse_WhenRequiredSymbolUnresolved()
    {
        var action = BuildAction(MainViewModelDefaults.ActionSetCredits, ExecutionKind.Sdk, MainViewModelDefaults.PayloadKeySymbol, MainViewModelDefaults.PayloadKeyIntValue);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            MainViewModelDefaults.ActionSetCredits,
            action,
            session,
            MainViewModelDefaults.DefaultSymbolByActionId,
            out var reason);

        available.Should().BeFalse();
        reason.Should().Be("required symbol 'credits' is unresolved for this attachment.");
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_ShouldReturnTrue_WhenRequiredSymbolIsHealthy()
    {
        var action = BuildAction(MainViewModelDefaults.ActionSetCredits, ExecutionKind.Sdk, MainViewModelDefaults.PayloadKeySymbol, MainViewModelDefaults.PayloadKeyIntValue);
        var session = BuildSession(
            RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            });

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            MainViewModelDefaults.ActionSetCredits,
            action,
            session,
            MainViewModelDefaults.DefaultSymbolByActionId,
            out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_ShouldReturnTrue_ForNonSymbolExecutionKinds()
    {
        var action = BuildAction("helper_action", ExecutionKind.Helper, MainViewModelDefaults.PayloadKeySymbol);
        var session = BuildSession(RuntimeMode.Galactic);
        var symbolMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["helper_action"] = MainViewModelDefaults.SymbolCredits
        };

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "helper_action",
            action,
            session,
            symbolMap,
            out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_ShouldReturnTrue_WhenPayloadDoesNotRequireSymbol()
    {
        var action = BuildAction("set_credits", ExecutionKind.Sdk, MainViewModelDefaults.PayloadKeyIntValue);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "set_credits",
            action,
            session,
            MainViewModelDefaults.DefaultSymbolByActionId,
            out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void IsActionAvailableForCurrentSession_ShouldReturnTrue_WhenDefaultSymbolMappingMissing()
    {
        var action = BuildAction("custom_action", ExecutionKind.Sdk, MainViewModelDefaults.PayloadKeySymbol);
        var session = BuildSession(RuntimeMode.Galactic);

        var available = MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            "custom_action",
            action,
            session,
            MainViewModelDefaults.DefaultSymbolByActionId,
            out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldPopulateExpectedDefaults()
    {
        var required = new JsonArray(
            JsonValue.Create(MainViewModelDefaults.PayloadKeySymbol),
            JsonValue.Create(MainViewModelDefaults.PayloadKeyIntValue),
            JsonValue.Create(MainViewModelDefaults.PayloadKeyFreeze),
            JsonValue.Create("patchBytes"),
            JsonValue.Create("helperHookId"),
            null,
            JsonValue.Create(string.Empty));

        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionSetCredits,
            required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadKeySymbol]!.GetValue<string>().Should().Be("credits");
        payload[MainViewModelDefaults.PayloadKeyIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultCreditsValue);
        payload[MainViewModelDefaults.PayloadKeyFreeze]!.GetValue<bool>().Should().BeTrue();
        payload["patchBytes"]!.GetValue<string>().Should().Be("90 90 90 90 90");
        payload["helperHookId"]!.GetValue<string>().Should().Be(MainViewModelDefaults.ActionSetCredits);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldSetLockCreditsFalse_ForSetCredits()
    {
        var payload = new JsonObject();

        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(MainViewModelDefaults.ActionSetCredits, payload);

        payload[MainViewModelDefaults.PayloadKeyLockCredits]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldSetDefaultInt_WhenFreezeMissingIntValue()
    {
        var payload = new JsonObject();

        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(MainViewModelDefaults.ActionFreezeSymbol, payload);

        payload[MainViewModelDefaults.PayloadKeyIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultCreditsValue);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldNotOverwriteExistingIntValue_ForFreezeSymbol()
    {
        var payload = new JsonObject
        {
            [MainViewModelDefaults.PayloadKeyIntValue] = 42
        };

        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(MainViewModelDefaults.ActionFreezeSymbol, payload);

        payload[MainViewModelDefaults.PayloadKeyIntValue]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void BuildCreditsPayload_ShouldSetCreditsSymbolAndValues()
    {
        var payload = MainViewModelPayloadHelpers.BuildCreditsPayload(value: 12345, lockCredits: true);

        payload[MainViewModelDefaults.PayloadKeySymbol]!.GetValue<string>().Should().Be("credits");
        payload[MainViewModelDefaults.PayloadKeyIntValue]!.GetValue<int>().Should().Be(12345);
        payload[MainViewModelDefaults.PayloadKeyLockCredits]!.GetValue<bool>().Should().BeTrue();
    }

    [Theory]
    [InlineData("100", typeof(int), "100")]
    [InlineData("4294967296", typeof(long), "4294967296")]
    [InlineData("true", typeof(bool), "True")]
    [InlineData("1.5f", typeof(float), "1.5")]
    [InlineData("2.75", typeof(double), "2.75")]
    [InlineData("not-a-number", typeof(string), "not-a-number")]
    public void ParsePrimitive_ShouldReturnExpectedTypes(string input, Type expectedType, string expectedString)
    {
        var parsed = MainViewModelDiagnostics.ParsePrimitive(input);

        parsed.Should().BeOfType(expectedType);
        parsed.ToString().Should().Be(expectedString);
    }

    [Fact]
    public void ResolveBundleGateResult_ShouldMapUnavailableAndNullStates()
    {
        var blocked = MainViewModelDiagnostics.ResolveBundleGateResult(
            new ActionReliabilityViewItem("set_credits", "unavailable", "CAPABILITY_REQUIRED_MISSING", 0.9, "missing"),
            "unknown");
        var passed = MainViewModelDiagnostics.ResolveBundleGateResult(
            new ActionReliabilityViewItem("set_credits", "stable", "CAPABILITY_PROBE_PASS", 1.0, "ok"),
            "unknown");
        var unknown = MainViewModelDiagnostics.ResolveBundleGateResult(null, "unknown");

        blocked.Should().Be("blocked");
        passed.Should().Be("bundle_pass");
        unknown.Should().Be("unknown");
    }

    [Fact]
    public void BuildDiagnosticsStatusSuffix_ShouldReadAliasKeys()
    {
        var result = new ActionExecutionResult(
            Succeeded: false,
            Message: "failed",
            AddressSource: AddressSource.Signature,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backendRoute"] = "extender",
                ["reasonCode"] = "CAPABILITY_REQUIRED_MISSING",
                ["probeReasonCode"] = "CAPABILITY_PROBE_PASS",
                ["hookState"] = "ready",
                ["hybridExecution"] = true
            });

        var suffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

        suffix.Should().Contain("backend=extender");
        suffix.Should().Contain("routeReasonCode=CAPABILITY_REQUIRED_MISSING");
        suffix.Should().Contain("capabilityProbeReasonCode=CAPABILITY_PROBE_PASS");
        suffix.Should().Contain("hookState=ready");
        suffix.Should().Contain("hybridExecution=True");
    }

    [Fact]
    public void BuildProcessDependencySegment_ShouldIncludeMessage_WhenNotPass()
    {
        MainViewModelDiagnostics.BuildProcessDependencySegment("Pass", "ignored")
            .Should().Be("dependency=Pass");
        MainViewModelDiagnostics.BuildProcessDependencySegment("SoftFail", "missing parent")
            .Should().Be("dependency=SoftFail (missing parent)");
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_ShouldParseValues_AndSupportBlankInputs()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                HpInput: "100",
                ShieldInput: "",
                SpeedInput: "2.5",
                DamageInput: "3",
                CooldownInput: "  "),
            out var values,
            out var error);

        ok.Should().BeTrue();
        error.Should().BeEmpty();
        values.Hp.Should().Be(100f);
        values.Shield.Should().BeNull();
        values.Speed.Should().Be(2.5f);
        values.Damage.Should().Be(3f);
        values.Cooldown.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_ShouldFail_WhenDamageIsInvalid()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                HpInput: "100",
                ShieldInput: "100",
                SpeedInput: "100",
                DamageInput: "invalid",
                CooldownInput: "1"),
            out var values,
            out var error);

        ok.Should().BeFalse();
        error.Should().Be("Damage multiplier must be a number.");
        values.Hp.Should().BeNull();
        values.Shield.Should().BeNull();
        values.Speed.Should().BeNull();
        values.Damage.Should().BeNull();
        values.Cooldown.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_ShouldFail_WhenOwnerFactionInvalid()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues(
            veterancyInput: "3",
            ownerFactionInput: "invalid",
            out var veterancy,
            out var ownerFaction,
            out var error);

        ok.Should().BeFalse();
        veterancy.Should().Be(3);
        ownerFaction.Should().BeNull();
        error.Should().Be("Owner faction must be an integer.");
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_ShouldTreatWhitespaceAsNull_AndSucceed()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues(
            veterancyInput: "",
            ownerFactionInput: " ",
            out var veterancy,
            out var ownerFaction,
            out var error);

        ok.Should().BeTrue();
        veterancy.Should().BeNull();
        ownerFaction.Should().BeNull();
        error.Should().BeEmpty();
    }

    [Fact]
    public void ResolveLaunchMode_ShouldMapKnownModes_AndFallbackToVanilla()
    {
        var steamMode = InvokeResolveLaunchMode("SteamMod");
        var modPathMode = InvokeResolveLaunchMode("ModPath");
        var fallbackMode = InvokeResolveLaunchMode("anything");

        steamMode.Should().Be(GameLaunchMode.SteamMod);
        modPathMode.Should().Be(GameLaunchMode.ModPath);
        fallbackMode.Should().Be(GameLaunchMode.Vanilla);
    }

    [Fact]
    public void ResolveProfileWorkshopChain_ShouldPrependParentsAndDeduplicate()
    {
        var profile = BuildProfile(
            steamWorkshopId: "child",
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "child,parentA,parentB,parentB",
                ["parentDependencies"] = "parentB,parentRoot"
            });

        var chain = InvokeResolveProfileWorkshopChain(profile);

        chain.Should().Equal("parentRoot", "child", "parentA", "parentB");
    }

    private static GameLaunchMode InvokeResolveLaunchMode(string mode)
    {
        var method = typeof(MainViewModel).GetMethod("ResolveLaunchMode", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (GameLaunchMode)method!.Invoke(null, new object?[] { mode })!;
    }

    private static IReadOnlyList<string> InvokeResolveProfileWorkshopChain(TrainerProfile profile)
    {
        var method = typeof(MainViewModel).GetMethod("ResolveProfileWorkshopChain", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return ((IReadOnlyList<string>?)method!.Invoke(null, new object?[] { profile }))!;
    }

    private static ActionSpec BuildAction(string id, ExecutionKind kind, params string[] requiredKeys)
    {
        var required = new JsonArray(requiredKeys.Select(static key => (JsonNode)JsonValue.Create(key)!).ToArray());
        return new ActionSpec(
            Id: id,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: kind,
            PayloadSchema: new JsonObject
            {
                ["required"] = required
            },
            VerifyReadback: false,
            CooldownMs: 0);
    }

    private static ProcessMetadata BuildProcess(
        int pid,
        string name,
        ExeTarget target,
        IReadOnlyDictionary<string, string>? metadata = null,
        LaunchContext? launchContext = null)
    {
        metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["commandLineAvailable"] = "True",
            ["steamModIdsDetected"] = "",
            ["detectedVia"] = "probe"
        };

        return new ProcessMetadata(
            ProcessId: pid,
            ProcessName: name,
            ProcessPath: $@"C:\Games\{name}",
            CommandLine: "",
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
                ProcessId: 100,
                ProcessName: "StarWarsG.exe",
                ProcessPath: @"C:\Games\StarWarsG.exe",
                CommandLine: "STEAMMOD=1397421866",
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: metadata,
                LaunchContext: null),
            Build: new ProfileBuild("test_profile", "build", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(symbolMap),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static TrainerProfile BuildProfile(string? steamWorkshopId, IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: steamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }
}
