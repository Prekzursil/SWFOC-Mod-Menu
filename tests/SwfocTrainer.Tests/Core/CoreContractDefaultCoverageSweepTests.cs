using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CoreContractDefaultCoverageSweepTests
{
    [Fact]
    public async Task CoreContractDefaultOverloads_ShouldForwardCancellationTokenNone()
    {
        var profile = BuildProfile();
        var session = BuildSession(profile.Id);
        var saveDoc = BuildSaveDocument();
        var patch = BuildPatch();

        IActionReliabilityService actionReliability = new ActionReliabilityStub();
        IAuditLogger auditLogger = new AuditLoggerStub();
        IHelperModService helperMod = new HelperModStub();
        IModCalibrationService calibration = new ModCalibrationStub();
        IModOnboardingService onboarding = new ModOnboardingStub();
        IProcessLocator processLocator = new ProcessLocatorStub(session.Process);
        IProfileUpdateService profileUpdate = new ProfileUpdateStub();
        ISaveCodec saveCodec = new SaveCodecStub(saveDoc);
        ISavePatchApplyService patchApply = new SavePatchApplyStub();
        ISavePatchPackService patchPack = new SavePatchPackStub(patch);
        ISignatureResolver signatureResolver = new SignatureResolverStub();
        ISupportBundleService supportBundle = new SupportBundleStub();
        ITelemetrySnapshotService telemetry = new TelemetrySnapshotStub();

        var reliability = actionReliability.Evaluate(profile, session);
        reliability.Should().ContainSingle();
        ((ActionReliabilityStub)actionReliability).LastCatalog.Should().BeNull();

        var auditRecord = new ActionAuditRecord(DateTimeOffset.UtcNow, profile.Id, 1, "set_credits", AddressSource.Signature, true, "ok");
        await auditLogger.WriteAsync(auditRecord);
        ((AuditLoggerStub)auditLogger).Cancellation.Should().Be(CancellationToken.None);

        await helperMod.DeployAsync(profile.Id);
        await helperMod.VerifyAsync(profile.Id);
        ((HelperModStub)helperMod).DeployCancellation.Should().Be(CancellationToken.None);
        ((HelperModStub)helperMod).VerifyCancellation.Should().Be(CancellationToken.None);

        var calibrationRequest = new ModCalibrationArtifactRequest(profile.Id, Path.GetTempPath(), session);
        await calibration.ExportCalibrationArtifactAsync(calibrationRequest);
        await calibration.BuildCompatibilityReportAsync(profile, session);
        await calibration.BuildCompatibilityReportAsync(profile, session, null, null);
        ((ModCalibrationStub)calibration).ExportCancellation.Should().Be(CancellationToken.None);
        ((ModCalibrationStub)calibration).ReportCancellation.Should().Be(CancellationToken.None);

        var onboardingRequest = new ModOnboardingRequest("draft", "Draft", "base_swfoc", Array.Empty<ModLaunchSample>());
        await onboarding.ScaffoldDraftProfileAsync(onboardingRequest);
        await onboarding.ScaffoldDraftProfilesFromSeedsAsync(new ModOnboardingSeedBatchRequest(null, Array.Empty<GeneratedProfileSeed>()));
        await onboarding.ScaffoldDraftProfileAsync(onboardingRequest);
        await ModOnboardingServiceExtensions.ScaffoldDraftProfileAsync(onboarding, onboardingRequest);
        await ModOnboardingServiceExtensions.ScaffoldDraftProfilesFromSeedsAsync(onboarding, new ModOnboardingSeedBatchRequest(null, Array.Empty<GeneratedProfileSeed>()));
        ((ModOnboardingStub)onboarding).ProfileCancellation.Should().Be(CancellationToken.None);
        ((ModOnboardingStub)onboarding).BatchCancellation.Should().Be(CancellationToken.None);

        var options = new ProcessLocatorOptions(new[] { "1397421866" }, profile.Id);
        await processLocator.FindSupportedProcessesAsync();
        await processLocator.FindSupportedProcessesAsync(options);
        await processLocator.FindBestMatchAsync(ExeTarget.Swfoc);
        await processLocator.FindBestMatchAsync(ExeTarget.Swfoc, options);
        ((ProcessLocatorStub)processLocator).FindCancellation.Should().Be(CancellationToken.None);
        ((ProcessLocatorStub)processLocator).BestCancellation.Should().Be(CancellationToken.None);

        await profileUpdate.CheckForUpdatesAsync();
        await profileUpdate.InstallProfileAsync(profile.Id);
        await profileUpdate.InstallProfileTransactionalAsync(profile.Id);
        await profileUpdate.RollbackLastInstallAsync(profile.Id);
        ((ProfileUpdateStub)profileUpdate).Cancellation.Should().Be(CancellationToken.None);

        await saveCodec.LoadAsync("test.sav", "schema");
        await saveCodec.EditAsync(saveDoc, "/economy/credits", 7);
        await saveCodec.ValidateAsync(saveDoc);
        await saveCodec.WriteAsync(saveDoc, "out.sav");
        await saveCodec.RoundTripCheckAsync(saveDoc);
        ((SaveCodecStub)saveCodec).Cancellation.Should().Be(CancellationToken.None);

        await patchApply.ApplyAsync("target.sav", patch, profile.Id);
        await patchApply.ApplyAsync("target.sav", patch, profile.Id, strict: false);
        await patchApply.RestoreLastBackupAsync("target.sav");
        ((SavePatchApplyStub)patchApply).Cancellation.Should().Be(CancellationToken.None);

        await patchPack.ExportAsync(saveDoc, saveDoc, profile.Id);
        await patchPack.LoadPackAsync("pack.json");
        await patchPack.ValidateCompatibilityAsync(patch, saveDoc, profile.Id);
        await patchPack.PreviewApplyAsync(patch, saveDoc, profile.Id);
        ((SavePatchPackStub)patchPack).Cancellation.Should().Be(CancellationToken.None);

        await signatureResolver.ResolveAsync(profileBuild: session.Build, signatureSets: Array.Empty<SignatureSet>(), fallbackOffsets: new Dictionary<string, long>());
        ((SignatureResolverStub)signatureResolver).Cancellation.Should().Be(CancellationToken.None);

        await supportBundle.ExportAsync(new SupportBundleRequest(Path.GetTempPath(), profile.Id));
        ((SupportBundleStub)supportBundle).Cancellation.Should().Be(CancellationToken.None);

        telemetry.RecordAction("set_credits", AddressSource.Signature, true);
        telemetry.CreateSnapshot().TotalActions.Should().Be(1);
        await telemetry.ExportSnapshotAsync(Path.GetTempPath());
        telemetry.Reset();
        ((TelemetrySnapshotStub)telemetry).ExportCancellation.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task ModOnboardingServiceExtensions_ShouldThrow_WhenServiceIsNull()
    {
        IModOnboardingService? service = null;
        var request = new ModOnboardingRequest("draft", "Draft", "base_swfoc", Array.Empty<ModLaunchSample>());

        var profileCall = () => ModOnboardingServiceExtensions.ScaffoldDraftProfileAsync(service!, request);
        var batchCall = () => ModOnboardingServiceExtensions.ScaffoldDraftProfilesFromSeedsAsync(service!, new ModOnboardingSeedBatchRequest(null, Array.Empty<GeneratedProfileSeed>()));

        await profileCall.Should().ThrowAsync<ArgumentNullException>();
        await batchCall.Should().ThrowAsync<ArgumentNullException>();
    }

    private static TrainerProfile BuildProfile()
    {
        return new TrainerProfile(
            Id: "profile",
            DisplayName: "Profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>());
    }

    private static AttachSession BuildSession(string profileId)
    {
        var process = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic);

        var build = new ProfileBuild(profileId, "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc);
        return new AttachSession(profileId, process, build, new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
    }

    private static SaveDocument BuildSaveDocument()
    {
        return new SaveDocument(
            Path: "save.sav",
            SchemaId: "schema",
            Raw: new byte[16],
            Root: new SaveNode("/", "root", "object", null));
    }

    private static SavePatchPack BuildPatch()
    {
        return new SavePatchPack(
            Metadata: new SavePatchMetadata("1", "profile", "schema", "hash", DateTimeOffset.UtcNow),
            Compatibility: new SavePatchCompatibility(new[] { "profile" }, "schema"),
            Operations: new[]
            {
                new SavePatchOperation(SavePatchOperationKind.SetValue, "/economy/credits", "credits", "int32", 1, 2, 4)
            });
    }

    private sealed class ActionReliabilityStub : IActionReliabilityService
    {
        public IReadOnlyDictionary<string, IReadOnlyList<string>>? LastCatalog { get; private set; }

        public IReadOnlyList<ActionReliabilityInfo> Evaluate(TrainerProfile profile, AttachSession session, IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
        {
            _ = profile;
            _ = session;
            LastCatalog = catalog;
            return new[]
            {
                new ActionReliabilityInfo("set_credits", ActionReliabilityState.Stable, "ok", 1.0)
            };
        }
    }

    private sealed class AuditLoggerStub : IAuditLogger
    {
        public CancellationToken Cancellation { get; private set; }

        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken)
        {
            _ = record;
            Cancellation = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class HelperModStub : IHelperModService
    {
        public CancellationToken DeployCancellation { get; private set; }
        public CancellationToken VerifyCancellation { get; private set; }

        public Task<string> DeployAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            DeployCancellation = cancellationToken;
            return Task.FromResult("ok");
        }

        public Task<bool> VerifyAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            VerifyCancellation = cancellationToken;
            return Task.FromResult(true);
        }
    }

    private sealed class ModCalibrationStub : IModCalibrationService
    {
        public CancellationToken ExportCancellation { get; private set; }
        public CancellationToken ReportCancellation { get; private set; }

        public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            ExportCancellation = cancellationToken;
            return Task.FromResult(new ModCalibrationArtifactResult(true, "artifact.json", "fingerprint", Array.Empty<CalibrationCandidate>(), Array.Empty<string>()));
        }

        public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(TrainerProfile profile, AttachSession? session, DependencyValidationResult? dependencyValidation, IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = session;
            _ = dependencyValidation;
            _ = catalog;
            ReportCancellation = cancellationToken;
            return Task.FromResult(new ModCompatibilityReport(profile.Id, DateTimeOffset.UtcNow, RuntimeMode.Galactic, DependencyValidationStatus.Pass, 0, true, Array.Empty<ModActionCompatibility>(), Array.Empty<string>()));
        }
    }

    private sealed class ModOnboardingStub : IModOnboardingService
    {
        public CancellationToken ProfileCancellation { get; private set; }
        public CancellationToken BatchCancellation { get; private set; }

        public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            ProfileCancellation = cancellationToken;
            return Task.FromResult(new ModOnboardingResult(true, "profile", "profile.json", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        }

        public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            BatchCancellation = cancellationToken;
            return Task.FromResult(new ModOnboardingBatchResult(true, 0, 0, 0, Array.Empty<ModOnboardingBatchItemResult>()));
        }
    }

    private sealed class ProcessLocatorStub : IProcessLocator
    {
        private readonly IReadOnlyList<ProcessMetadata> _processes;

        public ProcessLocatorStub(ProcessMetadata process)
        {
            _processes = new[] { process };
        }

        public CancellationToken FindCancellation { get; private set; }
        public CancellationToken BestCancellation { get; private set; }

        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
        {
            FindCancellation = cancellationToken;
            return Task.FromResult(_processes);
        }

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
        {
            _ = target;
            BestCancellation = cancellationToken;
            return Task.FromResult<ProcessMetadata?>(_processes[0]);
        }
    }

    private sealed class ProfileUpdateStub : IProfileUpdateService
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            Cancellation = cancellationToken;
            return Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
        }

        public Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            Cancellation = cancellationToken;
            return Task.FromResult("installed");
        }

        public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            Cancellation = cancellationToken;
            return Task.FromResult(new ProfileInstallResult(true, profileId, "target", "backup", null, "ok"));
        }

        public Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            Cancellation = cancellationToken;
            return Task.FromResult(new ProfileRollbackResult(true, profileId, "target", "backup", "ok"));
        }
    }

    private sealed class SaveCodecStub : ISaveCodec
    {
        private readonly SaveDocument _doc;

        public SaveCodecStub(SaveDocument doc)
        {
            _doc = doc;
        }

        public CancellationToken Cancellation { get; private set; }

        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken)
        {
            _ = path;
            _ = schemaId;
            Cancellation = cancellationToken;
            return Task.FromResult(_doc);
        }

        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken)
        {
            _ = document;
            _ = nodePath;
            _ = value;
            Cancellation = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            _ = document;
            Cancellation = cancellationToken;
            return Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        }

        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken)
        {
            _ = document;
            _ = outputPath;
            Cancellation = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            _ = document;
            Cancellation = cancellationToken;
            return Task.FromResult(true);
        }
    }

    private sealed class SavePatchApplyStub : ISavePatchApplyService
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<SavePatchApplyResult> ApplyAsync(string targetSavePath, SavePatchPack pack, string targetProfileId, bool strict, CancellationToken cancellationToken)
        {
            _ = targetSavePath;
            _ = pack;
            _ = targetProfileId;
            _ = strict;
            Cancellation = cancellationToken;
            return Task.FromResult(new SavePatchApplyResult(SavePatchApplyClassification.Applied, true, "ok"));
        }

        public Task<SaveRollbackResult> RestoreLastBackupAsync(string targetSavePath, CancellationToken cancellationToken)
        {
            _ = targetSavePath;
            Cancellation = cancellationToken;
            return Task.FromResult(new SaveRollbackResult(true, "ok"));
        }
    }

    private sealed class SavePatchPackStub : ISavePatchPackService
    {
        private readonly SavePatchPack _pack;

        public SavePatchPackStub(SavePatchPack pack)
        {
            _pack = pack;
        }

        public CancellationToken Cancellation { get; private set; }

        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId, CancellationToken cancellationToken)
        {
            _ = originalDoc;
            _ = editedDoc;
            _ = profileId;
            Cancellation = cancellationToken;
            return Task.FromResult(_pack);
        }

        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken)
        {
            _ = path;
            Cancellation = cancellationToken;
            return Task.FromResult(_pack);
        }

        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
        {
            _ = pack;
            _ = targetDoc;
            _ = targetProfileId;
            Cancellation = cancellationToken;
            return Task.FromResult(new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>()));
        }

        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
        {
            _ = pack;
            _ = targetDoc;
            _ = targetProfileId;
            Cancellation = cancellationToken;
            return Task.FromResult(new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), _pack.Operations));
        }
    }

    private sealed class SignatureResolverStub : ISignatureResolver
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<SymbolMap> ResolveAsync(ProfileBuild profileBuild, IReadOnlyList<SignatureSet> signatureSets, IReadOnlyDictionary<string, long> fallbackOffsets, CancellationToken cancellationToken)
        {
            _ = profileBuild;
            _ = signatureSets;
            _ = fallbackOffsets;
            Cancellation = cancellationToken;
            return Task.FromResult(new SymbolMap(new Dictionary<string, SymbolInfo>()));
        }
    }

    private sealed class SupportBundleStub : ISupportBundleService
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            Cancellation = cancellationToken;
            return Task.FromResult(new SupportBundleResult(true, "bundle.zip", "manifest.json", Array.Empty<string>(), Array.Empty<string>()));
        }
    }

    private sealed class TelemetrySnapshotStub : ITelemetrySnapshotService
    {
        public CancellationToken ExportCancellation { get; private set; }
        private int _totalActions;

        public void RecordAction(string actionId, AddressSource source, bool succeeded)
        {
            _ = actionId;
            _ = source;
            _ = succeeded;
            _totalActions++;
        }

        public TelemetrySnapshot CreateSnapshot()
        {
            return new TelemetrySnapshot(DateTimeOffset.UtcNow, new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), _totalActions, 0, 0, 0);
        }

        public Task<string> ExportSnapshotAsync(string outputDirectory, CancellationToken cancellationToken)
        {
            _ = outputDirectory;
            ExportCancellation = cancellationToken;
            return Task.FromResult("snapshot.json");
        }

        public void Reset()
        {
            _totalActions = 0;
        }
    }
}


