using FluentAssertions;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class ContractsAndModelsCoverageTests
{
    [Fact]
    public async Task InterfaceDefaultOverloads_ShouldForwardCancellationTokenNone()
    {
        IHelperBridgeBackend helperBackend = new StubHelperBridgeBackend();
        IGameLaunchService launchService = new StubGameLaunchService();
        IContentTransplantService transplantService = new StubTransplantService();
        ITransplantCompatibilityService transplantCompatibility = new StubTransplantCompatibilityService();
        IWorkshopInventoryService inventoryService = new StubWorkshopInventoryService();

        await helperBackend.ProbeAsync(new HelperBridgeProbeRequest(
            "profile",
            BuildProcessMetadata(),
            new[] { BuildHook() }));
        await helperBackend.ExecuteAsync(new HelperBridgeRequest(
            ActionRequest: BuildActionRequest(),
            Process: BuildProcessMetadata(),
            Hook: BuildHook(),
            OperationKind: HelperBridgeOperationKind.SpawnTacticalEntity,
            InvocationContractVersion: "1.0"));
        await launchService.LaunchAsync(new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.Vanilla));
        await transplantService.ExecuteAsync(new TransplantPlan("profile", Array.Empty<string>(), Array.Empty<RosterEntityRecord>()));
        await transplantCompatibility.ValidateAsync("profile", Array.Empty<string>(), Array.Empty<RosterEntityRecord>());
        await inventoryService.DiscoverInstalledAsync(new WorkshopInventoryRequest());

        ((StubHelperBridgeBackend)helperBackend).ProbeCancellation.Should().Be(CancellationToken.None);
        ((StubHelperBridgeBackend)helperBackend).ExecuteCancellation.Should().Be(CancellationToken.None);
        ((StubGameLaunchService)launchService).Cancellation.Should().Be(CancellationToken.None);
        ((StubTransplantService)transplantService).Cancellation.Should().Be(CancellationToken.None);
        ((StubTransplantCompatibilityService)transplantCompatibility).Cancellation.Should().Be(CancellationToken.None);
        ((StubWorkshopInventoryService)inventoryService).Cancellation.Should().Be(CancellationToken.None);
    }

    [Fact]
    public void EmptyFactoriesAndRecords_ShouldReturnExpectedDefaults()
    {
        var launchRequest = new GameLaunchRequest(GameLaunchTarget.Sweaw, GameLaunchMode.ModPath, new[] { "1" }, "Mods\\AOTR", "profile", true);
        var launchResult = new GameLaunchResult(true, "ok", 42, @"C:\Games\swfoc.exe", "STEAMMOD=1", new Dictionary<string, object?>());
        var mechanicReport = ModMechanicReport.Empty("profile");
        var transplantReport = TransplantValidationReport.Empty("profile");
        var inventoryGraph = WorkshopInventoryGraph.Empty("32470");
        var entity = new RosterEntityRecord(
            EntityId: "unit_x",
            DisplayName: "Unit X",
            SourceProfileId: "profile",
            SourceWorkshopId: "1234",
            EntityKind: RosterEntityKind.Unit,
            DefaultFaction: "EMPIRE",
            AllowedModes: new[] { RuntimeMode.Galactic },
            VisualRef: "visual.png",
            DependencyRefs: new[] { "dep_a" },
            TransplantState: "native");

        launchRequest.Target.Should().Be(GameLaunchTarget.Sweaw);
        launchRequest.Mode.Should().Be(GameLaunchMode.ModPath);
        launchRequest.WorkshopIds.Should().Equal("1");
        launchRequest.ModPath.Should().Be("Mods\\AOTR");
        launchRequest.ProfileIdHint.Should().Be("profile");
        launchRequest.TerminateExistingTargets.Should().BeTrue();

        launchResult.Succeeded.Should().BeTrue();
        launchResult.ProcessId.Should().Be(42);
        launchResult.ExecutablePath.Should().Be(@"C:\Games\swfoc.exe");

        mechanicReport.ProfileId.Should().Be("profile");
        mechanicReport.DependenciesSatisfied.Should().BeFalse();
        mechanicReport.HelperBridgeReady.Should().BeFalse();
        mechanicReport.ActionSupport.Should().BeEmpty();

        transplantReport.TargetProfileId.Should().Be("profile");
        transplantReport.AllResolved.Should().BeTrue();
        transplantReport.TotalEntities.Should().Be(0);
        transplantReport.Entities.Should().BeEmpty();

        inventoryGraph.AppId.Should().Be("32470");
        inventoryGraph.Items.Should().BeEmpty();
        inventoryGraph.Chains.Should().BeEmpty();

        entity.EntityId.Should().Be("unit_x");
        entity.EntityKind.Should().Be(RosterEntityKind.Unit);
        entity.AllowedModes.Should().ContainSingle().Which.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void ModelRecordsAndCatalog_ShouldExposeConstructedValues()
    {
        var inventoryItem = new WorkshopInventoryItem(
            WorkshopId: "1000",
            Title: "Test Item",
            ItemType: WorkshopItemType.Submod,
            ParentWorkshopIds: new[] { "999" },
            Tags: new[] { "Submod" },
            Description: "desc",
            ClassificationReason: "parent_dependency",
            Metadata: new Dictionary<string, string> { ["author"] = "tester" });
        var chain = new WorkshopInventoryChain(
            ChainId: "999>1000",
            OrderedWorkshopIds: new[] { "999", "1000" },
            ClassificationReason: "parent_dependency",
            ParentFirst: true,
            MissingParentIds: new[] { "123" });
        var transplantResult = new TransplantResult(
            Succeeded: false,
            ReasonCode: RuntimeReasonCode.TRANSPLANT_VALIDATION_FAILED,
            Message: "blocked",
            Report: TransplantValidationReport.Empty("profile"),
            ArtifactPath: @"C:\tmp\report.json",
            Diagnostics: new Dictionary<string, object?> { ["reason"] = "missing_dep" });
        var actionSupport = new ModMechanicSupport(
            ActionId: "set_context_allegiance",
            Supported: false,
            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
            Message: "missing",
            Confidence: 0.42d);

        inventoryItem.WorkshopId.Should().Be("1000");
        inventoryItem.ParentWorkshopIds.Should().ContainSingle().Which.Should().Be("999");
        chain.ChainId.Should().Be("999>1000");
        chain.MissingParentIds.Should().Contain("123");
        transplantResult.Succeeded.Should().BeFalse();
        transplantResult.ArtifactPath.Should().Be(@"C:\tmp\report.json");
        actionSupport.ActionId.Should().Be("set_context_allegiance");
        actionSupport.Supported.Should().BeFalse();
    }

    [Fact]
    public void SdkOperationCatalog_IsModeAllowed_ShouldHandleAnyTacticalAndUnknownModes()
    {
        SdkOperationCatalog.TryGet("set_owner", out var setOwner).Should().BeTrue();
        setOwner.IsMutation.Should().BeTrue();
        setOwner.IsModeAllowed(RuntimeMode.TacticalLand).Should().BeTrue();
        setOwner.IsModeAllowed(RuntimeMode.TacticalSpace).Should().BeTrue();
        setOwner.IsModeAllowed(RuntimeMode.Galactic).Should().BeTrue();
        setOwner.IsModeAllowed(RuntimeMode.Unknown).Should().BeFalse();

        SdkOperationCatalog.TryGet("list_selected", out var listSelected).Should().BeTrue();
        listSelected.IsMutation.Should().BeFalse();
        listSelected.IsModeAllowed(RuntimeMode.Unknown).Should().BeTrue();
        listSelected.IsModeAllowed(RuntimeMode.Galactic).Should().BeTrue();
    }
    [Fact]
    public void ModelRecords_ShouldRetainOptionalFieldsAndDefaults()
    {
        var models = BuildOptionalFieldModels();

        models.Item.Title.Should().Be("ROE Submod");
        models.Chain.MissingParentIds.Should().ContainSingle().Which.Should().Be("9999999999");
        models.Graph.Items.Should().ContainSingle();
        models.Graph.Chains.Should().ContainSingle();
        models.Transplant.ArtifactPath.Should().EndWith("report.json");
        models.Transplant.Diagnostics.Should().ContainKey("source");
        models.MechanicReport.ActionSupport.Should().ContainSingle();
        models.Roster.DisplayName.Should().Be("Barracks");
    }

    [Fact]
    public void InventoryAndTransplantModels_ShouldHonorDefaultOverloads()
    {
        var chain = new WorkshopInventoryChain(
            ChainId: "base",
            OrderedWorkshopIds: new[] { "1397421866" },
            ClassificationReason: "independent_mod");
        var emptyGraph = WorkshopInventoryGraph.Empty();
        var generatedAt = DateTimeOffset.UtcNow;
        var report = new TransplantValidationReport(
            TargetProfileId: "profile",
            GeneratedAtUtc: generatedAt,
            AllResolved: true,
            TotalEntities: 0,
            BlockingEntityCount: 0,
            Entities: Array.Empty<TransplantEntityValidation>(),
            Diagnostics: new Dictionary<string, object?>());
        var result = new TransplantResult(
            Succeeded: true,
            ReasonCode: RuntimeReasonCode.TRANSPLANT_APPLIED,
            Message: "ok",
            Report: report);

        chain.ParentFirst.Should().BeTrue();
        emptyGraph.AppId.Should().Be("32470");
        report.GeneratedAtUtc.Should().Be(generatedAt);
        result.Message.Should().Be("ok");
        result.ArtifactPath.Should().BeNull();
    }

    [Fact]
    public void ModMechanicAndRosterModels_ShouldKeepConstructorValues()
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var report = new ModMechanicReport(
            ProfileId: "profile",
            GeneratedAtUtc: generatedAt,
            DependenciesSatisfied: true,
            HelperBridgeReady: false,
            ActionSupport: Array.Empty<ModMechanicSupport>(),
            Diagnostics: new Dictionary<string, object?>());
        var roster = new RosterEntityRecord(
            EntityId: "unit_x",
            DisplayName: "Unit X",
            SourceProfileId: "profile",
            SourceWorkshopId: null,
            EntityKind: RosterEntityKind.Unit,
            DefaultFaction: "Empire",
            AllowedModes: new[] { RuntimeMode.Galactic });

        report.GeneratedAtUtc.Should().Be(generatedAt);
        roster.DefaultFaction.Should().Be("Empire");
    }

    [Fact]
    public void SdkOperationDefinition_IsModeAllowed_ShouldHandleAnyTacticalFallback()
    {
        SdkOperationCatalog.TryGet("set_owner", out var definition).Should().BeTrue();
        definition.Should().NotBeNull();

        definition!.IsModeAllowed(RuntimeMode.TacticalLand).Should().BeTrue();
        definition.IsModeAllowed(RuntimeMode.TacticalSpace).Should().BeTrue();
        definition.IsModeAllowed(RuntimeMode.Galactic).Should().BeTrue();
        definition.IsModeAllowed(RuntimeMode.Unknown).Should().BeFalse();

        var readOnly = SdkOperationDefinition.ReadOnly("list_selected");
        readOnly.IsModeAllowed(RuntimeMode.Unknown).Should().BeTrue();
    }

    [Fact]
    public void HeroMechanicModels_ShouldRetainConstructorValues()
    {
        var profile = new HeroMechanicsProfile(
            SupportsRespawn: true,
            SupportsPermadeath: false,
            SupportsRescue: true,
            DefaultRespawnTime: 7,
            RespawnExceptionSources: new[] { "RespawnExceptions.lua" },
            DuplicateHeroPolicy: "rescue_or_respawn",
            Diagnostics: new Dictionary<string, string> { ["profileId"] = "aotr_1397421866_swfoc" });

        var request = new HeroEditRequest(
            TargetHeroId: "MACE_WINDU",
            DesiredState: "respawn_pending",
            RespawnPolicyOverride: "force_respawn",
            AllowDuplicate: true,
            TargetFaction: "REPUBLIC",
            SourceFaction: "EMPIRE",
            Parameters: new Dictionary<string, object?> { ["planetId"] = "coruscant" });

        var result = new HeroEditResult(
            TargetHeroId: "MACE_WINDU",
            PreviousState: "dead",
            CurrentState: "respawn_pending",
            Applied: true,
            ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
            Message: "Hero state updated.",
            Diagnostics: new Dictionary<string, object?> { ["helperExecutionPath"] = "plugin_dispatch" });

        profile.SupportsRespawn.Should().BeTrue();
        profile.DefaultRespawnTime.Should().Be(7);
        profile.RespawnExceptionSources.Should().ContainSingle().Which.Should().Be("RespawnExceptions.lua");
        profile.DuplicateHeroPolicy.Should().Be("rescue_or_respawn");

        request.TargetHeroId.Should().Be("MACE_WINDU");
        request.AllowDuplicate.Should().BeTrue();
        request.TargetFaction.Should().Be("REPUBLIC");

        result.Applied.Should().BeTrue();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_EXECUTION_APPLIED);
        result.Diagnostics.Should().ContainKey("helperExecutionPath");
    }


    [Fact]
    public async Task RuntimeAdapterAndProfileRepository_DefaultMethods_ShouldForwardAndReturnExpectedDefaults()
    {
        IRuntimeAdapter runtimeAdapter = new MinimalRuntimeAdapter();
        IProfileRepository profileRepository = new RecordingProfileRepository();

        var calibration = await runtimeAdapter.ScanCalibrationCandidatesAsync(new RuntimeCalibrationScanRequest("credits"));
        calibration.Succeeded.Should().BeFalse();
        calibration.ReasonCode.Should().Be("not_supported");

        await runtimeAdapter.AttachAsync("profile");
        await runtimeAdapter.ReadAsync<int>("credits");
        await runtimeAdapter.WriteAsync("credits", 99);
        await runtimeAdapter.ExecuteAsync(BuildActionRequest());
        await runtimeAdapter.DetachAsync();

        var recorder = (RecordingProfileRepository)profileRepository;
        await profileRepository.LoadManifestAsync();
        await profileRepository.LoadProfileAsync("profile");
        await profileRepository.ResolveInheritedProfileAsync("profile");
        await profileRepository.ValidateProfileAsync(recorder.Profile);
        await profileRepository.ListAvailableProfilesAsync();

        recorder.LoadManifestCancellation.Should().Be(CancellationToken.None);
        recorder.LoadProfileCancellation.Should().Be(CancellationToken.None);
        recorder.ResolveInheritedCancellation.Should().Be(CancellationToken.None);
        recorder.ValidateCancellation.Should().Be(CancellationToken.None);
        recorder.ListCancellation.Should().Be(CancellationToken.None);
    }

    [Fact]
    public void HeroMechanicsEmptyAndRuntimeCalibrationViewItem_ShouldReturnExpectedDefaults()
    {
        var empty = HeroMechanicsProfile.Empty();
        var viewItem = new SwfocTrainer.App.Models.RuntimeCalibrationCandidateViewItem(
            SuggestedPattern: "90 90 90",
            Offset: 4,
            AddressMode: "HitPlusOffset",
            ValueType: "Int32",
            InstructionRva: "0x1234",
            ReferenceCount: 3,
            Snippet: "mov eax, [credits]");
        var variant = new HeroVariantRequest(
            SourceHeroId: "MACE_WINDU",
            VariantHeroId: "MACE_WINDU_ELITE",
            DisplayName: "Mace Windu Elite",
            StatOverrides: new Dictionary<string, object?> { ["hp"] = 4000 },
            AbilityOverrides: new Dictionary<string, object?> { ["cooldown"] = 0.5d },
            ReplaceExisting: true);
        var calibrationCandidate = new RuntimeCalibrationCandidate(
            SuggestedPattern: "48 8B 05 ?? ?? ?? ??",
            Offset: 3,
            AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset,
            ValueType: SymbolValueType.Int32,
            InstructionRva: "0x1020",
            Snippet: "mov eax, [rip+disp32]",
            ReferenceCount: 2);
        var capabilityAnchor = new CapabilityAnchor("credits_anchor", "pattern", "90 90", Required: false, Notes: "optional");

        empty.SupportsRespawn.Should().BeFalse();
        empty.SupportsPermadeath.Should().BeFalse();
        empty.SupportsRescue.Should().BeFalse();
        empty.DefaultRespawnTime.Should().BeNull();
        empty.RespawnExceptionSources.Should().BeEmpty();
        empty.DuplicateHeroPolicy.Should().Be("unknown");
        empty.Diagnostics.Should().NotBeNull();

        viewItem.SuggestedPattern.Should().Be("90 90 90");
        viewItem.Offset.Should().Be(4);
        viewItem.AddressMode.Should().Be("HitPlusOffset");
        viewItem.ReferenceCount.Should().Be(3);
        variant.VariantHeroId.Should().Be("MACE_WINDU_ELITE");
        variant.ReplaceExisting.Should().BeTrue();
        calibrationCandidate.ReferenceCount.Should().Be(2);
        capabilityAnchor.Required.Should().BeFalse();
        capabilityAnchor.Notes.Should().Be("optional");
    }
    private static (
        WorkshopInventoryItem Item,
        WorkshopInventoryChain Chain,
        WorkshopInventoryGraph Graph,
        TransplantResult Transplant,
        ModMechanicReport MechanicReport,
        RosterEntityRecord Roster) BuildOptionalFieldModels()
    {
        var item = new WorkshopInventoryItem(
            WorkshopId: "3447786229",
            Title: "ROE Submod",
            ItemType: WorkshopItemType.Submod,
            ParentWorkshopIds: new[] { "1397421866" },
            Tags: new[] { "Submod" },
            Description: "desc",
            ClassificationReason: "parent_dependency");

        var chain = new WorkshopInventoryChain(
            ChainId: "1397421866>3447786229",
            OrderedWorkshopIds: new[] { "1397421866", "3447786229" },
            ClassificationReason: "parent_dependency_partial_missing",
            MissingParentIds: new[] { "9999999999" });

        var graph = new WorkshopInventoryGraph(
            AppId: "32470",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Items: new[] { item },
            Diagnostics: new[] { "manifest=path" },
            Chains: new[] { chain });

        var transplant = new TransplantResult(
            Succeeded: false,
            ReasonCode: RuntimeReasonCode.TRANSPLANT_VALIDATION_FAILED,
            Message: "blocked",
            Report: TransplantValidationReport.Empty("profile"),
            ArtifactPath: @"C:\tmp\report.json",
            Diagnostics: new Dictionary<string, object?> { ["source"] = "tests" });

        var mechanicReport = new ModMechanicReport(
            ProfileId: "profile",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport:
            [
                new ModMechanicSupport("set_context_allegiance", true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok", 0.95d)
            ],
            Diagnostics: new Dictionary<string, object?>());

        var roster = new RosterEntityRecord(
            EntityId: "BARRACKS",
            DisplayName: "Barracks",
            SourceProfileId: "base_swfoc",
            SourceWorkshopId: null,
            EntityKind: RosterEntityKind.Building,
            DefaultFaction: "Empire",
            AllowedModes: new[] { RuntimeMode.Galactic });

        return (item, chain, graph, transplant, mechanicReport, roster);
    }

    private sealed class StubHelperBridgeBackend : IHelperBridgeBackend
    {
        public CancellationToken ProbeCancellation { get; private set; }
        public CancellationToken ExecuteCancellation { get; private set; }

        public Task<HelperBridgeProbeResult> ProbeAsync(HelperBridgeProbeRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            ProbeCancellation = cancellationToken;
            return Task.FromResult(new HelperBridgeProbeResult(
                Available: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok",
                Diagnostics: new Dictionary<string, object?>()));
        }

        public Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            ExecuteCancellation = cancellationToken;
            return Task.FromResult(new HelperBridgeExecutionResult(
                Succeeded: true,
                ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
                Message: "ok",
                Diagnostics: new Dictionary<string, object?>()));
        }
    }

    private sealed class StubGameLaunchService : IGameLaunchService
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            Cancellation = cancellationToken;
            return Task.FromResult(new GameLaunchResult(true, "ok", 1, "swfoc.exe", string.Empty, null));
        }
    }

    private sealed class StubTransplantService : IContentTransplantService
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<TransplantResult> ExecuteAsync(TransplantPlan plan, CancellationToken cancellationToken)
        {
            _ = plan;
            Cancellation = cancellationToken;
            var report = TransplantValidationReport.Empty(plan.TargetProfileId);
            return Task.FromResult(new TransplantResult(
                Succeeded: true,
                ReasonCode: RuntimeReasonCode.TRANSPLANT_APPLIED,
                Message: "ok",
                Report: report));
        }
    }

    private sealed class StubTransplantCompatibilityService : ITransplantCompatibilityService
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<TransplantValidationReport> ValidateAsync(
            string targetProfileId,
            IReadOnlyList<string> activeWorkshopIds,
            IReadOnlyList<RosterEntityRecord> entities,
            CancellationToken cancellationToken)
        {
            _ = activeWorkshopIds;
            _ = entities;
            Cancellation = cancellationToken;
            return Task.FromResult(TransplantValidationReport.Empty(targetProfileId));
        }
    }

    private sealed class StubWorkshopInventoryService : IWorkshopInventoryService
    {
        public CancellationToken Cancellation { get; private set; }

        public Task<WorkshopInventoryGraph> DiscoverInstalledAsync(WorkshopInventoryRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            Cancellation = cancellationToken;
            return Task.FromResult(WorkshopInventoryGraph.Empty(request.AppId));
        }
    }

    private static ProcessMetadata BuildProcessMetadata()
    {
        return new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic);
    }

    private static HelperHookSpec BuildHook()
    {
        return new HelperHookSpec(
            Id: "spawn_bridge",
            Script: "scripts/spawn.lua",
            Version: "1.0",
            EntryPoint: "SWFOC_Trainer_Spawn_Context");
    }

    private static ActionExecutionRequest BuildActionRequest()
    {
        return new ActionExecutionRequest(
            Action: new ActionSpec(
                Id: "spawn_tactical_entity",
                Category: ActionCategory.Global,
                Mode: RuntimeMode.AnyTactical,
                ExecutionKind: ExecutionKind.Helper,
                PayloadSchema: new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: new JsonObject(),
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.AnyTactical,
            Context: null);
    }
    private sealed class MinimalRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached { get; private set; }

        public AttachSession? CurrentSession { get; private set; }

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IsAttached = true;
            CurrentSession = new AttachSession(
                profileId,
                BuildProcessMetadata(),
                new ProfileBuild(profileId, "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
                new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
                DateTimeOffset.UtcNow);
            return Task.FromResult(CurrentSession);
        }

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = cancellationToken;
            return Task.FromResult(default(T));
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = value;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature, null));
        }

        public Task DetachAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IsAttached = false;
            CurrentSession = null;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProfileRepository : IProfileRepository
    {
        public CancellationToken LoadManifestCancellation { get; private set; }
        public CancellationToken LoadProfileCancellation { get; private set; }
        public CancellationToken ResolveInheritedCancellation { get; private set; }
        public CancellationToken ValidateCancellation { get; private set; }
        public CancellationToken ListCancellation { get; private set; }

        public TrainerProfile Profile { get; } = new(
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

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            LoadManifestCancellation = cancellationToken;
            return Task.FromResult(new ProfileManifest("1", DateTimeOffset.UtcNow, new[]
            {
                new ProfileManifestEntry("profile", "1", "hash", "url", "schema")
            }));
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            LoadProfileCancellation = cancellationToken;
            return Task.FromResult(Profile);
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            ResolveInheritedCancellation = cancellationToken;
            return Task.FromResult(Profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            ValidateCancellation = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            ListCancellation = cancellationToken;
            return Task.FromResult((IReadOnlyList<string>)new[] { "profile" });
        }
    }

}






