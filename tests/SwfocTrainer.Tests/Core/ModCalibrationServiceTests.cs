using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class ModCalibrationServiceTests
{
    [Fact]
    public async Task ExportCalibrationArtifactAsync_ShouldWriteArtifactFile()
    {
        using var temp = new TempDirectory();
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var session = BuildAttachedSession();

        var result = await service.ExportCalibrationArtifactAsync(
            new ModCalibrationArtifactRequest("test_profile", temp.Path, session, "test notes"));

        result.Succeeded.Should().BeTrue();
        File.Exists(result.ArtifactPath).Should().BeTrue();
        result.Candidates.Should().NotBeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_ShouldThrow_WhenProfileIdIsBlank()
    {
        using var temp = new TempDirectory();
        var service = new ModCalibrationService(new StubActionReliabilityService());

        var act = async () => await service.ExportCalibrationArtifactAsync(
            new ModCalibrationArtifactRequest("", temp.Path, null));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*ProfileId*");
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_ShouldThrow_WhenOutputDirectoryIsBlank()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());

        var act = async () => await service.ExportCalibrationArtifactAsync(
            new ModCalibrationArtifactRequest("test", "  ", null));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*OutputDirectory*");
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_ShouldThrow_WhenRequestIsNull()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());

        var act1 = async () => await service.ExportCalibrationArtifactAsync(null!);
        var act2 = async () => await service.ExportCalibrationArtifactAsync(null!, CancellationToken.None);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_WithNullSession_ShouldWarnAndUseSessionUnavailable()
    {
        using var temp = new TempDirectory();
        var service = new ModCalibrationService(new StubActionReliabilityService());

        var result = await service.ExportCalibrationArtifactAsync(
            new ModCalibrationArtifactRequest("test_profile", temp.Path, null));

        result.Succeeded.Should().BeTrue();
        result.ModuleFingerprint.Should().Be("session_unavailable");
        result.Warnings.Should().Contain(x => x.Contains("No attach session"));
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportCalibrationArtifactAsync_WithEmptySymbols_ShouldWarn()
    {
        using var temp = new TempDirectory();
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var session = new AttachSession(
            "test",
            new ProcessMetadata(123, "swfoc", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic),
            new ProfileBuild("test", "1.0", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            DateTimeOffset.UtcNow);

        var result = await service.ExportCalibrationArtifactAsync(
            new ModCalibrationArtifactRequest("test_profile", temp.Path, session));

        result.Succeeded.Should().BeTrue();
        result.Warnings.Should().Contain(x => x.Contains("does not contain resolved symbols"));
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_ShouldReturnReport_WithSession()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");
        var session = BuildAttachedSession();

        var report = await service.BuildCompatibilityReportAsync(profile, session);

        report.ProfileId.Should().Be("test");
        report.RuntimeMode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_ShouldReturnReport_WithoutSession()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");

        var report = await service.BuildCompatibilityReportAsync(profile, null);

        report.ProfileId.Should().Be("test");
        report.RuntimeMode.Should().Be(RuntimeMode.Unknown);
        report.Notes.Should().Contain(x => x.Contains("No attach session"));
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_ShouldThrow_WhenProfileIsNull()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());

        var act = async () => await service.BuildCompatibilityReportAsync(null!, null);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_WithDependencyHardFail_ShouldBlockPromotion()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");
        var session = BuildAttachedSession();
        var depResult = new DependencyValidationResult(DependencyValidationStatus.HardFail, "hard fail", new HashSet<string>());

        var report = await service.BuildCompatibilityReportAsync(profile, session, depResult, null, CancellationToken.None);

        report.PromotionReady.Should().BeFalse();
        report.Notes.Should().Contain(x => x.Contains("HardFail"));
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_WithDependencySoftFail_ShouldNote()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");
        var session = BuildAttachedSession();
        var depResult = new DependencyValidationResult(DependencyValidationStatus.SoftFail, "soft fail", new HashSet<string>());

        var report = await service.BuildCompatibilityReportAsync(profile, session, depResult, null, CancellationToken.None);

        report.Notes.Should().Contain(x => x.Contains("SoftFail"));
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_WithUnresolvedCriticalSymbols_ShouldBlockPromotion()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test", criticalSymbols: "credits");
        var session = BuildSessionWithUnresolvedSymbol();

        var report = await service.BuildCompatibilityReportAsync(profile, session);

        report.PromotionReady.Should().BeFalse();
        report.UnresolvedCriticalSymbols.Should().BeGreaterThan(0);
        report.Notes.Should().Contain(x => x.Contains("critical symbol"));
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_InfersDependencyStatus_FromMetadata()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");
        var session = new AttachSession(
            "test",
            new ProcessMetadata(123, "swfoc", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dependencyValidation"] = "SoftFail" }),
            new ProfileBuild("test", "1.0", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            DateTimeOffset.UtcNow);

        var report = await service.BuildCompatibilityReportAsync(profile, session, null, null, CancellationToken.None);

        report.DependencyStatus.Should().Be(DependencyValidationStatus.SoftFail);
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_InfersDependencyStatusAsPass_WhenMetadataIsMissing()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");
        var session = new AttachSession(
            "test",
            new ProcessMetadata(123, "swfoc", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic),
            new ProfileBuild("test", "1.0", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            DateTimeOffset.UtcNow);

        var report = await service.BuildCompatibilityReportAsync(profile, session, null, null, CancellationToken.None);

        report.DependencyStatus.Should().Be(DependencyValidationStatus.Pass);
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_InfersDependencyStatusAsPass_WhenMetadataValueIsInvalid()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");
        var session = new AttachSession(
            "test",
            new ProcessMetadata(123, "swfoc", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dependencyValidation"] = "InvalidValue" }),
            new ProfileBuild("test", "1.0", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            DateTimeOffset.UtcNow);

        var report = await service.BuildCompatibilityReportAsync(profile, session, null, null, CancellationToken.None);

        report.DependencyStatus.Should().Be(DependencyValidationStatus.Pass);
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_FourParamOverload_ShouldWork()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test");

        var report = await service.BuildCompatibilityReportAsync(profile, null, null, null);

        report.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_WithNullCriticalSymbolsInSession_CountsAllAsCritical()
    {
        var service = new ModCalibrationService(new StubActionReliabilityService());
        var profile = BuildProfile("test", criticalSymbols: "credits,game_speed");

        var report = await service.BuildCompatibilityReportAsync(profile, null);

        report.UnresolvedCriticalSymbols.Should().Be(2);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDependencyIsNull()
    {
        var act = () => new ModCalibrationService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static AttachSession BuildAttachedSession()
    {
        return new AttachSession(
            "test",
            new ProcessMetadata(123, "swfoc", @"C:\Games\swfoc.exe", "args", ExeTarget.Swfoc, RuntimeMode.Galactic,
                null,
                new LaunchContext(LaunchKind.BaseGame, true, Array.Empty<string>(), null, null, "detected",
                    new ProfileRecommendation("test", "ok", 0.9d))),
            new ProfileBuild("test", "1.0", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits"] = new("credits", (nint)0x1234, SymbolValueType.Int32, AddressSource.Signature,
                    "healthy", 0.95d, SymbolHealthStatus.Healthy)
            }),
            DateTimeOffset.UtcNow);
    }

    private static AttachSession BuildSessionWithUnresolvedSymbol()
    {
        return new AttachSession(
            "test",
            new ProcessMetadata(123, "swfoc", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic),
            new ProfileBuild("test", "1.0", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits"] = new("credits", nint.Zero, SymbolValueType.Int32, AddressSource.None,
                    null, 0d, SymbolHealthStatus.Unresolved)
            }),
            DateTimeOffset.UtcNow);
    }

    private static TrainerProfile BuildProfile(string profileId, string? criticalSymbols = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (criticalSymbols is not null)
        {
            metadata["criticalSymbols"] = criticalSymbols;
        }

        return new TrainerProfile(
            profileId,
            "test",
            null,
            ExeTarget.Swfoc,
            null,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new ActionSpec(
                    "set_credits",
                    ActionCategory.Economy,
                    RuntimeMode.Unknown,
                    ExecutionKind.Memory,
                    new JsonObject { ["required"] = new JsonArray(JsonValue.Create("symbol")!, JsonValue.Create("intValue")!) },
                    VerifyReadback: false,
                    CooldownMs: 0)
            },
            new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(),
            "test",
            Array.Empty<HelperHookSpec>(),
            metadata);
    }

    private sealed class StubActionReliabilityService : IActionReliabilityService
    {
        public IReadOnlyList<ActionReliabilityInfo> Evaluate(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
        {
            return profile.Actions.Keys
                .Select(id => new ActionReliabilityInfo(id, ActionReliabilityState.Stable, "stub_ok", 0.9d))
                .ToArray();
        }

        public IReadOnlyList<ActionReliabilityInfo> Evaluate(TrainerProfile profile, AttachSession session)
        {
            return Evaluate(profile, session, null);
        }
    }
}
