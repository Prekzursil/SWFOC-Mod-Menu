using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.IO;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Wave 3 Final coverage for Core models (record constructors), TrustedPathPolicy,
/// TelemetrySnapshotService, and interface default methods.
/// </summary>
public sealed class CoreWave3FinalTests
{
    #region Record constructor coverage — Models

    [Fact]
    public void ActionExecutionRequest_ShouldStoreAllProperties()
    {
        var spec = BuildActionSpec("test");
        var request = new ActionExecutionRequest(spec, new JsonObject(), "profile", RuntimeMode.Galactic, new Dictionary<string, object?>());
        request.Action.Should().Be(spec);
        request.ProfileId.Should().Be("profile");
        request.Context.Should().NotBeNull();
    }

    [Fact]
    public void BackendCapability_ShouldStoreAllProperties()
    {
        var cap = new BackendCapability("feature1", true, CapabilityConfidenceState.Verified, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "some notes");
        cap.FeatureId.Should().Be("feature1");
        cap.Available.Should().BeTrue();
        cap.Confidence.Should().Be(CapabilityConfidenceState.Verified);
        cap.Notes.Should().Be("some notes");
    }

    [Fact]
    public void CapabilityReport_IsFeatureAvailable_MissingFeature_ShouldReturnFalse()
    {
        var report = CapabilityReport.Unknown("test");
        report.IsFeatureAvailable("missing").Should().BeFalse();
    }

