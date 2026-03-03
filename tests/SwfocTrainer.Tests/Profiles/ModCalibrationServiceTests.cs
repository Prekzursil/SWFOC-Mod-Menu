using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ModCalibrationServiceTests
{
    [Fact]
    public async Task BuildCompatibilityReportAsync_ShouldBlockPromotion_WhenCriticalSymbolsUnresolved()
    {
        var profile = BuildCriticalSymbolProfile();
        var session = BuildSessionWithUnresolvedCredit(profile.Id);

        var service = new ModCalibrationService(new ActionReliabilityService());
        var report = await service.BuildCompatibilityReportAsync(profile, session);

        report.PromotionReady.Should().BeFalse();
        report.UnresolvedCriticalSymbols.Should().Be(1);
        report.Notes.Should().Contain(x => x.Contains("critical symbol", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_ShouldWriteCandidateArtifactJson()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"swfoc-calibration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try
        {
            var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits"] = new("credits", 0x1111, SymbolValueType.Int32, AddressSource.Signature, "ok", 0.92, SymbolHealthStatus.Healthy, "healthy", DateTimeOffset.UtcNow),
                ["selected_hp"] = new("selected_hp", 0x2222, SymbolValueType.Float, AddressSource.Fallback, "fallback", 0.61, SymbolHealthStatus.Degraded, "fallback_only", DateTimeOffset.UtcNow)
            });

            var session = new AttachSession(
                ProfileId: "custom_test",
                Process: new ProcessMetadata(2222, "StarWarsG.exe", @"C:\Games\StarWarsG.exe", "STEAMMOD=555000111", ExeTarget.Swfoc, RuntimeMode.AnyTactical),
                Build: new ProfileBuild("custom_test", "test", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc, "STEAMMOD=555000111", 2222),
                Symbols: symbols,
                AttachedAt: DateTimeOffset.UtcNow);

            var service = new ModCalibrationService(new ActionReliabilityService());
            var result = await service.ExportCalibrationArtifactAsync(new ModCalibrationArtifactRequest(
                ProfileId: "custom_test",
                OutputDirectory: outputDir,
                Session: session,
                OperatorNotes: "test export"));

            result.Succeeded.Should().BeTrue();
            File.Exists(result.ArtifactPath).Should().BeTrue();
            result.Candidates.Should().HaveCount(2);

            var json = await File.ReadAllTextAsync(result.ArtifactPath);
            var node = JsonNode.Parse(json)!.AsObject();
            node["schemaVersion"]!.GetValue<string>().Should().Be("1.0");
            node["profileId"]!.GetValue<string>().Should().Be("custom_test");
            node["moduleFingerprint"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
            node["candidates"]!.AsArray().Count.Should().Be(2);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }

    private static TrainerProfile BuildCriticalSymbolProfile()
    {
        return new TrainerProfile(
            Id: "custom_test",
            DisplayName: "Custom Test",
            Inherits: "base_swfoc",
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new(
                    "set_credits",
                    ActionCategory.Economy,
                    RuntimeMode.Unknown,
                    ExecutionKind.Memory,
                    new JsonObject { ["required"] = new JsonArray("symbol", "intValue") },
                    true,
                    100)
            },
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "base_swfoc_steam_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["criticalSymbols"] = "credits"
            });
    }

    private static AttachSession BuildSessionWithUnresolvedCredit(string profileId)
    {
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new(
                Name: "credits",
                Address: 0x1234,
                ValueType: SymbolValueType.Int32,
                Source: AddressSource.Fallback,
                Diagnostics: "fallback",
                Confidence: 0.4,
                HealthStatus: SymbolHealthStatus.Unresolved,
                HealthReason: "signature_missing",
                LastValidatedAt: DateTimeOffset.UtcNow)
        });

        return new AttachSession(
            ProfileId: profileId,
            Process: new ProcessMetadata(
                1234,
                "StarWarsG.exe",
                @"C:\Games\StarWarsG.exe",
                "STEAMMOD=555000111",
                ExeTarget.Swfoc,
                RuntimeMode.Galactic,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dependencyValidation"] = "Pass"
                }),
            Build: new ProfileBuild(profileId, "test", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc, "STEAMMOD=555000111", 1234),
            Symbols: symbols,
            AttachedAt: DateTimeOffset.UtcNow);
    }
}
