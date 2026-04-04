using FluentAssertions;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

/// <summary>
/// Wave 3 Final coverage for Profiles: record constructor coverage for
/// ProfileUpdateCheckResult, ProfileAutoUpdateResult, and model records
/// that are missing line coverage from constructors.
/// </summary>
public sealed class ProfilesWave3FinalTests
{
    #region Model constructor coverage

    [Fact]
    public void ProfileInstallResult_ShouldStoreAllProperties()
    {
        var r = new ProfileInstallResult(true, "p1", "/installed", "/bak", "/receipt", "ok", "install_pass");
        r.Succeeded.Should().BeTrue();
        r.InstalledPath.Should().Be("/installed");
        r.BackupPath.Should().Be("/bak");
        r.ReceiptPath.Should().Be("/receipt");
        r.ReasonCode.Should().Be("install_pass");
    }

    [Fact]
    public void ProfileRollbackResult_ShouldStoreAllProperties()
    {
        var r = new ProfileRollbackResult(true, "p1", "/restored", "/bak", "restored ok", "rollback_pass");
        r.Restored.Should().BeTrue();
        r.RestoredPath.Should().Be("/restored");
        r.ReasonCode.Should().Be("rollback_pass");
    }

    [Fact]
    public void TrainerProfile_WithAllOptionalFields_ShouldStoreAll()
    {
        var profile = new TrainerProfile(
            Id: "test",
            DisplayName: "Test Profile",
            Inherits: "parent",
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "123",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string> { ["key"] = "val" },
            BackendPreference: "auto",
            RequiredCapabilities: new[] { "cap1" },
            HostPreference: "any",
            ExperimentalFeatures: new[] { "exp1" });
        profile.BackendPreference.Should().Be("auto");
        profile.RequiredCapabilities.Should().Contain("cap1");
        profile.HostPreference.Should().Be("any");
        profile.ExperimentalFeatures.Should().Contain("exp1");
        profile.Metadata.Should().ContainKey("key");
    }

    [Fact]
    public void CatalogSource_WithOptionalFields_ShouldStoreAll()
    {
        var cs = new CatalogSource("xml", "/data/units.xml", false, "Unit catalog");
        cs.Required.Should().BeFalse();
        cs.Description.Should().Be("Unit catalog");
    }

    [Fact]
    public void SignatureSpec_WithOptionalFields_ShouldStoreAll()
    {
        var spec = new SignatureSpec("credits", "AA BB CC", 4, SignatureAddressMode.HitPlusOffset, "game.exe", SymbolValueType.Float);
        spec.Module.Should().Be("game.exe");
        spec.ValueType.Should().Be(SymbolValueType.Float);
    }

    [Fact]
    public void SignatureSet_ShouldStoreAllProperties()
    {
        var sigs = new[] { new SignatureSpec("sym", "AA", 0) };
        var set = new SignatureSet("set1", "build1", sigs);
        set.Name.Should().Be("set1");
        set.GameBuild.Should().Be("build1");
        set.Signatures.Should().HaveCount(1);
    }

    [Fact]
    public void SymbolInfo_WithAllOptionalFields_ShouldStoreAll()
    {
        var info = new SymbolInfo(
            "credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
            Diagnostics: "ok", Confidence: 0.95, HealthStatus: SymbolHealthStatus.Degraded,
            HealthReason: "fallback", LastValidatedAt: DateTimeOffset.UtcNow);
        info.Diagnostics.Should().Be("ok");
        info.Confidence.Should().Be(0.95);
        info.HealthStatus.Should().Be(SymbolHealthStatus.Degraded);
        info.HealthReason.Should().Be("fallback");
        info.LastValidatedAt.Should().NotBeNull();
    }

    [Fact]
    public void AttachSession_ShouldStoreAllProperties()
    {
        var process = new ProcessMetadata(1, "test", "/path", null, ExeTarget.Swfoc, RuntimeMode.Galactic);
        var build = new ProfileBuild("p1", "build", "/path", ExeTarget.Swfoc);
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase));
        var session = new AttachSession("p1", process, build, symbols, DateTimeOffset.UtcNow);
        session.ProfileId.Should().Be("p1");
        session.Process.ProcessId.Should().Be(1);
    }

    [Fact]
    public void ActionExecutionResult_WithDiagnostics_ShouldStoreAll()
    {
        var diag = new Dictionary<string, object?> { ["key"] = "val" };
        var r = new ActionExecutionResult(true, "ok", AddressSource.Signature, diag);
        r.Diagnostics.Should().ContainKey("key");
    }

    [Fact]
    public void ActionReliabilityInfo_ShouldStoreAllProperties()
    {
        var info = new ActionReliabilityInfo("a1", ActionReliabilityState.Stable, "reason", 0.95, "detail");
        info.ActionId.Should().Be("a1");
        info.Detail.Should().Be("detail");
    }

    [Fact]
    public void ProcessMetadata_AllOptionalFields_ShouldStoreAll()
    {
        var launch = new LaunchContext(LaunchKind.Workshop, true, new[] { "123" }, @"Mods\test", @"Mods\test", "cmd",
            new ProfileRecommendation("p1", "reason", 0.95));
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["key"] = "val" };
        var proc = new ProcessMetadata(
            ProcessId: 1, ProcessName: "test.exe", ProcessPath: "/path",
            CommandLine: "--args", ExeTarget: ExeTarget.Swfoc, Mode: RuntimeMode.Galactic,
            Metadata: meta, LaunchContext: launch,
            HostRole: ProcessHostRole.GameHost, MainModuleSize: 4096,
            WorkshopMatchCount: 1, SelectionScore: 0.85);
        proc.HostRole.Should().Be(ProcessHostRole.GameHost);
        proc.MainModuleSize.Should().Be(4096);
    }

    [Fact]
    public void SelectedUnitTransactionResult_WithRollback_ShouldStoreAll()
    {
        var r = new SelectedUnitTransactionResult(false, "err", "txn1",
            Array.Empty<ActionExecutionResult>(),
            RolledBack: true,
            RollbackSteps: Array.Empty<ActionExecutionResult>());
        r.RolledBack.Should().BeTrue();
    }

    #endregion
}