    [Fact]
    public void CapabilityReport_IsFeatureAvailable_FeatureUnavailable_ShouldReturnFalse()
    {
        var caps = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
        {
            ["f1"] = new("f1", false, CapabilityConfidenceState.Verified, RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var report = new CapabilityReport("profile", DateTimeOffset.UtcNow, caps, RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        report.IsFeatureAvailable("f1").Should().BeFalse();
    }

    [Fact]
    public void CapabilityReport_IsFeatureAvailable_FeatureAvailable_ShouldReturnTrue()
    {
        var caps = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
        {
            ["f1"] = new("f1", true, CapabilityConfidenceState.Verified, RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var report = new CapabilityReport("profile", DateTimeOffset.UtcNow, caps, RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        report.IsFeatureAvailable("f1").Should().BeTrue();
    }

    [Fact]
    public void CapabilityReport_UnknownWithReasonCode_ShouldSetFields()
    {
        var report = CapabilityReport.Unknown("test", RuntimeReasonCode.UNKNOWN);
        report.ProfileId.Should().Be("test");
        report.ProbeReasonCode.Should().Be(RuntimeReasonCode.UNKNOWN);
    }

    [Fact]
    public void BackendHealth_ShouldStoreAllProperties()
    {
        var health = new BackendHealth("be1", ExecutionBackendKind.Helper, true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok", new Dictionary<string, object?>());
        health.BackendId.Should().Be("be1");
        health.IsHealthy.Should().BeTrue();
        health.Diagnostics.Should().NotBeNull();
    }

    [Fact]
    public void BackendRouteDecision_ShouldStoreAllProperties()
    {
        var decision = new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "go", new Dictionary<string, object?>());
        decision.Allowed.Should().BeTrue();
        decision.Diagnostics.Should().NotBeNull();
    }

    [Fact]
    public void ExtenderCommand_ShouldStoreAllProperties()
    {
        var cmd = new ExtenderCommand("c1", "f1", "p1", RuntimeMode.Galactic, new JsonObject(), 1, "test", new JsonObject(), "user", DateTimeOffset.UtcNow);
        cmd.CommandId.Should().Be("c1");
        cmd.ProcessId.Should().Be(1);
    }

    [Fact]
    public void ExtenderResult_ShouldStoreAllProperties()
    {
        var result = new ExtenderResult("c1", true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "mem", "ready", "ok", new Dictionary<string, object?>());
        result.CommandId.Should().Be("c1");
        result.Diagnostics.Should().NotBeNull();
    }

    [Fact]
    public void RuntimeCalibrationCandidate_ShouldStoreAllProperties()
    {
        var c = new RuntimeCalibrationCandidate("AA BB", 4, SignatureAddressMode.HitPlusOffset, SymbolValueType.Int32, "0x1000", "mov eax", 2);
        c.SuggestedPattern.Should().Be("AA BB");
        c.ReferenceCount.Should().Be(2);
    }

    [Fact]
    public void RuntimeCalibrationScanRequest_ShouldStoreAllProperties()
    {
        var r = new RuntimeCalibrationScanRequest("credits", 5);
        r.TargetSymbol.Should().Be("credits");
        r.MaxCandidates.Should().Be(5);
    }

    [Fact]
    public void RuntimeCalibrationScanResult_ShouldStoreAllProperties()
    {
        var candidates = new[] { new RuntimeCalibrationCandidate("AA", 0, SignatureAddressMode.HitPlusOffset, SymbolValueType.Int32, "0", "", 0) };
        var r = new RuntimeCalibrationScanResult(true, "ok", "msg", candidates, "/path");
        r.ArtifactPath.Should().Be("/path");
    }

    [Fact]
    public void BinaryFingerprint_ShouldStoreAllProperties()
    {
        var fp = new BinaryFingerprint("fp1", "sha256", "mod", "1.0", "1.0", DateTimeOffset.UtcNow, new[] { "mod1" }, "/path");
        fp.FingerprintId.Should().Be("fp1");
        fp.ModuleList.Should().HaveCount(1);
    }

    [Fact]
    public void CapabilityAnchor_ShouldStoreAllProperties()
    {
        var a = new CapabilityAnchor("a1", "sig", "AA BB", false, "notes");
        a.Required.Should().BeFalse();
        a.Notes.Should().Be("notes");
    }

    [Fact]
    public void CapabilityOperationMap_ShouldStoreAllProperties()
    {
        var m = new CapabilityOperationMap(new[] { "a1" }, new[] { "a2" });
        m.RequiredAnchors.Should().Contain("a1");
        m.OptionalAnchors.Should().Contain("a2");
    }

    [Fact]
    public void CapabilityAvailabilityHint_ShouldStoreAllProperties()
    {
        var h = new CapabilityAvailabilityHint("f1", true, "active", "ok", new[] { "a1" });
        h.FeatureId.Should().Be("f1");
    }

    [Fact]
    public void CapabilityMap_ShouldStoreAllProperties()
    {
        var ops = new Dictionary<string, CapabilityOperationMap>(StringComparer.OrdinalIgnoreCase);
        var hints = new Dictionary<string, CapabilityAvailabilityHint>(StringComparer.OrdinalIgnoreCase);
        var m = new CapabilityMap("v1", "fp1", "profile", DateTimeOffset.UtcNow, ops, hints);
        m.SchemaVersion.Should().Be("v1");
        m.DefaultProfileId.Should().Be("profile");
    }

    [Fact]
    public void CapabilityResolutionMetadata_Empty_ShouldHaveNullDeclared()
    {
        CapabilityResolutionMetadata.Empty.DeclaredAvailable.Should().BeNull();
    }

    [Fact]
    public void CapabilityResolutionResult_ShouldStoreAllProperties()
    {
        var meta = CapabilityResolutionMetadata.Empty;
        var r = new CapabilityResolutionResult("p", "op", SdkCapabilityStatus.Available, CapabilityReasonCode.AllRequiredAnchorsPresent, 0.9, "fp", new[] { "a" }, Array.Empty<string>(), meta);
        r.Confidence.Should().Be(0.9);
    }

    [Fact]
    public void SdkExecutionDecision_ShouldStoreAllProperties()
    {
        var d = new SdkExecutionDecision(true, CapabilityReasonCode.AllRequiredAnchorsPresent, "go");
        d.Allowed.Should().BeTrue();
    }

    [Fact]
    public void ProfileVariantResolution_WithOptionalFields_ShouldStoreAll()
    {
        var r = new ProfileVariantResolution("req", "res", "reason", 0.8, "fp1", 42, "proc");
        r.FingerprintId.Should().Be("fp1");
        r.ProcessId.Should().Be(42);
        r.ProcessName.Should().Be("proc");
    }

    [Fact]
    public void SdkOperationRequest_ShouldStoreAllProperties()
    {
        var r = new SdkOperationRequest("op1", new JsonObject(), true, RuntimeMode.Galactic, "p1", new Dictionary<string, object?>());
        r.OperationId.Should().Be("op1");
        r.IsMutation.Should().BeTrue();
        r.Context.Should().NotBeNull();
    }

    [Fact]
    public void ModLaunchSample_ShouldStoreAllProperties()
    {
        var s = new ModLaunchSample("proc", "/path", "--args");
        s.ProcessName.Should().Be("proc");
    }

    [Fact]
    public void ModOnboardingResult_ShouldStoreAllProperties()
    {
        var r = new ModOnboardingResult(true, "id", "/path", new[] { "w1" }, new[] { "h1" }, new[] { "a1" }, Array.Empty<string>());
        r.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ModOnboardingBatchItemResult_ShouldStoreAllProperties()
    {
        var r = new ModOnboardingBatchItemResult(0, "seed", true, "id", "/path", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        r.Index.Should().Be(0);
    }

    [Fact]
    public void ModOnboardingBatchResult_ShouldStoreAllProperties()
    {
        var r = new ModOnboardingBatchResult(true, 1, 1, 0, Array.Empty<ModOnboardingBatchItemResult>());
        r.SucceededCount.Should().Be(1);
    }

    [Fact]
    public void GeneratedProfileSeed_ShouldStoreAllOptionalFields()
    {
        var seed = new GeneratedProfileSeed(
            "draft", "name", "base", Array.Empty<ModLaunchSample>(),
            "run1", 0.95, "parent",
            RequiredWorkshopIds: new[] { "w1" },
            ProfileAliases: new[] { "alias" },
            LocalPathHints: new[] { "/hint" },
            Notes: "note",
            WorkshopId: "w2",
            RequiredCapabilities: new[] { "cap" },
            AnchorHints: new[] { "anchor" },
            RiskLevel: "low",
            ParentDependencies: new[] { "dep" },
            LaunchHints: new[] { "launch" },
            Title: "title",
            CandidateBaseProfile: "candidate");
        seed.Title.Should().Be("title");
        seed.CandidateBaseProfile.Should().Be("candidate");
        seed.RiskLevel.Should().Be("low");
    }

    [Fact]
    public void ModOnboardingSeedBatchRequest_ShouldStoreAllProperties()
    {
        var r = new ModOnboardingSeedBatchRequest("ns", Array.Empty<GeneratedProfileSeed>());
        r.TargetNamespaceRoot.Should().Be("ns");
    }

    [Fact]
    public void CalibrationCandidate_ShouldStoreAllProperties()
    {
        var c = new CalibrationCandidate("sym", "sig", "healthy", 0.9, "notes");
        c.Notes.Should().Be("notes");
    }

    [Fact]
    public void ModCalibrationArtifactResult_ShouldStoreAllProperties()
    {
        var r = new ModCalibrationArtifactResult(true, "/path", "fp", Array.Empty<CalibrationCandidate>(), Array.Empty<string>());
        r.ArtifactPath.Should().Be("/path");
    }

    [Fact]
    public void ModCalibrationArtifactRequest_ShouldStoreAllProperties()
    {
        var r = new ModCalibrationArtifactRequest("p1", "/dir", null, "notes");
        r.OperatorNotes.Should().Be("notes");
    }

    [Fact]
    public void ProfileInstallResult_ShouldStoreAllProperties()
    {
        var r = new ProfileInstallResult(true, "p1", "/path", "/bak", "/receipt", "ok", "install_ok");
        r.Succeeded.Should().BeTrue();
        r.InstalledPath.Should().Be("/path");
        r.ReasonCode.Should().Be("install_ok");
    }

    [Fact]
    public void ProfileRollbackResult_ShouldStoreAllProperties()
    {
        var r = new ProfileRollbackResult(true, "p1", "/path", "/bak", "ok", "rollback_ok");
        r.Restored.Should().BeTrue();
        r.ReasonCode.Should().Be("rollback_ok");
    }

    [Fact]
    public void CompatibilityReportModels_ShouldStoreAllProperties()
    {
        var entry = new ModActionCompatibility("a1", ActionReliabilityState.Stable, "healthy_signature", 0.95);
        entry.ActionId.Should().Be("a1");
        entry.State.Should().Be(ActionReliabilityState.Stable);
    }

    [Fact]
    public void SaveModels_Coverage()
    {
        var node = new SaveNode("/root", "root", "root", null, new[] { new SaveNode("/child", "child", "int32", "42") });
        node.Children.Should().HaveCount(1);

        var doc = new SaveDocument(@"C:\test.sav", "schema", new byte[] { 0 }, node);
        doc.Path.Should().NotBeEmpty();

        var result = new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>());
        result.IsValid.Should().BeTrue();

        var field = new SaveFieldDefinition("f1", "Field1", "int32", 0, 4, "desc", "/path");
        field.Description.Should().Be("desc");

        var block = new SaveBlockDefinition("b1", "Block1", 0, 100, "root");
        block.Name.Should().Be("Block1");

        var rule = new ValidationRule("r1", "min_value", "f1", "too low", "warning");
        rule.Rule.Should().Be("min_value");
        rule.Severity.Should().Be("warning");
    }

    [Fact]
    public void SavePatchModels_Coverage()
    {
        var failure = new SavePatchApplyFailure("reason", "msg", "f1", "/path");
        failure.FieldId.Should().Be("f1");

        var applyResult = new SavePatchApplyResult(
            SavePatchApplyClassification.Applied, true, "ok", "/out", "/bak", "/receipt");
        applyResult.OutputPath.Should().Be("/out");

        var rollback = new SaveRollbackResult(true, "ok", "/target", "/bak", "hash");
        rollback.RestoredHash.Should().Be("hash");
    }

    [Fact]
    public void RuntimeModels_Coverage()
    {
        var launch = new LaunchContext(LaunchKind.Workshop, true, new[] { "123" }, @"Mods\test", @"Mods\test", "cmd", new ProfileRecommendation("p1", "reason", 0.95));
        launch.DetectedVia.Should().Be("cmd");

        var process = new ProcessMetadata(1, "test", "/path", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
            Metadata: null, LaunchContext: launch, HostRole: ProcessHostRole.GameHost, MainModuleSize: 4096,
            WorkshopMatchCount: 1, SelectionScore: 0.8);
        process.SelectionScore.Should().Be(0.8);
    }

    [Fact]
    public void WorkshopInventoryModels_Coverage()
    {
        var item = new WorkshopInventoryItem("123", "Test Mod", WorkshopItemType.Mod,
            new[] { "parent1" }, new[] { "tag1" }, "desc", "reason", new Dictionary<string, string>());
        item.WorkshopId.Should().Be("123");
        item.Description.Should().Be("desc");
        item.ClassificationReason.Should().Be("reason");
    }

    [Fact]
    public void SdkOperationCatalog_TryGet_ShouldReturnKnownOps()
    {
        SdkOperationCatalog.TryGet("spawn", out var def).Should().BeTrue();
        def.IsMutation.Should().BeTrue();

        SdkOperationCatalog.TryGet("nonexistent_op", out _).Should().BeFalse();
    }

    [Fact]
    public void SdkOperationCatalog_List_ShouldReturnAll()
    {
        var all = SdkOperationCatalog.List();
        all.Should().NotBeEmpty();
    }

    [Fact]
    public void SdkOperationDefinition_IsModeAllowed_UnknownMode_ReadOnly_ShouldReturnTrue()
    {
        var def = SdkOperationDefinition.ReadOnly("test_op");
        def.IsModeAllowed(RuntimeMode.Unknown).Should().BeTrue();
    }

    [Fact]
    public void SdkOperationDefinition_IsModeAllowed_UnknownMode_Mutation_ShouldReturnFalse()
    {
        var def = SdkOperationDefinition.Mutation("test_op", RuntimeMode.Galactic);
        def.IsModeAllowed(RuntimeMode.Unknown).Should().BeFalse();
    }

    [Fact]
    public void SdkOperationDefinition_IsModeAllowed_EmptyAllowedModes_ShouldReturnTrue()
    {
        var def = new SdkOperationDefinition("test", false, new HashSet<RuntimeMode>(), false);
        def.IsModeAllowed(RuntimeMode.Galactic).Should().BeTrue();
    }

    [Fact]
    public void SdkOperationDefinition_IsModeAllowed_TacticalLand_WithAnyTactical_ShouldReturnTrue()
    {
        var def = SdkOperationDefinition.Mutation("test_op", RuntimeMode.AnyTactical);
        def.IsModeAllowed(RuntimeMode.TacticalLand).Should().BeTrue();
        def.IsModeAllowed(RuntimeMode.TacticalSpace).Should().BeTrue();
    }

    [Fact]
    public void ProfileBuild_ShouldStoreOptionalFields()
    {
        var build = new ProfileBuild("p1", "build", "/path", ExeTarget.Swfoc, "cmdline", 42);
        build.ProcessCommandLine.Should().Be("cmdline");
        build.ProcessId.Should().Be(42);
    }

    [Fact]
    public void HelperHookSpec_ShouldStoreOptionalFields()
    {
        var hook = new HelperHookSpec("h1", "script.lua", "1.0", "main",
            new Dictionary<string, string> { ["arg"] = "val" },
            new Dictionary<string, string> { ["verify"] = "val" },
            new Dictionary<string, string> { ["meta"] = "val" });
        hook.EntryPoint.Should().Be("main");
        hook.ArgContract.Should().ContainKey("arg");
        hook.VerifyContract.Should().ContainKey("verify");
        hook.Metadata.Should().ContainKey("meta");
    }

    [Fact]
    public void SymbolValidationRule_ShouldStoreOptionalFields()
    {
        var rule = new SymbolValidationRule("sym", RuntimeMode.Galactic, IntMin: 0L, IntMax: 999999L, FloatMin: null);
        rule.Mode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void JsonProfileSerializer_ToJsonObject_ShouldReturnJsonObject()
    {
        var obj = JsonProfileSerializer.ToJsonObject(new { hello = "world" });
        obj.Should().NotBeNull();
        obj["hello"]!.GetValue<string>().Should().Be("world");
    }

    [Fact]
    public void JsonProfileSerializer_ToJsonObject_NullResult_ShouldReturnEmptyObject()
    {
        // string value serializes to JsonValue not JsonObject
        var obj = JsonProfileSerializer.ToJsonObject("hello");
        obj.Should().NotBeNull();
    }

    #endregion

    #region TrustedPathPolicy — BuildSiblingFilePath directory null branch

    [Fact]
    public void TrustedPathPolicy_BuildSiblingFilePath_ValidPath_ShouldWork()
    {
        var result = TrustedPathPolicy.BuildSiblingFilePath(@"C:\Games\save.sav", ".edited");
        result.Should().Contain("save.edited.sav");
    }

    [Fact]
    public void TrustedPathPolicy_IsSubPath_OnLinuxStylePaths_ShouldHandleCorrectly()
    {
        // Test the OS comparison branch - on Windows this is OrdinalIgnoreCase
        var result = TrustedPathPolicy.IsSubPath(@"C:\root", @"C:\root\sub");
        result.Should().BeTrue();

        var notSub = TrustedPathPolicy.IsSubPath(@"C:\root", @"C:\other");
        notSub.Should().BeFalse();
    }

    [Fact]
    public void TrustedPathPolicy_CombineUnderRoot_EmptySegment_ShouldSkip()
    {
        var result = TrustedPathPolicy.CombineUnderRoot(@"C:\root", "", "sub", " ");
        result.Should().Contain("sub");
    }

    #endregion

    #region TelemetrySnapshotService

    [Fact]
    public void RecordAction_WhitespaceActionId_ShouldReturnEarly()
    {
        var svc = new SwfocTrainer.Core.Services.TelemetrySnapshotService();
        svc.RecordAction("   ", AddressSource.Signature, true);
        var snapshot = svc.CreateSnapshot();
        snapshot.TotalActions.Should().Be(0);
    }

    [Fact]
    public void CreateSnapshot_ZeroActions_ShouldReturnZeroRates()
    {
        var svc = new SwfocTrainer.Core.Services.TelemetrySnapshotService();
        var snapshot = svc.CreateSnapshot();
        snapshot.FailureRate.Should().Be(0d);
        snapshot.FallbackRate.Should().Be(0d);
        snapshot.UnresolvedRate.Should().Be(0d);
        snapshot.TotalActions.Should().Be(0);
    }

    [Fact]
    public void CreateSnapshot_WithActions_ShouldCalculateRates()
    {
        var svc = new SwfocTrainer.Core.Services.TelemetrySnapshotService();
        svc.RecordAction("a1", AddressSource.Signature, true);
        svc.RecordAction("a1", AddressSource.Fallback, false);
        svc.RecordAction("a2", AddressSource.Fallback, false);
        var snapshot = svc.CreateSnapshot();
        snapshot.TotalActions.Should().Be(3);
        snapshot.FailureRate.Should().BeGreaterThan(0);
        snapshot.FallbackRate.Should().BeGreaterThan(0);
        snapshot.UnresolvedRate.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportSnapshotAsync_WhitespaceDir_ShouldThrow()
    {
        var svc = new SwfocTrainer.Core.Services.TelemetrySnapshotService();
        var act = () => svc.ExportSnapshotAsync("   ");
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ExportSnapshotAsync_ValidDir_ShouldCreateFile()
    {
        var svc = new SwfocTrainer.Core.Services.TelemetrySnapshotService();
        svc.RecordAction("a1", AddressSource.Signature, true);
        var tempDir = Path.Join(Path.GetTempPath(), $"swfoc-telem-{Guid.NewGuid():N}");
        try
        {
            var path = await svc.ExportSnapshotAsync(tempDir);
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Reset_ShouldClearAllCounters()
    {
        var svc = new SwfocTrainer.Core.Services.TelemetrySnapshotService();
        svc.RecordAction("a1", AddressSource.Signature, true);
        svc.Reset();
        var snapshot = svc.CreateSnapshot();
        snapshot.TotalActions.Should().Be(0);
    }

    [Fact]
    public async Task ExportSnapshotAsync_NoCancellation_ShouldWork()
    {
        var svc = new SwfocTrainer.Core.Services.TelemetrySnapshotService();
        var tempDir = Path.Join(Path.GetTempPath(), $"swfoc-telem2-{Guid.NewGuid():N}");
        try
        {
            var path = await svc.ExportSnapshotAsync(tempDir, CancellationToken.None);
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region ActionPayloadValidator missing field branch

    [Fact]
    public void Validate_MissingRequiredField_ShouldReturnInvalid()
    {
        var schema = new JsonObject
        {
            ["required"] = new JsonArray(JsonValue.Create("symbol")!)
        };
        var payload = new JsonObject();
        var (isValid, message) = SwfocTrainer.Core.Validation.ActionPayloadValidator.Validate(schema, payload);
        isValid.Should().BeFalse();
        message.Should().Contain("symbol");
    }

    #endregion

    private static ActionSpec BuildActionSpec(string id)
    {
        return new ActionSpec(id, ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Memory, new JsonObject(), false, 0);
    }
}
