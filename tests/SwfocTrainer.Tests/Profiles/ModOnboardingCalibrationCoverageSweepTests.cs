using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ModOnboardingCalibrationCoverageSweepTests
{
    [Fact]
    public void ModOnboardingHelpers_ShouldResolveFallbacksAndNormalizeCollections()
    {
        var seed = new GeneratedProfileSeed(
            DraftProfileId: "",
            DisplayName: "",
            BaseProfileId: "",
            LaunchSamples: Array.Empty<ModLaunchSample>(),
            SourceRunId: " run-42 ",
            Confidence: 0.75,
            ParentProfile: "parent_profile",
            RequiredWorkshopIds: new[] { "200", "100" },
            ProfileAliases: new[] { "Rise Of Clones", " roc " },
            LocalPathHints: new[] { " Mods/Rise-Of-Clones ", "steammod", "a" },
            WorkshopId: "300",
            RequiredCapabilities: new[] { "hero_lab", "spawn" },
            RiskLevel: "EXTREME",
            Title: "Rise Of Clones",
            CandidateBaseProfile: "candidate_profile");

        InvokeStatic<string?>(nameof(ModOnboardingService), "ResolveSeedDraftProfileId", seed).Should().Be("workshop_300");
        InvokeStatic<string?>(nameof(ModOnboardingService), "ResolveSeedDisplayName", seed).Should().Be("Rise Of Clones");
        InvokeStatic<string?>(nameof(ModOnboardingService), "ResolveBaseProfileId", seed).Should().Be("candidate_profile");
        InvokeStatic<string>(nameof(ModOnboardingService), "ResolveParentProfile", seed, "fallback_profile").Should().Be("parent_profile");
        InvokeStatic<string>(nameof(ModOnboardingService), "NormalizeRiskLevel", "EXTREME").Should().Be("medium");
        InvokeStatic<string>(nameof(ModOnboardingService), "NormalizeRiskLevel", "HIGH").Should().Be("high");

        var workshopIds = InvokeStatic<IReadOnlyList<string>>(
            nameof(ModOnboardingService),
            "MergeWorkshopIds",
            "300",
            new[] { "200", "100", "200" },
            new[] { "050", "300" });
        workshopIds.Should().Equal("050", "100", "200", "300");

        var pathHints = InvokeStatic<IReadOnlyList<string>>(
            nameof(ModOnboardingService),
            "MergePathHints",
            new[] { " Mods/Rise-Of-Clones ", "steammod", "a" },
            new[] { "rise_of_clones", "custom", "swfoc" });
        pathHints.Should().Contain("rise_of_clones");
        pathHints.Should().Contain("custom");
        pathHints.Should().NotContain("steammod");
        pathHints.Should().NotContain("swfoc");

        var capabilities = InvokeStatic<IReadOnlyList<string>>(
            nameof(ModOnboardingService),
            "MergeRequiredCapabilities",
            new[] { "spawn", "hero_lab" },
            new[] { "hero_lab", "building_ops" });
        capabilities.Should().Equal("building_ops", "hero_lab", "spawn");
    }

    [Fact]
    public void ModOnboardingHelpers_ShouldInferWorkshopIdsPathHintsTokensAndAliases()
    {
        var samples = new[]
        {
            new ModLaunchSample(
                "StarWarsG.exe",
                @"C:\Games\Mods\Republic At War\StarWarsG.exe",
                "STEAMMOD=111 STEAMMOD=222 MODPATH=\"Mods\\Rise-Of-Clones\" /windowed"),
            new ModLaunchSample(
                "StarWarsG.exe",
                @"/games/custom/Thrawns-Revenge/StarWarsG.exe",
                "steammod=222")
        };

        var workshopIds = InvokeStatic<IReadOnlyList<string>>(nameof(ModOnboardingService), "InferWorkshopIds", (object)samples);
        workshopIds.Should().Equal("111", "222");

        var pathHints = InvokeStatic<IReadOnlyList<string>>(nameof(ModOnboardingService), "InferPathHints", (object)samples);
        pathHints.Should().Contain("republic");
        pathHints.Should().Contain("rise");
        pathHints.Should().Contain("revenge");

        var tokens = InvokeStatic<IEnumerable<string>>(nameof(ModOnboardingService), "TokenizeHintInput", @"Mods\Rise-Of-Clones (Campaign)");
        tokens.Should().Contain(new[] { "mods", "rise", "of", "clones", "campaign" });
        InvokeStatic<bool>(nameof(ModOnboardingService), "IsPathHintCandidate", "swfoc").Should().BeFalse();
        InvokeStatic<bool>(nameof(ModOnboardingService), "IsPathHintCandidate", "rise").Should().BeTrue();

        var aliases = InvokeStatic<IReadOnlyList<string>>(
            nameof(ModOnboardingService),
            "InferAliases",
            "custom_rise",
            "Rise Of Clones",
            new[] { "ROC", "Rise-Of-Clones", "  " });
        aliases.Should().Equal("custom_rise", "rise_of_clones", "roc");
    }

    [Fact]
    public async Task ModCalibrationService_ShouldReportStaticAnalysisAndSessionUnavailableArtifact()
    {
        using var outputDir = new TempDirectory("swfoc-calibration-sessionless");
        var profile = BuildProfileWithActions("custom_profile");
        var service = new ModCalibrationService(new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()));

        var report = await service.BuildCompatibilityReportAsync(
            profile,
            session: null,
            dependencyValidation: new DependencyValidationResult(
                DependencyValidationStatus.SoftFail,
                "soft",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
            catalog: null,
            cancellationToken: CancellationToken.None);

        report.RuntimeMode.Should().Be(RuntimeMode.Unknown);
        report.DependencyStatus.Should().Be(DependencyValidationStatus.SoftFail);
        report.UnresolvedCriticalSymbols.Should().Be(2);
        report.PromotionReady.Should().BeFalse();
        report.Actions.Should().OnlyContain(x => x.State == ActionReliabilityState.Unavailable && x.ReasonCode == "session_unavailable");
        report.Notes.Should().Contain(x => x.Contains("static profile analysis", StringComparison.OrdinalIgnoreCase));
        report.Notes.Should().Contain(x => x.Contains("SoftFail", StringComparison.OrdinalIgnoreCase));
        report.Notes.Should().Contain(x => x.Contains("critical symbol", StringComparison.OrdinalIgnoreCase));

        var artifact = await service.ExportCalibrationArtifactAsync(new ModCalibrationArtifactRequest(
            ProfileId: profile.Id,
            OutputDirectory: outputDir.Path,
            Session: null,
            OperatorNotes: "sessionless"));

        artifact.Succeeded.Should().BeTrue();
        artifact.ModuleFingerprint.Should().Be("session_unavailable");
        artifact.Candidates.Should().BeEmpty();
        artifact.Warnings.Should().Contain(x => x.Contains("No attach session", StringComparison.OrdinalIgnoreCase));
        File.Exists(artifact.ArtifactPath).Should().BeTrue();
    }

    [Fact]
    public async Task ModCalibrationService_ShouldInferHardFailFromSessionMetadata()
    {
        var profile = BuildProfileWithActions("custom_profile");
        var reliability = new[]
        {
            new ActionReliabilityInfo("spawn_tactical_entity", ActionReliabilityState.Stable, "ready", 0.99)
        };
        var service = new ModCalibrationService(new StubActionReliabilityService(reliability));
        var session = new AttachSession(
            "custom_profile",
            new ProcessMetadata(
                42,
                "StarWarsG.exe",
                @"C:\Games\StarWarsG.exe",
                "STEAMMOD=123",
                ExeTarget.Swfoc,
                RuntimeMode.Galactic,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dependencyValidation"] = "HardFail"
                }),
            new ProfileBuild("custom_profile", "test", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc, "STEAMMOD=123", 42),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits"] = new("credits", 0x1, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy),
                ["selected_hp"] = new("selected_hp", 0x2, SymbolValueType.Float, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy)
            }),
            DateTimeOffset.UtcNow);

        var report = await service.BuildCompatibilityReportAsync(profile, session, dependencyValidation: null, catalog: null, cancellationToken: CancellationToken.None);

        report.DependencyStatus.Should().Be(DependencyValidationStatus.HardFail);
        report.PromotionReady.Should().BeFalse();
        report.Notes.Should().Contain(x => x.Contains("HardFail", StringComparison.OrdinalIgnoreCase));
        report.Actions.Should().ContainSingle(x => x.ActionId == "spawn_tactical_entity" && x.State == ActionReliabilityState.Stable);
    }

    private static TrainerProfile BuildProfileWithActions(string profileId)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: "Coverage Profile",
            Inherits: "base_swfoc",
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["place_planet_building"] = new(
                    "place_planet_building",
                    ActionCategory.Campaign,
                    RuntimeMode.Galactic,
                    ExecutionKind.Helper,
                    new JsonObject(),
                    VerifyReadback: true,
                    CooldownMs: 250),
                ["spawn_tactical_entity"] = new(
                    "spawn_tactical_entity",
                    ActionCategory.Tactical,
                    RuntimeMode.AnyTactical,
                    ExecutionKind.Helper,
                    new JsonObject(),
                    VerifyReadback: true,
                    CooldownMs: 250)
            },
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "base_swfoc_steam_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["criticalSymbols"] = "credits,selected_hp"
            });
    }

    private static T InvokeStatic<T>(string typeName, string methodName, params object?[] args)
    {
        var type = typeof(ModOnboardingService);
        if (!string.Equals(type.Name, typeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected type lookup: {typeName}");
        }

        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, methodName);
        return (T)method.Invoke(null, args)!;
    }

    private sealed class StubActionReliabilityService : IActionReliabilityService
    {
        private readonly IReadOnlyList<ActionReliabilityInfo> _values;

        public StubActionReliabilityService(IReadOnlyList<ActionReliabilityInfo> values)
        {
            _values = values;
        }

        public IReadOnlyList<ActionReliabilityInfo> Evaluate(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
        {
            _ = profile;
            _ = session;
            _ = catalog;
            return _values;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
