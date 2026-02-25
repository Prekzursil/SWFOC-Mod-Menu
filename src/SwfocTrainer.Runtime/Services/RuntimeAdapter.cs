using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;

namespace SwfocTrainer.Runtime.Services;

public sealed class RuntimeAdapter : IRuntimeAdapter
{
    private const string CreditsHookPatternText = "F3 0F 2C 50 70 89 57";

    private const int CreditsHookJumpLength = 5;
    private const byte CreditsContextOffsetByte = 0x70;

    private const int CreditsHookCodeSize = 41;
    private const int CreditsHookDataLastContextOffset = CreditsHookCodeSize;
    private const int CreditsHookDataHitCountOffset = CreditsHookDataLastContextOffset + sizeof(long);
    private const int CreditsHookDataLockEnabledOffset = CreditsHookDataHitCountOffset + sizeof(int);
    private const int CreditsHookDataForcedFloatBitsOffset = CreditsHookDataLockEnabledOffset + sizeof(int);
    private const int CreditsHookCaveSize = CreditsHookDataForcedFloatBitsOffset + sizeof(int);

    private const int CreditsHookPulseTimeoutMs = 2500;
    private const int CreditsHookPollingDelayMs = 25;
    private const float CreditsFloatTolerance = 0.9f;
    private const int CreditsStoreCorrelationWindowBytes = 96;

    private const string UnitCapHookPatternText = "48 8B 74 24 68 8B C7";
    private static readonly byte[] UnitCapHookOriginalBytes = [0x48, 0x8B, 0x74, 0x24, 0x68];
    private const int UnitCapHookJumpLength = 5;
    private const int UnitCapHookCaveSize = 15;

    private static readonly byte[][] InstantBuildHookPatterns =
    [
        [0x8B, 0x83, 0x04, 0x09, 0x00, 0x00], // build time/cost (variant A)
        [0x8B, 0x83, 0xFC, 0x09, 0x00, 0x00]  // build time/cost (variant B)
    ];
    private const int InstantBuildHookInstructionLength = 6;
    private const int InstantBuildHookJumpLength = 5;
    private const int InstantBuildHookCaveSize = 31;

    private const string DiagnosticKeyHookState = "hookState";
    private const string DiagnosticKeyCreditsStateTag = "creditsStateTag";
    private const string DiagnosticKeyState = "state";
    private const string DiagnosticKeyExpertOverrideEnabled = "expertOverrideEnabled";
    private const string DiagnosticKeyOverrideReason = "overrideReason";
    private const string DiagnosticKeyPanicDisableState = "panicDisableState";
    private const string PanicDisableStateActive = "active";
    private const string PanicDisableStateInactive = "inactive";
    private const string ExpertOverrideEnvVarName = "SWFOC_EXPERT_MUTATION_OVERRIDES";
    private const string ExpertOverridePanicEnvVarName = "SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC";
    private const string ActionIdSetUnitCap = "set_unit_cap";
    private const string ActionIdToggleInstantBuildPatch = "toggle_instant_build_patch";
    private const string ActionIdSetCredits = "set_credits";
    private const string SymbolCredits = "credits";

    private static readonly string[] ResultHookStateKeys =
    [
        DiagnosticKeyHookState,
        DiagnosticKeyCreditsStateTag,
        DiagnosticKeyState
    ];

    private static readonly HashSet<string> PromotedExtenderActionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "freeze_timer",
        "toggle_fog_reveal",
        "toggle_ai",
        ActionIdSetUnitCap,
        ActionIdToggleInstantBuildPatch
    };

    private readonly IProcessLocator _processLocator;
    private readonly IProfileRepository _profileRepository;
    private readonly ISignatureResolver _signatureResolver;
    private readonly IModDependencyValidator _modDependencyValidator;
    private readonly ISymbolHealthService _symbolHealthService;
    private readonly IProfileVariantResolver? _profileVariantResolver;
    private readonly ISdkOperationRouter? _sdkOperationRouter;
    private readonly IBackendRouter _backendRouter;
    private readonly IExecutionBackend? _extenderBackend;
    private readonly ILogger<RuntimeAdapter> _logger;
    private readonly string _calibrationArtifactRoot;

    private static readonly JsonSerializerOptions SymbolValidationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private ProcessMemoryAccessor? _memory;
    private nint _creditsHookInjectionAddress;
    private byte[]? _creditsHookOriginalBytesBackup;
    private nint _creditsHookCodeCaveAddress;
    private nint _creditsHookLastContextAddress;
    private nint _creditsHookHitCountAddress;
    private nint _creditsHookLockEnabledAddress;
    private nint _creditsHookForcedFloatBitsAddress;
    private byte _creditsHookContextOffset = CreditsContextOffsetByte;

    private nint _unitCapHookInjectionAddress;
    private byte[]? _unitCapHookOriginalBytesBackup;
    private nint _unitCapHookCodeCaveAddress;
    private nint _unitCapHookValueAddress;

    private nint _instantBuildHookInjectionAddress;
    private byte[]? _instantBuildHookOriginalBytesBackup;
    private nint _instantBuildHookCodeCaveAddress;

    /// <summary>
    /// Tracks active code patches keyed by symbol name.
    /// Value = original bytes that were saved before the patch was applied.
    /// </summary>
    private readonly Dictionary<string, (nint Address, byte[] OriginalBytes)> _activeCodePatches = new();
    private HashSet<string> _dependencySoftDisabledActions = new(StringComparer.OrdinalIgnoreCase);
    private DependencyValidationStatus _dependencyValidationStatus = DependencyValidationStatus.Pass;
    private string? _dependencyValidationMessage;
    private TrainerProfile? _attachedProfile;
    private Dictionary<string, List<SymbolValidationRule>> _symbolValidationRules = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _criticalSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _telemetryLock = new();
    private readonly Dictionary<string, int> _actionSuccessCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _actionFailureCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _symbolSourceCounters = new(StringComparer.OrdinalIgnoreCase);

    public RuntimeAdapter(
        IProcessLocator processLocator,
        IProfileRepository profileRepository,
        ISignatureResolver signatureResolver,
        ILogger<RuntimeAdapter> logger,
        IServiceProvider serviceProvider)
    {
        _processLocator = processLocator;
        _profileRepository = profileRepository;
        _signatureResolver = signatureResolver;
        _modDependencyValidator = ResolveOptionalService<IModDependencyValidator>(serviceProvider) ?? new ModDependencyValidator();
        _symbolHealthService = ResolveOptionalService<ISymbolHealthService>(serviceProvider) ?? new SymbolHealthService();
        _profileVariantResolver = ResolveOptionalService<IProfileVariantResolver>(serviceProvider);
        _sdkOperationRouter = ResolveOptionalService<ISdkOperationRouter>(serviceProvider);
        _backendRouter = ResolveOptionalService<IBackendRouter>(serviceProvider) ?? new BackendRouter();
        _extenderBackend = ResolveOptionalService<IExecutionBackend>(serviceProvider) ?? new NamedPipeExtenderBackend();
        _logger = logger;
        _calibrationArtifactRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwfocTrainer",
            "calibration");
    }

    public RuntimeAdapter(
        IProcessLocator processLocator,
        IProfileRepository profileRepository,
        ISignatureResolver signatureResolver,
        ILogger<RuntimeAdapter> logger)
        : this(processLocator, profileRepository, signatureResolver, logger, EmptyServiceProvider.Instance)
    {
    }

    public bool IsAttached => CurrentSession is not null;

    public AttachSession? CurrentSession { get; private set; }

    public Task<AttachSession> AttachAsync(string profileId)
    {
        return AttachAsync(profileId, CancellationToken.None);
    }

    public async Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
    {
        if (IsAttached)
        {
            return CurrentSession!;
        }

        var profileContext = await ResolveAttachProfileContextAsync(profileId, cancellationToken);
        var profile = await _profileRepository.ResolveInheritedProfileAsync(profileContext.ResolvedProfileId, cancellationToken);
        _attachedProfile = profile;
        var process = await SelectProcessForProfileAsync(profile, cancellationToken)
            ?? throw new InvalidOperationException($"{RuntimeReasonCode.ATTACH_NO_PROCESS}: No running process found for target {profile.ExeTarget}");

        process = ApplyDependencyValidation(profile, process);
        var attachPreparation = await PrepareAttachSessionArtifactsAsync(
            profile,
            process,
            profileContext.RequestedProfileId,
            profileContext.VariantResolution,
            cancellationToken);

        _memory = new ProcessMemoryAccessor(attachPreparation.Process.ProcessId);
        ClearCreditsHookState();
        ClearUnitCapHookState();
        ClearInstantBuildHookState();
        CurrentSession = new AttachSession(
            profile.Id,
            attachPreparation.Process,
            attachPreparation.Build,
            attachPreparation.Symbols,
            DateTimeOffset.UtcNow);
        _logger.LogInformation("Attached to process {Pid} for profile {Profile}", attachPreparation.Process.ProcessId, profile.Id);
        return CurrentSession;
    }

    private async Task<AttachProfileContext> ResolveAttachProfileContextAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var requestedProfileId = profileId;
        ProfileVariantResolution? variantResolution = null;
        var resolvedProfileId = profileId;

        if (string.Equals(profileId, ProfileVariantResolver.UniversalProfileId, StringComparison.OrdinalIgnoreCase) &&
            _profileVariantResolver is not null)
        {
            variantResolution = await _profileVariantResolver.ResolveAsync(profileId, cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(variantResolution.ResolvedProfileId))
            {
                _logger.LogInformation(
                    "Resolved universal profile to {ResolvedProfileId} (reason={ReasonCode}, confidence={Confidence:0.00}).",
                    variantResolution.ResolvedProfileId,
                    variantResolution.ReasonCode,
                    variantResolution.Confidence);
                resolvedProfileId = variantResolution.ResolvedProfileId;
            }
        }

        return new AttachProfileContext(requestedProfileId, resolvedProfileId, variantResolution);
    }

    private ProcessMetadata ApplyDependencyValidation(TrainerProfile profile, ProcessMetadata process)
    {
        var dependencyValidation = _modDependencyValidator.Validate(profile, process);
        _dependencyValidationStatus = dependencyValidation.Status;
        _dependencyValidationMessage = dependencyValidation.Message;
        _dependencySoftDisabledActions = dependencyValidation.DisabledActionIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(dependencyValidation.DisabledActionIds, StringComparer.OrdinalIgnoreCase);

        if (dependencyValidation.Status == DependencyValidationStatus.HardFail)
        {
            throw new InvalidOperationException($"{RuntimeReasonCode.ATTACH_PROFILE_MISMATCH}: {dependencyValidation.Message}");
        }

        return AttachDependencyDiagnostics(process, dependencyValidation);
    }

    private async Task<AttachPreparation> PrepareAttachSessionArtifactsAsync(
        TrainerProfile profile,
        ProcessMetadata process,
        string requestedProfileId,
        ProfileVariantResolution? variantResolution,
        CancellationToken cancellationToken)
    {
        var signatureSets = profile.SignatureSets.ToArray();
        if (signatureSets.Length == 0)
        {
            throw new InvalidOperationException($"No signature sets configured for profile '{profile.Id}'");
        }

        var build = new ProfileBuild(
            profile.Id,
            signatureSets[^1].GameBuild,
            process.ProcessPath,
            process.ExeTarget,
            process.CommandLine,
            process.ProcessId);

        InitializeProfileSymbolPolicy(profile);
        var resolvedSymbols = await _signatureResolver.ResolveAsync(build, signatureSets, profile.FallbackOffsets, cancellationToken);
        var symbols = ApplySymbolHealth(profile, process.Mode, resolvedSymbols);
        process = AttachSessionDiagnostics(process, symbols, requestedProfileId, profile.Id, variantResolution);

        var calibrationArtifactPath = TryEmitCalibrationSnapshot(profile, process, build, symbols);
        if (!string.IsNullOrWhiteSpace(calibrationArtifactPath))
        {
            process = AttachMetadataValue(process, "calibrationArtifactPath", calibrationArtifactPath);
        }

        return new AttachPreparation(process, build, symbols);
    }

    private ProcessMetadata AttachSessionDiagnostics(
        ProcessMetadata process,
        SymbolMap symbols,
        string requestedProfileId,
        string resolvedProfileId,
        ProfileVariantResolution? variantResolution)
    {
        var modeProbe = RuntimeModeProbeResolver.Resolve(process.Mode, symbols);
        process = process with { Mode = modeProbe.EffectiveMode };
        process = AttachMetadataValue(process, "runtimeModeHint", modeProbe.HintMode.ToString());
        process = AttachMetadataValue(process, "runtimeModeEffective", modeProbe.EffectiveMode.ToString());
        process = AttachMetadataValue(process, "runtimeModeReasonCode", modeProbe.ReasonCode);
        process = AttachMetadataValue(process, "runtimeModeTacticalSignals", modeProbe.TacticalSignalCount.ToString(CultureInfo.InvariantCulture));
        process = AttachMetadataValue(process, "runtimeModeGalacticSignals", modeProbe.GalacticSignalCount.ToString(CultureInfo.InvariantCulture));
        process = AttachMetadataValue(process, "processSelectionReason", ResolveProcessSelectionReason(process));
        process = AttachMetadataValue(process, "requestedProfileId", requestedProfileId);
        process = AttachMetadataValue(process, "resolvedVariant", resolvedProfileId);

        if (variantResolution is not null)
        {
            process = AttachMetadataValue(process, "resolvedVariantReasonCode", variantResolution.ReasonCode);
            process = AttachMetadataValue(process, "resolvedVariantConfidence", variantResolution.Confidence.ToString("0.00", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(variantResolution.FingerprintId))
            {
                process = AttachMetadataValue(process, "resolvedVariantFingerprintId", variantResolution.FingerprintId!);
            }
        }
        else
        {
            process = AttachMetadataValue(process, "resolvedVariantReasonCode", "explicit_profile_selection");
            process = AttachMetadataValue(process, "resolvedVariantConfidence", "1.00");
        }

        return AttachSymbolHealthDiagnostics(process, symbols);
    }

    private static string ResolveProcessSelectionReason(ProcessMetadata process)
    {
        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("recommendationReason", out var reason) &&
            !string.IsNullOrWhiteSpace(reason))
        {
            return reason;
        }

        return "exe_target_match";
    }

    private async Task<ProcessMetadata?> SelectProcessForProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        var processes = await _processLocator.FindSupportedProcessesAsync(cancellationToken);
        var matches = ResolveInitialProcessMatches(profile, processes);
        if (matches.Length == 0)
        {
            return null;
        }

        var requiredWorkshopIds = CollectRequiredWorkshopIds(profile);
        var pool = ResolveCandidatePool(profile, matches, requiredWorkshopIds);

        if (pool.Length == 1)
        {
            var singleCandidate = CreateProcessSelectionCandidate(profile, pool[0], requiredWorkshopIds);
            return FinalizeProcessSelection(
                singleCandidate.Process,
                singleCandidate.MainModuleSize,
                singleCandidate.WorkshopMatchCount,
                singleCandidate.SelectionScore);
        }

        var ranked = pool
            .Select(candidate => CreateProcessSelectionCandidate(profile, candidate, requiredWorkshopIds))
            .OrderByDescending(x => x.SelectionScore)
            .ThenByDescending(x => x.WorkshopMatchCount)
            .ThenByDescending(x => x.RecommendationMatch)
            .ThenByDescending(x => x.MainModuleSize)
            .ThenByDescending(x => x.HasCommandLine)
            .ToArray();

        var top = ranked[0];
        var selected = FinalizeProcessSelection(
            top.Process,
            top.MainModuleSize,
            top.WorkshopMatchCount,
            top.SelectionScore);

        _logger.LogInformation(
            "Selected process {Pid} ({Name}) for profile {Profile}. Candidates={Count}, hostRole={HostRole}, workshopMatches={WorkshopMatches}, selectionScore={SelectionScore:0.00}, chosenModuleSize=0x{ModuleSize:X}",
            selected.ProcessId,
            selected.ProcessName,
            profile.Id,
            ranked.Length,
            selected.HostRole,
            top.WorkshopMatchCount,
            top.SelectionScore,
            top.MainModuleSize);
        return selected;
    }

    private ProcessMetadata[] ResolveInitialProcessMatches(
        TrainerProfile profile,
        IReadOnlyCollection<ProcessMetadata> processes)
    {
        var matches = processes.Where(x => x.ExeTarget == profile.ExeTarget).ToArray();
        if (matches.Length > 0)
        {
            return matches;
        }

        // StarWarsG.exe can be ambiguous (or misclassified when command-line is unavailable).
        // Prefer profile-aware fallback instead of hard-failing attach.
        matches = processes.Where(IsStarWarsGProcess).ToArray();
        if (matches.Length > 0)
        {
            _logger.LogInformation(
                "No direct process match for target {Target}; using StarWarsG fallback candidates ({Count}).",
                profile.ExeTarget,
                matches.Length);
        }

        return matches;
    }

    private static ProcessMetadata[] ResolveCandidatePool(
        TrainerProfile profile,
        ProcessMetadata[] matches,
        IReadOnlyCollection<string> requiredWorkshopIds)
    {
        var pool = ResolveWorkshopFilteredPool(matches, requiredWorkshopIds);

        var recommendedMatches = pool
            .Where(x => x.LaunchContext?.Recommendation.ProfileId?.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        if (recommendedMatches.Length > 0)
        {
            pool = recommendedMatches;
        }

        return ApplyFoCHostPreference(profile, pool);
    }

    private static ProcessMetadata[] ResolveWorkshopFilteredPool(
        ProcessMetadata[] matches,
        IReadOnlyCollection<string> requiredWorkshopIds)
    {
        if (requiredWorkshopIds.Count == 0)
        {
            return matches;
        }

        var strictWorkshopMatches = matches
            .Where(x => requiredWorkshopIds.All(id => ProcessContainsWorkshopId(x, id)))
            .ToArray();
        if (strictWorkshopMatches.Length > 0)
        {
            return strictWorkshopMatches;
        }

        var looseWorkshopMatches = matches
            .Where(x => requiredWorkshopIds.Any(id => ProcessContainsWorkshopId(x, id)))
            .ToArray();
        return looseWorkshopMatches.Length > 0 ? looseWorkshopMatches : matches;
    }

    private static ProcessMetadata[] ApplyFoCHostPreference(TrainerProfile profile, ProcessMetadata[] pool)
    {
        // For FoC, StarWarsG is typically the real game host while swfoc.exe can be a thin launcher.
        // Prefer StarWarsG whenever both are present.
        var preferStarWarsGHost = !string.Equals(profile.HostPreference, "any", StringComparison.OrdinalIgnoreCase);
        if (profile.ExeTarget != ExeTarget.Swfoc || !preferStarWarsGHost)
        {
            return pool;
        }

        var starWarsGCandidates = pool
            .Where(x => x.HostRole == ProcessHostRole.GameHost || IsStarWarsGProcess(x))
            .ToArray();
        return starWarsGCandidates.Length > 0 ? starWarsGCandidates : pool;
    }

    private static ProcessSelectionCandidate CreateProcessSelectionCandidate(
        TrainerProfile profile,
        ProcessMetadata process,
        IReadOnlyCollection<string> requiredWorkshopIds)
    {
        var workshopMatchCount = requiredWorkshopIds.Count == 0
            ? 0
            : requiredWorkshopIds.Count(id => ProcessContainsWorkshopId(process, id));
        var recommendationMatch = process.LaunchContext?.Recommendation.ProfileId?.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true;
        var mainModuleSize = process.MainModuleSize > 0 ? process.MainModuleSize : TryGetMainModuleSize(process.ProcessId);
        var hasCommandLine = !string.IsNullOrWhiteSpace(process.CommandLine);
        var hostRoleScore = process.HostRole switch
        {
            ProcessHostRole.GameHost => 2,
            ProcessHostRole.Launcher => 1,
            _ => 0
        };
        var selectionScore = ComputeSelectionScore(workshopMatchCount, recommendationMatch, hostRoleScore, hasCommandLine, mainModuleSize);
        return new ProcessSelectionCandidate(
            process,
            workshopMatchCount,
            recommendationMatch,
            mainModuleSize,
            hasCommandLine,
            selectionScore);
    }

    private static double ComputeSelectionScore(
        int workshopMatchCount,
        bool recommendationMatch,
        int hostRoleScore,
        bool hasCommandLine,
        int mainModuleSize)
    {
        return
            (workshopMatchCount * 1000d) +
            (recommendationMatch ? 300d : 0d) +
            (hostRoleScore * 100d) +
            (hasCommandLine ? 10d : 0d) +
            (mainModuleSize / 1_000_000d);
    }

    private static ProcessMetadata FinalizeProcessSelection(
        ProcessMetadata selected,
        int mainModuleSize,
        int workshopMatchCount,
        double selectionScore)
    {
        selected = selected with
        {
            MainModuleSize = mainModuleSize,
            WorkshopMatchCount = workshopMatchCount,
            SelectionScore = selectionScore
        };
        selected = AttachMetadataValue(selected, "hostRole", selected.HostRole.ToString().ToLowerInvariant());
        selected = AttachMetadataValue(selected, "mainModuleSize", selected.MainModuleSize.ToString(CultureInfo.InvariantCulture));
        selected = AttachMetadataValue(selected, "workshopMatchCount", selected.WorkshopMatchCount.ToString(CultureInfo.InvariantCulture));
        selected = AttachMetadataValue(selected, "selectionScore", selected.SelectionScore.ToString("0.00", CultureInfo.InvariantCulture));
        return selected;
    }

    private readonly record struct AttachProfileContext(
        string RequestedProfileId,
        string ResolvedProfileId,
        ProfileVariantResolution? VariantResolution);

    private readonly record struct AttachPreparation(
        ProcessMetadata Process,
        ProfileBuild Build,
        SymbolMap Symbols);

    private readonly record struct ProcessSelectionCandidate(
        ProcessMetadata Process,
        int WorkshopMatchCount,
        bool RecommendationMatch,
        int MainModuleSize,
        bool HasCommandLine,
        double SelectionScore);

    private static T? ResolveOptionalService<T>(IServiceProvider? serviceProvider)
        where T : class
    {
        return serviceProvider?.GetService(typeof(T)) as T;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private static HashSet<string> CollectRequiredWorkshopIds(TrainerProfile profile)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.SteamWorkshopId))
        {
            ids.Add(profile.SteamWorkshopId);
        }

        if (profile.Metadata is not null &&
            profile.Metadata.TryGetValue("requiredWorkshopIds", out var requiredIds) &&
            !string.IsNullOrWhiteSpace(requiredIds))
        {
            foreach (var id in requiredIds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                ids.Add(id);
            }
        }

        if (profile.Metadata is not null &&
            profile.Metadata.TryGetValue("requiredWorkshopId", out var legacyRequiredId) &&
            !string.IsNullOrWhiteSpace(legacyRequiredId))
        {
            foreach (var id in legacyRequiredId.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static int TryGetMainModuleSize(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.ModuleMemorySize ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private ProcessMetadata AttachDependencyDiagnostics(ProcessMetadata process, DependencyValidationResult validation)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (process.Metadata is not null)
        {
            foreach (var kv in process.Metadata)
            {
                metadata[kv.Key] = kv.Value;
            }
        }

        metadata["dependencyValidation"] = validation.Status.ToString();
        metadata["dependencyValidationMessage"] = validation.Message;
        metadata["dependencyDisabledActions"] = validation.DisabledActionIds.Count == 0
            ? string.Empty
            : string.Join(",", validation.DisabledActionIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        return process with { Metadata = metadata };
    }

    private static ProcessMetadata AttachMetadataValue(ProcessMetadata process, string key, string value)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (process.Metadata is not null)
        {
            foreach (var kv in process.Metadata)
            {
                metadata[kv.Key] = kv.Value;
            }
        }

        metadata[key] = value;
        return process with { Metadata = metadata };
    }

    private void InitializeProfileSymbolPolicy(TrainerProfile profile)
    {
        _symbolValidationRules = ParseSymbolValidationRules(profile)
            .Where(x => !string.IsNullOrWhiteSpace(x.Symbol))
            .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.Mode.HasValue)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        _criticalSymbols = ParseCriticalSymbols(profile);
    }

    private SymbolMap ApplySymbolHealth(TrainerProfile profile, RuntimeMode mode, SymbolMap symbols)
    {
        var updated = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, symbol) in symbols.Symbols)
        {
            var evaluation = _symbolHealthService.Evaluate(symbol, profile, mode);
            var baseConfidence = symbol.Confidence > 0 ? symbol.Confidence : evaluation.Confidence;
            var confidence = evaluation.Status == SymbolHealthStatus.Healthy
                ? ClampConfidence(Math.Max(baseConfidence, evaluation.Confidence))
                : ClampConfidence(Math.Min(baseConfidence, evaluation.Confidence));
            var mergedReason = string.IsNullOrWhiteSpace(symbol.HealthReason)
                ? evaluation.Reason
                : $"{symbol.HealthReason}+{evaluation.Reason}";
            updated[name] = symbol with
            {
                Confidence = confidence,
                HealthStatus = evaluation.Status,
                HealthReason = mergedReason,
                LastValidatedAt = DateTimeOffset.UtcNow
            };
        }

        return new SymbolMap(updated);
    }

    private string? TryEmitCalibrationSnapshot(
        TrainerProfile profile,
        ProcessMetadata process,
        ProfileBuild build,
        SymbolMap symbols)
    {
        try
        {
            Directory.CreateDirectory(_calibrationArtifactRoot);
            var generatedAtUtc = DateTimeOffset.UtcNow;
            var filePath = BuildCalibrationSnapshotPath(profile.Id, generatedAtUtc);
            var payload = BuildCalibrationSnapshotPayload(profile, process, build, symbols, generatedAtUtc);

            File.WriteAllText(filePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit calibration snapshot.");
            return null;
        }
    }

    private string BuildCalibrationSnapshotPath(string profileId, DateTimeOffset generatedAtUtc)
    {
        var safeTimestamp = generatedAtUtc.ToString("yyyyMMdd_HHmmss");
        var safeProfile = profileId.Replace('/', '_').Replace('\\', '_');
        return Path.Combine(_calibrationArtifactRoot, $"attach_{safeProfile}_{safeTimestamp}.json");
    }

    private object BuildCalibrationSnapshotPayload(
        TrainerProfile profile,
        ProcessMetadata process,
        ProfileBuild build,
        SymbolMap symbols,
        DateTimeOffset generatedAtUtc)
    {
        return new
        {
            schemaVersion = "1.0",
            generatedAtUtc,
            trigger = "attach",
            profile = new
            {
                id = profile.Id,
                exeTarget = profile.ExeTarget.ToString()
            },
            process = BuildCalibrationSnapshotProcessPayload(process),
            moduleFingerprint = BuildCalibrationSnapshotModuleFingerprint(process.ProcessPath),
            build = new
            {
                build.ProfileId,
                build.GameBuild,
                build.ExecutablePath,
                build.ExeTarget,
                build.ProcessId
            },
            symbolPolicy = BuildCalibrationSnapshotSymbolPolicyPayload(),
            symbols = BuildCalibrationSnapshotSymbolsPayload(symbols)
        };
    }

    private static object BuildCalibrationSnapshotProcessPayload(ProcessMetadata process)
    {
        return new
        {
            process.ProcessId,
            process.ProcessName,
            process.ProcessPath,
            process.CommandLine,
            launchContext = process.LaunchContext
        };
    }

    private object BuildCalibrationSnapshotModuleFingerprint(string processPath)
    {
        var moduleHash = ComputeFileSha256(processPath);
        return new
        {
            path = processPath,
            sha256 = moduleHash,
            sizeBytes = TryGetFileSize(processPath),
            lastWriteUtc = TryGetLastWriteUtc(processPath)
        };
    }

    private object BuildCalibrationSnapshotSymbolPolicyPayload()
    {
        return new
        {
            criticalSymbols = _criticalSymbols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            validationRules = _symbolValidationRules.Values
                .SelectMany(x => x)
                .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static object[] BuildCalibrationSnapshotSymbolsPayload(SymbolMap symbols)
    {
        return symbols.Symbols
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                name = x.Key,
                address = $"0x{x.Value.Address.ToInt64():X}",
                valueType = x.Value.ValueType.ToString(),
                source = x.Value.Source.ToString(),
                diagnostics = x.Value.Diagnostics,
                confidence = x.Value.Confidence,
                healthStatus = x.Value.HealthStatus.ToString(),
                healthReason = x.Value.HealthReason,
                lastValidatedAt = x.Value.LastValidatedAt
            })
            .ToArray<object>();
    }

    private static IReadOnlyList<SymbolValidationRule> ParseSymbolValidationRules(TrainerProfile profile)
    {
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue("symbolValidationRules", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<SymbolValidationRule>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<SymbolValidationRule>>(raw, SymbolValidationJsonOptions);
            return parsed is not null
                ? parsed
                : Array.Empty<SymbolValidationRule>();
        }
        catch
        {
            return Array.Empty<SymbolValidationRule>();
        }
    }

    private static HashSet<string> ParseCriticalSymbols(TrainerProfile profile)
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue("criticalSymbols", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return symbols;
        }

        foreach (var symbol in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            symbols.Add(symbol);
        }

        return symbols;
    }

    private static long? TryGetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetLastWriteUtc(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var utc = File.GetLastWriteTimeUtc(path);
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static string? ComputeFileSha256(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static double ClampConfidence(double value)
    {
        if (double.IsNaN(value))
        {
            return 0d;
        }

        return Math.Max(0d, Math.Min(1d, value));
    }

    private ProcessMetadata AttachSymbolHealthDiagnostics(ProcessMetadata process, SymbolMap symbols)
    {
        var total = symbols.Symbols.Count;
        var healthy = symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Healthy);
        var degraded = symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Degraded);
        var unresolved = symbols.Symbols.Values.Count(x =>
            x.HealthStatus == SymbolHealthStatus.Unresolved || x.Address == nint.Zero);
        var fallback = symbols.Symbols.Values.Count(x => x.Source == AddressSource.Fallback);
        var fallbackRate = total == 0 ? 0d : (double)fallback / total;
        var unresolvedRate = total == 0 ? 0d : (double)unresolved / total;

        var withMetadata = AttachMetadataValue(process, "symbolTotal", total.ToString());
        withMetadata = AttachMetadataValue(withMetadata, "symbolHealthy", healthy.ToString());
        withMetadata = AttachMetadataValue(withMetadata, "symbolDegraded", degraded.ToString());
        withMetadata = AttachMetadataValue(withMetadata, "symbolUnresolved", unresolved.ToString());
        withMetadata = AttachMetadataValue(withMetadata, "fallbackHitRate", fallbackRate.ToString("0.000"));
        withMetadata = AttachMetadataValue(withMetadata, "unresolvedSymbolRate", unresolvedRate.ToString("0.000"));

        _logger.LogInformation(
            "Symbol health summary profile={ProfileId} pid={Pid} total={Total} healthy={Healthy} degraded={Degraded} unresolved={Unresolved} fallbackRate={FallbackRate:0.000} unresolvedRate={UnresolvedRate:0.000}",
            _attachedProfile?.Id ?? "<pending>",
            process.ProcessId,
            total,
            healthy,
            degraded,
            unresolved,
            fallbackRate,
            unresolvedRate);

        return withMetadata;
    }

    private static bool IsStarWarsGProcess(ProcessMetadata process)
    {
        if (process.ProcessName.Equals("StarWarsG", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("StarWarsG.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("isStarWarsG", out var isStarWarsG) &&
            bool.TryParse(isStarWarsG, out var parsed))
        {
            return parsed;
        }

        return process.ProcessPath.Contains("StarWarsG.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ProcessContainsWorkshopId(ProcessMetadata process, string workshopId)
    {
        if (process.CommandLine?.Contains(workshopId, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("steamModIdsDetected", out var ids) &&
            !string.IsNullOrWhiteSpace(ids))
        {
            return ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(x => x.Equals(workshopId, StringComparison.OrdinalIgnoreCase));
        }

        if (process.LaunchContext is not null)
        {
            return process.LaunchContext.SteamModIds
                .Any(x => x.Equals(workshopId, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    public Task<T> ReadAsync<T>(string symbol) where T : unmanaged
    {
        return ReadAsync<T>(symbol, CancellationToken.None);
    }

    public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
    {
        EnsureAttached();
        var sym = ResolveSymbol(symbol);
        var value = _memory!.Read<T>(sym.Address);
        return Task.FromResult(value);
    }

    public Task WriteAsync<T>(string symbol, T value) where T : unmanaged
    {
        return WriteAsync(symbol, value, CancellationToken.None);
    }

    public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
    {
        EnsureAttached();
        var sym = ResolveSymbol(symbol);
        _memory!.Write(sym.Address, value);
        return Task.CompletedTask;
    }

    public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request)
    {
        return ExecuteAsync(request, CancellationToken.None);
    }

    public async Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        EnsureAttached();
        if (TryCreateDependencyDisabledResult(request, out var dependencyDisabled))
        {
            return dependencyDisabled;
        }

        var attachedProfile = _attachedProfile
            ?? await _profileRepository.ResolveInheritedProfileAsync(request.ProfileId, cancellationToken);
        var capabilityReport = await ProbeCapabilitiesAsync(attachedProfile, cancellationToken);
        var routeDecision = _backendRouter.Resolve(request, attachedProfile, CurrentSession!.Process, capabilityReport);
        if (!routeDecision.Allowed)
        {
            var overrideResult = await TryExecuteExpertMutationOverrideAsync(
                request,
                routeDecision,
                capabilityReport,
                cancellationToken);
            if (overrideResult is not null)
            {
                RecordActionTelemetry(request, overrideResult);
                return overrideResult;
            }

            var blocked = CreateBlockedRouteResult(routeDecision, capabilityReport);
            RecordActionTelemetry(request, blocked);
            return blocked;
        }

        try
        {
            var result = await ExecuteByRouteAsync(routeDecision, request, capabilityReport, cancellationToken);
            result = ApplyBackendRouteDiagnostics(result, routeDecision, capabilityReport);
            RecordActionTelemetry(request, result);
            return result;
        }
        catch (Exception ex)
        {
            var failed = CreateExecutionExceptionResult(request, routeDecision, capabilityReport, ex);
            RecordActionTelemetry(request, failed);
            return failed;
        }
    }

    private bool TryCreateDependencyDisabledResult(
        ActionExecutionRequest request,
        out ActionExecutionResult result)
    {
        if (!_dependencySoftDisabledActions.Contains(request.Action.Id))
        {
            result = default!;
            return false;
        }

        result = new ActionExecutionResult(
            false,
            _dependencyValidationStatus == DependencyValidationStatus.SoftFail
                ? $"Action '{request.Action.Id}' is disabled due to dependency verification soft-fail. {_dependencyValidationMessage}"
                : $"Action '{request.Action.Id}' is disabled for the current attachment.",
            AddressSource.None,
            new Dictionary<string, object?>
            {
                ["dependencyValidation"] = _dependencyValidationStatus.ToString(),
                ["dependencyValidationMessage"] = _dependencyValidationMessage,
                ["disabledActionId"] = request.Action.Id
            });
        return true;
    }

    private static ActionExecutionResult CreateBlockedRouteResult(
        BackendRouteDecision routeDecision,
        CapabilityReport capabilityReport)
    {
        var blocked = new ActionExecutionResult(
            false,
            routeDecision.Message,
            AddressSource.None,
            MergeDiagnostics(
                routeDecision.Diagnostics,
                new Dictionary<string, object?>
                {
                    ["reasonCode"] = routeDecision.ReasonCode.ToString()
                }));
        return ApplyBackendRouteDiagnostics(blocked, routeDecision, capabilityReport);
    }

    private async Task<ActionExecutionResult> ExecuteByRouteAsync(
        BackendRouteDecision routeDecision,
        ActionExecutionRequest request,
        CapabilityReport capabilityReport,
        CancellationToken cancellationToken)
    {
        return routeDecision.Backend switch
        {
            ExecutionBackendKind.Extender => await ExecuteExtenderBackendActionAsync(request, capabilityReport, cancellationToken),
            ExecutionBackendKind.Helper => await ExecuteHelperActionAsync(request, cancellationToken),
            ExecutionBackendKind.Save => await ExecuteSaveActionAsync(request, cancellationToken),
            ExecutionBackendKind.Memory => await ExecuteLegacyBackendActionAsync(request, cancellationToken),
            _ => new ActionExecutionResult(false, "Unsupported execution backend.", AddressSource.None)
        };
    }

    private ActionExecutionResult CreateExecutionExceptionResult(
        ActionExecutionRequest request,
        BackendRouteDecision routeDecision,
        CapabilityReport capabilityReport,
        Exception ex)
    {
        _logger.LogError(ex, "Action execution failed for {Action}", request.Action.Id);
        var failed = new ActionExecutionResult(
            false,
            ex.Message,
            AddressSource.None,
            MergeDiagnostics(
                routeDecision.Diagnostics,
                new Dictionary<string, object?>
                {
                    ["failureReasonCode"] = "action_exception",
                    ["exceptionType"] = ex.GetType().Name
                }));
        return ApplyBackendRouteDiagnostics(failed, routeDecision, capabilityReport);
    }

    private async Task<CapabilityReport> ProbeCapabilitiesAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        if (_extenderBackend is null || CurrentSession is null)
        {
            return CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE);
        }

        var report = await _extenderBackend.ProbeCapabilitiesAsync(profile.Id, CurrentSession.Process, cancellationToken);
        if (report.Capabilities.Count > 0)
        {
            return report;
        }

        // Expose SDK execution catalog IDs as unknown capabilities so router diagnostics are explicit.
        var inferredCapabilities = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase);
        foreach (var featureId in profile.RequiredCapabilities ?? Array.Empty<string>())
        {
            inferredCapabilities[featureId] = new BackendCapability(
                featureId,
                Available: false,
                Confidence: CapabilityConfidenceState.Unknown,
                ReasonCode: RuntimeReasonCode.CAPABILITY_UNKNOWN,
                Notes: "Capability not confirmed by extender probe.");
        }

        return report with { Capabilities = inferredCapabilities };
    }

    private async Task<ActionExecutionResult> ExecuteLegacyBackendActionAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        return request.Action.ExecutionKind switch
        {
            ExecutionKind.Memory => await ExecuteMemoryActionAsync(request, cancellationToken),
            ExecutionKind.Helper => await ExecuteHelperActionAsync(request, cancellationToken),
            ExecutionKind.Save => await ExecuteSaveActionAsync(request, cancellationToken),
            ExecutionKind.CodePatch => await ExecuteCodePatchActionAsync(request, cancellationToken),
            ExecutionKind.Freeze => new ActionExecutionResult(false, "Freeze actions must be handled by the orchestrator, not the runtime adapter.", AddressSource.None),
            ExecutionKind.Sdk => await ExecuteSdkActionAsync(request, cancellationToken),
            _ => new ActionExecutionResult(false, "Unsupported execution kind", AddressSource.None)
        };
    }

    private async Task<ActionExecutionResult> ExecuteExtenderBackendActionAsync(
        ActionExecutionRequest request,
        CapabilityReport capabilityReport,
        CancellationToken cancellationToken)
    {
        if (_extenderBackend is null)
        {
            return new ActionExecutionResult(
                false,
                "Extender backend was selected but is not configured.",
                AddressSource.None,
                new Dictionary<string, object?>
                {
                    ["reasonCode"] = RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE.ToString(),
                    ["backendRoute"] = ExecutionBackendKind.Extender.ToString()
                });
        }

        var extenderRequest = request with
        {
            Context = BuildExtenderContext(request)
        };

        return await _extenderBackend.ExecuteAsync(extenderRequest, capabilityReport, cancellationToken);
    }

    private static ActionExecutionResult ApplyBackendRouteDiagnostics(
        ActionExecutionResult result,
        BackendRouteDecision routeDecision,
        CapabilityReport capabilityReport)
    {
        var baseDiagnostics = MergeDiagnostics(routeDecision.Diagnostics, result.Diagnostics);
        var overrideState = ResolveExpertMutationOverrideState();
        var configuredOverrideEnabled = IsEnabledEnvironmentFlag(ExpertOverrideEnvVarName);
        var hybridExecution = ResolveHybridExecutionFlag(baseDiagnostics);
        var backend = ResolveBackendDiagnosticValue(baseDiagnostics, routeDecision.Backend);
        var hookState = ResolveHookStateDiagnosticValue(baseDiagnostics, capabilityReport.Diagnostics);
        var expertOverrideEnabled = ResolveExpertOverrideEnabledDiagnosticValue(baseDiagnostics, configuredOverrideEnabled);
        var panicDisableState = ResolvePanicDisableStateDiagnosticValue(baseDiagnostics, overrideState.PanicDisableState);
        var overrideReason = ResolveOverrideReasonDiagnosticValue(baseDiagnostics, "none");
        var diagnostics = MergeDiagnostics(
            baseDiagnostics,
            new Dictionary<string, object?>
            {
                ["backend"] = backend,
                ["backendRoute"] = routeDecision.Backend.ToString(),
                ["routeReasonCode"] = routeDecision.ReasonCode.ToString(),
                ["capabilityProbeReasonCode"] = capabilityReport.ProbeReasonCode.ToString(),
                [DiagnosticKeyHookState] = hookState,
                ["hybridExecution"] = hybridExecution,
                ["capabilityCount"] = capabilityReport.Capabilities.Count,
                [DiagnosticKeyExpertOverrideEnabled] = expertOverrideEnabled,
                [DiagnosticKeyOverrideReason] = overrideReason,
                [DiagnosticKeyPanicDisableState] = panicDisableState
            });
        return result with { Diagnostics = diagnostics };
    }

    private static bool ResolveHybridExecutionFlag(IReadOnlyDictionary<string, object?>? diagnostics)
    {
        if (!TryReadDiagnosticString(diagnostics, "hybridExecution", out var hybridRaw) ||
            string.IsNullOrWhiteSpace(hybridRaw))
        {
            return false;
        }

        return bool.TryParse(hybridRaw, out var parsed) && parsed;
    }

    private static string ResolveBackendDiagnosticValue(
        IReadOnlyDictionary<string, object?>? diagnostics,
        ExecutionBackendKind routeBackend)
    {
        if (TryReadDiagnosticString(diagnostics, "backend", out var backend) &&
            !string.IsNullOrWhiteSpace(backend))
        {
            return backend!;
        }

        return routeBackend.ToString();
    }

    private static string ResolveHookStateDiagnosticValue(
        IReadOnlyDictionary<string, object?>? resultDiagnostics,
        IReadOnlyDictionary<string, object?>? capabilityDiagnostics)
    {
        if (TryResolveFirstDiagnosticValue(resultDiagnostics, ResultHookStateKeys, out var hookState))
        {
            return hookState!;
        }

        if (TryResolveFirstDiagnosticValue(capabilityDiagnostics, [DiagnosticKeyHookState], out var probeHookState))
        {
            return probeHookState!;
        }

        return "unknown";
    }

    private static bool ResolveExpertOverrideEnabledDiagnosticValue(
        IReadOnlyDictionary<string, object?>? diagnostics,
        bool defaultValue)
    {
        if (!TryReadDiagnosticString(diagnostics, DiagnosticKeyExpertOverrideEnabled, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static string ResolveOverrideReasonDiagnosticValue(
        IReadOnlyDictionary<string, object?>? diagnostics,
        string defaultValue)
    {
        if (TryReadDiagnosticString(diagnostics, DiagnosticKeyOverrideReason, out var reason) &&
            !string.IsNullOrWhiteSpace(reason))
        {
            return reason!;
        }

        return defaultValue;
    }

    private static string ResolvePanicDisableStateDiagnosticValue(
        IReadOnlyDictionary<string, object?>? diagnostics,
        string defaultValue)
    {
        if (TryReadDiagnosticString(diagnostics, DiagnosticKeyPanicDisableState, out var state) &&
            !string.IsNullOrWhiteSpace(state))
        {
            return state!;
        }

        return defaultValue;
    }

    private static bool TryResolveFirstDiagnosticValue(
        IReadOnlyDictionary<string, object?>? diagnostics,
        IReadOnlyList<string> keys,
        out string? value)
    {
        value = null;
        foreach (var key in keys)
        {
            if (TryReadDiagnosticString(diagnostics, key, out var resolved) &&
                !string.IsNullOrWhiteSpace(resolved))
            {
                value = resolved;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDiagnosticString(
        IReadOnlyDictionary<string, object?>? diagnostics,
        string key,
        out string? value)
    {
        value = null;
        if (diagnostics is null || !diagnostics.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        value = raw.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsPromotedExtenderAction(string actionId)
    {
        return !string.IsNullOrWhiteSpace(actionId) &&
               PromotedExtenderActionIds.Contains(actionId);
    }

    private static ExpertMutationOverrideState ResolveExpertMutationOverrideState()
    {
        var panicDisableActive = IsEnabledEnvironmentFlag(ExpertOverridePanicEnvVarName);
        if (panicDisableActive)
        {
            return new ExpertMutationOverrideState(
                Enabled: false,
                PanicDisableState: PanicDisableStateActive,
                DefaultReason: $"Expert mutation override panic-disable is active via {ExpertOverridePanicEnvVarName}.");
        }

        var overrideEnabled = IsEnabledEnvironmentFlag(ExpertOverrideEnvVarName);
        if (overrideEnabled)
        {
            return new ExpertMutationOverrideState(
                Enabled: true,
                PanicDisableState: PanicDisableStateInactive,
                DefaultReason: $"Expert mutation override enabled via {ExpertOverrideEnvVarName}.");
        }

        return new ExpertMutationOverrideState(
            Enabled: false,
            PanicDisableState: PanicDisableStateInactive,
            DefaultReason: "Expert mutation override disabled by default (fail-closed).");
    }

    private static bool IsEnabledEnvironmentFlag(string variableName)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, object?>? MergeDiagnostics(
        IReadOnlyDictionary<string, object?>? primary,
        IReadOnlyDictionary<string, object?>? secondary)
    {
        if ((primary is null || primary.Count == 0) &&
            (secondary is null || secondary.Count == 0))
        {
            return primary;
        }

        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (primary is not null)
        {
            foreach (var kv in primary)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        if (secondary is not null)
        {
            foreach (var kv in secondary)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }

    private readonly record struct ExpertMutationOverrideState(
        bool Enabled,
        string PanicDisableState,
        string DefaultReason);

    public Task DetachAsync()
    {
        return DetachAsync(CancellationToken.None);
    }

    public Task DetachAsync(CancellationToken cancellationToken)
    {
        TryRestoreCodePatchesOnDetach();
        TryRestoreCreditsHookOnDetach();
        TryRestoreUnitCapHookOnDetach();
        TryRestoreInstantBuildHookOnDetach();
        _memory?.Dispose();
        _memory = null;
        ClearCreditsHookState();
        ClearUnitCapHookState();
        ClearInstantBuildHookState();
        _dependencySoftDisabledActions.Clear();
        _dependencyValidationStatus = DependencyValidationStatus.Pass;
        _dependencyValidationMessage = null;
        _attachedProfile = null;
        _symbolValidationRules.Clear();
        _criticalSymbols.Clear();
        CurrentSession = null;
        return Task.CompletedTask;
    }

    private async Task<ActionExecutionResult> ExecuteSdkActionAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        if (_sdkOperationRouter is null)
        {
            return new ActionExecutionResult(
                false,
                "SDK operation routing is not configured in this runtime.",
                AddressSource.None,
                new Dictionary<string, object?>
                {
                    ["failureReasonCode"] = "sdk_router_missing"
                });
        }

        var sdkRequest = new SdkOperationRequest(
            OperationId: request.Action.Id,
            Payload: request.Payload,
            IsMutation: IsMutatingSdkOperation(request.Action.Id),
            RuntimeMode: request.RuntimeMode,
            ProfileId: request.ProfileId,
            Context: BuildSdkContext(request.Context));

        var sdkResult = await _sdkOperationRouter.ExecuteAsync(sdkRequest, cancellationToken);
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sdkReasonCode"] = sdkResult.ReasonCode.ToString(),
            ["sdkCapabilityState"] = sdkResult.CapabilityState.ToString()
        };

        if (sdkResult.Diagnostics is not null)
        {
            foreach (var kv in sdkResult.Diagnostics)
            {
                diagnostics[kv.Key] = kv.Value;
            }
        }

        return new ActionExecutionResult(
            sdkResult.Succeeded,
            sdkResult.Message,
            AddressSource.None,
            diagnostics);
    }

    private async Task<ActionExecutionResult?> TryExecuteExpertMutationOverrideAsync(
        ActionExecutionRequest request,
        BackendRouteDecision routeDecision,
        CapabilityReport capabilityReport,
        CancellationToken cancellationToken)
    {
        if (!IsEligibleForExpertMutationOverride(request, routeDecision))
        {
            return null;
        }

        var overrideState = ResolveExpertMutationOverrideState();
        if (!overrideState.Enabled)
        {
            return null;
        }

        var riskyBackend = ResolveLegacyOverrideBackend(request.Action.ExecutionKind);
        var legacyResult = await ExecuteLegacyBackendActionAsync(request, cancellationToken);
        var overrideReason =
            $"Expert override executed risky legacy backend '{riskyBackend}' for blocked promoted mutating action '{request.Action.Id}' (routeReason={routeDecision.ReasonCode}).";
        var diagnostics = MergeDiagnostics(
            legacyResult.Diagnostics,
            new Dictionary<string, object?>
            {
                ["backend"] = riskyBackend.ToString(),
                ["overrideBackend"] = riskyBackend.ToString(),
                [DiagnosticKeyExpertOverrideEnabled] = true,
                [DiagnosticKeyPanicDisableState] = overrideState.PanicDisableState,
                [DiagnosticKeyOverrideReason] = overrideReason,
                ["riskyOverride"] = true,
                ["riskyAction"] = true
            });
        var message = string.IsNullOrWhiteSpace(legacyResult.Message)
            ? "Expert override executed risky legacy backend path."
            : $"{legacyResult.Message} Expert override executed risky legacy backend path.";
        var overridden = legacyResult with
        {
            Message = message,
            Diagnostics = diagnostics
        };
        return ApplyBackendRouteDiagnostics(overridden, routeDecision, capabilityReport);
    }

    private static bool IsEligibleForExpertMutationOverride(
        ActionExecutionRequest request,
        BackendRouteDecision routeDecision)
    {
        return !routeDecision.Allowed &&
               routeDecision.Backend == ExecutionBackendKind.Extender &&
               IsPromotedExtenderAction(request.Action.Id) &&
               IsMutatingActionId(request.Action.Id);
    }

    private static ExecutionBackendKind ResolveLegacyOverrideBackend(ExecutionKind executionKind)
    {
        return executionKind switch
        {
            ExecutionKind.Helper => ExecutionBackendKind.Helper,
            ExecutionKind.Save => ExecutionBackendKind.Save,
            _ => ExecutionBackendKind.Memory
        };
    }

    private static bool IsMutatingActionId(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return true;
        }

        return !(actionId.StartsWith("read_", StringComparison.OrdinalIgnoreCase) ||
                 actionId.StartsWith("list_", StringComparison.OrdinalIgnoreCase) ||
                 actionId.StartsWith("get_", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMutatingSdkOperation(string operationId)
    {
        if (SdkOperationCatalog.TryGet(operationId, out var definition))
        {
            return definition.IsMutation;
        }

        return !operationId.StartsWith("list_", StringComparison.OrdinalIgnoreCase) &&
               !operationId.StartsWith("read_", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyDictionary<string, object?> BuildSdkContext(IReadOnlyDictionary<string, object?>? context)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (context is not null)
        {
            foreach (var kv in context)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        if (CurrentSession is not null)
        {
            merged["processId"] = CurrentSession.Process.ProcessId;
            merged["processPath"] = CurrentSession.Process.ProcessPath;
        }

        return merged;
    }

    private IReadOnlyDictionary<string, object?> BuildExtenderContext(ActionExecutionRequest request)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (request.Context is not null)
        {
            foreach (var kv in request.Context)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        if (CurrentSession is not null)
        {
            merged["processId"] = CurrentSession.Process.ProcessId;
            merged["processName"] = CurrentSession.Process.ProcessName;
            merged["processPath"] = CurrentSession.Process.ProcessPath;
        }

        var resolvedAnchors = BuildResolvedAnchors(request);
        if (resolvedAnchors.Count > 0)
        {
            merged["resolvedAnchors"] = resolvedAnchors;
        }

        return merged;
    }

    private Dictionary<string, string> BuildResolvedAnchors(ActionExecutionRequest request)
    {
        var anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MergeAnchorsFromRequestContext(request, anchors);
        TryAddPayloadSymbolAnchor(request.Payload, anchors);
        AddPromotedActionAnchors(request.Action.Id, anchors);
        AddActiveHookAnchors(anchors);
        return anchors;
    }

    private void MergeAnchorsFromRequestContext(ActionExecutionRequest request, IDictionary<string, string> anchors)
    {
        if (TryReadContextValue(request.Context, "resolvedAnchors", out var existingResolved))
        {
            MergeAnchorMap(anchors, existingResolved);
            return;
        }

        if (TryReadContextValue(request.Context, "anchors", out var existingLegacy))
        {
            MergeAnchorMap(anchors, existingLegacy);
        }
    }

    private void TryAddPayloadSymbolAnchor(JsonObject payload, IDictionary<string, string> anchors)
    {
        if (TryReadPayloadString(payload, "symbol", out var payloadSymbol))
        {
            TryAddResolvedSymbolAnchor(anchors, payloadSymbol!);
        }
    }

    private void AddPromotedActionAnchors(string actionId, IDictionary<string, string> anchors)
    {
        if (!IsPromotedExtenderAction(actionId))
        {
            return;
        }

        foreach (var alias in ResolvePromotedAnchorAliases(actionId))
        {
            TryAddResolvedSymbolAnchor(anchors, alias);
        }
    }

    private void AddActiveHookAnchors(IDictionary<string, string> anchors)
    {
        AddAddressAnchorIfAvailable(anchors, "credits_hook_injection", _creditsHookInjectionAddress);
        AddAddressAnchorIfAvailable(anchors, "unit_cap_hook_injection", _unitCapHookInjectionAddress);
        AddAddressAnchorIfAvailable(anchors, "unit_cap_hook_cave", _unitCapHookCodeCaveAddress);
        AddAddressAnchorIfAvailable(anchors, "unit_cap_hook_value", _unitCapHookValueAddress);
        AddAddressAnchorIfAvailable(anchors, "instant_build_patch_injection", _instantBuildHookInjectionAddress);
        AddAddressAnchorIfAvailable(anchors, "instant_build_patch_cave", _instantBuildHookCodeCaveAddress);
    }

    private static void AddAddressAnchorIfAvailable(IDictionary<string, string> anchors, string key, nint address)
    {
        if (address != nint.Zero)
        {
            anchors[key] = ToHex(address);
        }
    }

    private static string[] ResolvePromotedAnchorAliases(string actionId)
    {
        return actionId switch
        {
            "freeze_timer" => ["game_timer_freeze", "freeze_timer"],
            "toggle_fog_reveal" => ["fog_reveal", "toggle_fog_reveal"],
            "toggle_ai" => ["ai_enabled", "toggle_ai"],
            ActionIdSetUnitCap => ["unit_cap", ActionIdSetUnitCap],
            ActionIdToggleInstantBuildPatch => ["instant_build_patch", ActionIdToggleInstantBuildPatch],
            ActionIdSetCredits => [SymbolCredits, ActionIdSetCredits],
            _ => Array.Empty<string>()
        };
    }

    private void TryAddResolvedSymbolAnchor(IDictionary<string, string> anchors, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || anchors.ContainsKey(symbol))
        {
            return;
        }

        if (!TryResolveSessionSymbolAddress(symbol, out var address))
        {
            return;
        }

        anchors[symbol] = ToHex(address);
    }

    private bool TryResolveSessionSymbolAddress(string symbol, out nint address)
    {
        address = nint.Zero;
        if (CurrentSession is null)
        {
            return false;
        }

        if (!CurrentSession.Symbols.Symbols.TryGetValue(symbol, out var info) ||
            info.Address == nint.Zero)
        {
            return false;
        }

        address = info.Address;
        return true;
    }

    private static bool TryReadPayloadString(JsonObject payload, string key, out string? value)
    {
        value = null;
        if (!payload.TryGetPropertyValue(key, out var node) || node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<string>();
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadContextValue(
        IReadOnlyDictionary<string, object?>? context,
        string key,
        out object? value)
    {
        value = null;
        if (context is null)
        {
            return false;
        }

        if (!context.TryGetValue(key, out var raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    private static void MergeAnchorMap(IDictionary<string, string> destination, object? raw)
    {
        if (raw is null)
        {
            return;
        }

        if (TryMergeAnchorJsonObject(destination, raw) ||
            TryMergeAnchorJsonElement(destination, raw) ||
            TryMergeAnchorObjectDictionary(destination, raw) ||
            TryMergeAnchorStringPairs(destination, raw))
        {
            return;
        }

        TryMergeSerializedAnchorMap(destination, raw);
    }

    private static bool TryMergeAnchorJsonObject(IDictionary<string, string> destination, object raw)
    {
        if (raw is not JsonObject jsonObject)
        {
            return false;
        }

        foreach (var kv in jsonObject)
        {
            AddAnchorIfNotEmpty(destination, kv.Key, kv.Value?.ToString());
        }

        return true;
    }

    private static bool TryMergeAnchorJsonElement(IDictionary<string, string> destination, object raw)
    {
        if (raw is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            AddAnchorIfNotEmpty(destination, property.Name, property.Value.ToString());
        }

        return true;
    }

    private static bool TryMergeAnchorObjectDictionary(IDictionary<string, string> destination, object raw)
    {
        if (raw is not IReadOnlyDictionary<string, object?> dictionary)
        {
            return false;
        }

        foreach (var kv in dictionary)
        {
            AddAnchorIfNotEmpty(destination, kv.Key, kv.Value?.ToString());
        }

        return true;
    }

    private static bool TryMergeAnchorStringPairs(IDictionary<string, string> destination, object raw)
    {
        if (raw is not IEnumerable<KeyValuePair<string, string>> pairs)
        {
            return false;
        }

        foreach (var kv in pairs.Where(static kv => !string.IsNullOrWhiteSpace(kv.Value)))
        {
            destination[kv.Key] = kv.Value;
        }

        return true;
    }

    private static void TryMergeSerializedAnchorMap(IDictionary<string, string> destination, object raw)
    {
        if (raw is not string serialized || string.IsNullOrWhiteSpace(serialized))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
            if (parsed is null)
            {
                return;
            }

            foreach (var kv in parsed.Where(static kv => !string.IsNullOrWhiteSpace(kv.Value)))
            {
                destination[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // ignored
        }
    }

    private static void AddAnchorIfNotEmpty(IDictionary<string, string> destination, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            destination[key] = value;
        }
    }

    private async Task<ActionExecutionResult> ExecuteMemoryActionAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        var symbol = ResolveMemoryActionSymbol(request.Payload);
        if (TryExecuteCreditsMemoryWrite(request, symbol, cancellationToken) is { } creditsWrite)
        {
            return await creditsWrite;
        }

        var context = BuildMemoryActionContext(symbol, request.RuntimeMode);
        var writeResult = await TryExecuteMemoryWriteAsync(request, symbol, context, cancellationToken);
        if (writeResult is not null)
        {
            return writeResult;
        }

        return ExecuteMemoryReadAction(symbol, context.SymbolInfo, context.ValidationRule, context.IsCriticalSymbol);
    }

    private static string ResolveMemoryActionSymbol(JsonObject payload)
    {
        var symbol = payload["symbol"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new InvalidOperationException("Memory action payload requires 'symbol'.");
        }

        return symbol;
    }

    private Task<ActionExecutionResult>? TryExecuteCreditsMemoryWrite(
        ActionExecutionRequest request,
        string symbol,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload["intValue"] is null || !IsCreditsWrite(request, symbol))
        {
            return null;
        }

        var value = payload["intValue"]!.GetValue<int>();
        var lockCredits =
            TryReadBooleanPayload(payload, "lockCredits", out var lockFromPayload) ? lockFromPayload :
            TryReadBooleanPayload(payload, "forcePatchHook", out var legacyForcePatchHook) && legacyForcePatchHook;
        return SetCreditsAsync(value, lockCredits, request.Action.VerifyReadback, cancellationToken);
    }

    private MemoryActionContext BuildMemoryActionContext(string symbol, RuntimeMode runtimeMode)
    {
        var symbolInfo = ResolveSymbol(symbol);
        var validationRule = ResolveSymbolValidationRule(symbol, runtimeMode);
        var isCriticalSymbol = IsCriticalSymbol(symbol, validationRule);
        return new MemoryActionContext(symbolInfo, validationRule, isCriticalSymbol);
    }

    private async Task<ActionExecutionResult?> TryExecuteMemoryWriteAsync(
        ActionExecutionRequest request,
        string symbol,
        MemoryActionContext context,
        CancellationToken cancellationToken)
    {
        if (TryReadIntPayload(request.Payload, out var intValue))
        {
            return await ExecuteIntMemoryWriteAsync(request, symbol, context, intValue, cancellationToken);
        }

        if (TryReadFloatPayload(request.Payload, out var floatValue))
        {
            return await ExecuteFloatMemoryWriteAsync(request, symbol, context, floatValue, cancellationToken);
        }

        if (TryReadBoolPayload(request.Payload, out var boolValue))
        {
            return await ExecuteBoolMemoryWriteAsync(request, symbol, context, boolValue, cancellationToken);
        }

        return null;
    }

    private static bool TryReadIntPayload(JsonObject payload, out int value)
    {
        if (payload["intValue"] is null)
        {
            value = default;
            return false;
        }

        value = payload["intValue"]!.GetValue<int>();
        return true;
    }

    private static bool TryReadFloatPayload(JsonObject payload, out float value)
    {
        if (payload["floatValue"] is null)
        {
            value = default;
            return false;
        }

        try
        {
            value = payload["floatValue"]!.GetValue<float>();
        }
        catch (InvalidOperationException)
        {
            value = (float)payload["floatValue"]!.GetValue<double>();
        }

        return true;
    }

    private static bool TryReadBoolPayload(JsonObject payload, out bool value)
    {
        if (payload["boolValue"] is null)
        {
            value = default;
            return false;
        }

        value = payload["boolValue"]!.GetValue<bool>();
        return true;
    }

    private async Task<ActionExecutionResult> ExecuteIntMemoryWriteAsync(
        ActionExecutionRequest request,
        string symbol,
        MemoryActionContext context,
        int value,
        CancellationToken cancellationToken)
    {
        var requestedValidation = ValidateRequestedIntValue(symbol, value, context.ValidationRule);
        if (!requestedValidation.IsValid)
        {
            return BuildRequestedValidationFailureResult(context.SymbolInfo, requestedValidation);
        }

        return await WriteWithOptionalRetryAsync(
            symbol,
            context.SymbolInfo,
            value,
            request.Action.VerifyReadback,
            context.IsCriticalSymbol,
            request.RuntimeMode,
            context.ValidationRule,
            readValue: address => _memory!.Read<int>(address),
            writeValue: (address, requestedValue) => _memory!.Write(address, requestedValue),
            compareValues: static (expected, actual) => actual == expected,
            validateObservedValue: observed => ValidateObservedIntValue(symbol, observed, context.ValidationRule),
            formatValue: observed => observed.ToString(),
            cancellationToken: cancellationToken);
    }

    private async Task<ActionExecutionResult> ExecuteFloatMemoryWriteAsync(
        ActionExecutionRequest request,
        string symbol,
        MemoryActionContext context,
        float value,
        CancellationToken cancellationToken)
    {
        var requestedValidation = ValidateRequestedFloatValue(symbol, value, context.ValidationRule);
        if (!requestedValidation.IsValid)
        {
            return BuildRequestedValidationFailureResult(context.SymbolInfo, requestedValidation);
        }

        return await WriteWithOptionalRetryAsync(
            symbol,
            context.SymbolInfo,
            value,
            request.Action.VerifyReadback,
            context.IsCriticalSymbol,
            request.RuntimeMode,
            context.ValidationRule,
            readValue: address => _memory!.Read<float>(address),
            writeValue: (address, requestedValue) => _memory!.Write(address, requestedValue),
            compareValues: static (expected, actual) => Math.Abs(actual - expected) <= 0.0001f,
            validateObservedValue: observed => ValidateObservedFloatValue(symbol, observed, context.ValidationRule),
            formatValue: observed => observed.ToString("0.####"),
            cancellationToken: cancellationToken);
    }

    private async Task<ActionExecutionResult> ExecuteBoolMemoryWriteAsync(
        ActionExecutionRequest request,
        string symbol,
        MemoryActionContext context,
        bool value,
        CancellationToken cancellationToken)
    {
        var byteValue = value ? (byte)1 : (byte)0;
        var requestedValidation = ValidateRequestedIntValue(symbol, byteValue, context.ValidationRule);
        if (!requestedValidation.IsValid)
        {
            return BuildRequestedValidationFailureResult(context.SymbolInfo, requestedValidation);
        }

        return await WriteWithOptionalRetryAsync(
            symbol,
            context.SymbolInfo,
            byteValue,
            request.Action.VerifyReadback,
            context.IsCriticalSymbol,
            request.RuntimeMode,
            context.ValidationRule,
            readValue: address => _memory!.Read<byte>(address),
            writeValue: (address, requestedValue) => _memory!.Write(address, requestedValue),
            compareValues: static (expected, actual) => actual == expected,
            validateObservedValue: observed => ValidateObservedIntValue(symbol, observed, context.ValidationRule),
            formatValue: observed => (observed != 0).ToString(),
            cancellationToken: cancellationToken);
    }

    private static ActionExecutionResult BuildRequestedValidationFailureResult(
        SymbolInfo symbolInfo,
        ValidationOutcome requestedValidation)
    {
        return new ActionExecutionResult(
            false,
            requestedValidation.Message,
            symbolInfo.Source,
            new Dictionary<string, object?>
            {
                ["address"] = $"0x{symbolInfo.Address.ToInt64():X}",
                ["failureReasonCode"] = requestedValidation.ReasonCode,
                ["symbolHealthStatus"] = symbolInfo.HealthStatus.ToString(),
                ["symbolConfidence"] = symbolInfo.Confidence
            });
    }

    private ActionExecutionResult ExecuteMemoryReadAction(
        string symbol,
        SymbolInfo symbolInfo,
        SymbolValidationRule? validationRule,
        bool isCriticalSymbol)
    {
        if (!TryReadMemorySymbolValue(symbolInfo, out var readValue))
        {
            throw new InvalidOperationException(
                $"Memory action payload must include one of: intValue, floatValue, boolValue. Read is unsupported for symbol value type {symbolInfo.ValueType}.");
        }

        var readDiagnostics = CreateSymbolDiagnostics(symbolInfo, validationRule, isCriticalSymbol);
        readDiagnostics["value"] = readValue;
        var observedValidation = ValidateObservedReadValue(symbol, readValue!, symbolInfo.ValueType, validationRule);
        readDiagnostics["validationStatus"] = observedValidation.IsValid ? "pass" : "degraded";
        readDiagnostics["validationReasonCode"] = observedValidation.ReasonCode;

        if (!observedValidation.IsValid)
        {
            return new ActionExecutionResult(false, observedValidation.Message, symbolInfo.Source, readDiagnostics);
        }

        return new ActionExecutionResult(
            true,
            $"Read {symbolInfo.ValueType} value {readValue} from symbol {symbol}",
            symbolInfo.Source,
            readDiagnostics);
    }

    private bool TryReadMemorySymbolValue(SymbolInfo symbolInfo, out object? readValue)
    {
        readValue = symbolInfo.ValueType switch
        {
            SymbolValueType.Int32 => _memory!.Read<int>(symbolInfo.Address),
            SymbolValueType.Int64 => _memory!.Read<long>(symbolInfo.Address),
            SymbolValueType.Float => _memory!.Read<float>(symbolInfo.Address),
            SymbolValueType.Double => _memory!.Read<double>(symbolInfo.Address),
            SymbolValueType.Byte => _memory!.Read<byte>(symbolInfo.Address),
            SymbolValueType.Bool => _memory!.Read<byte>(symbolInfo.Address) != 0,
            SymbolValueType.Pointer => $"0x{_memory!.Read<long>(symbolInfo.Address):X}",
            _ => null
        };
        return readValue is not null;
    }

    private readonly record struct MemoryActionContext(
        SymbolInfo SymbolInfo,
        SymbolValidationRule? ValidationRule,
        bool IsCriticalSymbol);

    private sealed record ValidationOutcome(bool IsValid, string ReasonCode, string Message)
    {
        public static ValidationOutcome Pass(string reasonCode = "ok") => new(true, reasonCode, string.Empty);

        public static ValidationOutcome Fail(string reasonCode, string message) => new(false, reasonCode, message);
    }

    private SymbolValidationRule? ResolveSymbolValidationRule(string symbol, RuntimeMode mode)
    {
        if (!_symbolValidationRules.TryGetValue(symbol, out var rules) || rules.Count == 0)
        {
            return null;
        }

        if (mode != RuntimeMode.Unknown)
        {
            var exact = rules.FirstOrDefault(x => x.Mode == mode);
            if (exact is not null)
            {
                return exact;
            }
        }

        return rules.FirstOrDefault(x => x.Mode is null) ?? rules[0];
    }

    private bool IsCriticalSymbol(string symbol, SymbolValidationRule? rule)
    {
        return _criticalSymbols.Contains(symbol) || (rule?.Critical ?? false);
    }

    private static ValidationOutcome ValidateRequestedIntValue(string symbol, long value, SymbolValidationRule? rule)
    {
        if (rule is null)
        {
            return ValidationOutcome.Pass();
        }

        if (rule.IntMin.HasValue && value < rule.IntMin.Value)
        {
            return ValidationOutcome.Fail(
                "value_below_min",
                $"Requested int value {value} for symbol '{symbol}' is below minimum {rule.IntMin.Value}.");
        }

        if (rule.IntMax.HasValue && value > rule.IntMax.Value)
        {
            return ValidationOutcome.Fail(
                "value_above_max",
                $"Requested int value {value} for symbol '{symbol}' exceeds maximum {rule.IntMax.Value}.");
        }

        return ValidationOutcome.Pass();
    }

    private static ValidationOutcome ValidateRequestedFloatValue(string symbol, double value, SymbolValidationRule? rule)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return ValidationOutcome.Fail(
                "value_non_finite",
                $"Requested float value for symbol '{symbol}' must be finite.");
        }

        if (rule is null)
        {
            return ValidationOutcome.Pass();
        }

        if (rule.FloatMin.HasValue && value < rule.FloatMin.Value)
        {
            return ValidationOutcome.Fail(
                "value_below_min",
                $"Requested float value {value:0.####} for symbol '{symbol}' is below minimum {rule.FloatMin.Value:0.####}.");
        }

        if (rule.FloatMax.HasValue && value > rule.FloatMax.Value)
        {
            return ValidationOutcome.Fail(
                "value_above_max",
                $"Requested float value {value:0.####} for symbol '{symbol}' exceeds maximum {rule.FloatMax.Value:0.####}.");
        }

        return ValidationOutcome.Pass();
    }

    private static ValidationOutcome ValidateObservedIntValue(string symbol, long observed, SymbolValidationRule? rule)
    {
        if (rule is null)
        {
            return ValidationOutcome.Pass();
        }

        if (rule.IntMin.HasValue && observed < rule.IntMin.Value)
        {
            return ValidationOutcome.Fail(
                "observed_below_min",
                $"Observed int value {observed} for symbol '{symbol}' is below minimum {rule.IntMin.Value}.");
        }

        if (rule.IntMax.HasValue && observed > rule.IntMax.Value)
        {
            return ValidationOutcome.Fail(
                "observed_above_max",
                $"Observed int value {observed} for symbol '{symbol}' exceeds maximum {rule.IntMax.Value}.");
        }

        return ValidationOutcome.Pass();
    }

    private static ValidationOutcome ValidateObservedFloatValue(string symbol, double observed, SymbolValidationRule? rule)
    {
        if (double.IsNaN(observed) || double.IsInfinity(observed))
        {
            return ValidationOutcome.Fail(
                "observed_non_finite",
                $"Observed float value for symbol '{symbol}' is non-finite.");
        }

        if (rule is null)
        {
            return ValidationOutcome.Pass();
        }

        if (rule.FloatMin.HasValue && observed < rule.FloatMin.Value)
        {
            return ValidationOutcome.Fail(
                "observed_below_min",
                $"Observed float value {observed:0.####} for symbol '{symbol}' is below minimum {rule.FloatMin.Value:0.####}.");
        }

        if (rule.FloatMax.HasValue && observed > rule.FloatMax.Value)
        {
            return ValidationOutcome.Fail(
                "observed_above_max",
                $"Observed float value {observed:0.####} for symbol '{symbol}' exceeds maximum {rule.FloatMax.Value:0.####}.");
        }

        return ValidationOutcome.Pass();
    }

    private static ValidationOutcome ValidateObservedReadValue(
        string symbol,
        object observedValue,
        SymbolValueType valueType,
        SymbolValidationRule? rule)
    {
        try
        {
            return valueType switch
            {
                SymbolValueType.Int32 => ValidateObservedIntValue(symbol, Convert.ToInt64(observedValue), rule),
                SymbolValueType.Int64 => ValidateObservedIntValue(symbol, Convert.ToInt64(observedValue), rule),
                SymbolValueType.Byte => ValidateObservedIntValue(symbol, Convert.ToInt64(observedValue), rule),
                SymbolValueType.Bool => ValidateObservedIntValue(symbol, Convert.ToBoolean(observedValue) ? 1 : 0, rule),
                SymbolValueType.Float => ValidateObservedFloatValue(symbol, Convert.ToDouble(observedValue), rule),
                SymbolValueType.Double => ValidateObservedFloatValue(symbol, Convert.ToDouble(observedValue), rule),
                _ => ValidationOutcome.Pass()
            };
        }
        catch
        {
            return ValidationOutcome.Fail(
                "observed_cast_failed",
                $"Could not validate observed value for symbol '{symbol}' as {valueType}.");
        }
    }

    private static Dictionary<string, object?> CreateSymbolDiagnostics(
        SymbolInfo symbolInfo,
        SymbolValidationRule? validationRule,
        bool isCriticalSymbol)
    {
        var diagnostics = new Dictionary<string, object?>
        {
            ["address"] = $"0x{symbolInfo.Address.ToInt64():X}",
            ["symbolSource"] = symbolInfo.Source.ToString(),
            ["symbolHealthStatus"] = symbolInfo.HealthStatus.ToString(),
            ["symbolHealthReason"] = symbolInfo.HealthReason,
            ["symbolConfidence"] = symbolInfo.Confidence,
            ["criticalSymbol"] = isCriticalSymbol
        };

        if (validationRule is not null)
        {
            diagnostics["validationRuleMode"] = validationRule.Mode?.ToString() ?? "Any";
            diagnostics["validationRange"] = FormatValidationRuleRange(validationRule);
        }

        return diagnostics;
    }

    private static string FormatValidationRuleRange(SymbolValidationRule rule)
    {
        var parts = new List<string>();
        if (rule.IntMin.HasValue || rule.IntMax.HasValue)
        {
            parts.Add($"int[{rule.IntMin?.ToString() ?? "-inf"},{rule.IntMax?.ToString() ?? "+inf"}]");
        }

        if (rule.FloatMin.HasValue || rule.FloatMax.HasValue)
        {
            parts.Add($"float[{rule.FloatMin?.ToString("0.####") ?? "-inf"},{rule.FloatMax?.ToString("0.####") ?? "+inf"}]");
        }

        return parts.Count == 0 ? "none" : string.Join(";", parts);
    }

    private async Task<ActionExecutionResult> WriteWithOptionalRetryAsync<T>(
        string symbol,
        SymbolInfo symbolInfo,
        T requestedValue,
        bool verifyReadback,
        bool isCriticalSymbol,
        RuntimeMode runtimeMode,
        SymbolValidationRule? validationRule,
        Func<nint, T> readValue,
        Action<nint, T> writeValue,
        Func<T, T, bool> compareValues,
        Func<T, ValidationOutcome> validateObservedValue,
        Func<T, string> formatValue,
        CancellationToken cancellationToken)
        where T : struct
    {
        var diagnostics = CreateSymbolDiagnostics(symbolInfo, validationRule, isCriticalSymbol);
        diagnostics["requestedValue"] = formatValue(requestedValue);
        var initial = ExecuteWriteAttempt(
            symbol,
            symbolInfo,
            requestedValue,
            verifyReadback,
            "initial",
            readValue,
            writeValue,
            compareValues,
            validateObservedValue,
            formatValue);
        if (initial.HasObservedValue)
        {
            diagnostics["readbackValue"] = formatValue(initial.ObservedValue);
        }

        if (initial.Success)
        {
            return new ActionExecutionResult(
                true,
                $"Wrote value {formatValue(requestedValue)} to symbol {symbol}",
                symbolInfo.Source,
                diagnostics);
        }

        diagnostics["failureReasonCode"] = initial.ReasonCode;
        if (!isCriticalSymbol)
        {
            return new ActionExecutionResult(false, initial.Message, symbolInfo.Source, diagnostics);
        }

        diagnostics["retryAttempted"] = true;
        var reresolve = await TryReResolveSymbolAsync(symbol, runtimeMode, cancellationToken);
        diagnostics["retryReasonCode"] = reresolve.ReasonCode;
        if (!reresolve.Succeeded || reresolve.Symbol is null)
        {
            diagnostics["failureReasonCode"] = reresolve.ReasonCode;
            return new ActionExecutionResult(false, reresolve.Message, symbolInfo.Source, diagnostics);
        }

        var refreshedSymbol = reresolve.Symbol;
        diagnostics["retryAddress"] = ToHex(refreshedSymbol.Address);
        diagnostics["retrySymbolSource"] = refreshedSymbol.Source.ToString();
        diagnostics["retrySymbolHealthStatus"] = refreshedSymbol.HealthStatus.ToString();
        diagnostics["retrySymbolConfidence"] = refreshedSymbol.Confidence;

        var retryAttempt = ExecuteWriteAttempt(
            symbol,
            refreshedSymbol,
            requestedValue,
            verifyReadback,
            "retry",
            readValue,
            writeValue,
            compareValues,
            validateObservedValue,
            formatValue);
        if (retryAttempt.HasObservedValue)
        {
            diagnostics["retryReadbackValue"] = formatValue(retryAttempt.ObservedValue);
        }

        if (retryAttempt.Success)
        {
            diagnostics.Remove("failureReasonCode");
            return new ActionExecutionResult(
                true,
                $"Wrote value {formatValue(requestedValue)} to symbol {symbol} after re-resolve retry.",
                refreshedSymbol.Source,
                diagnostics);
        }

        diagnostics["failureReasonCode"] = retryAttempt.ReasonCode;
        return new ActionExecutionResult(
            false,
            retryAttempt.Message,
            refreshedSymbol.Source,
            diagnostics);
    }

    private WriteAttemptResult<T> ExecuteWriteAttempt<T>(
        string symbol,
        SymbolInfo activeSymbol,
        T requestedValue,
        bool verifyReadback,
        string attemptPrefix,
        Func<nint, T> readValue,
        Action<nint, T> writeValue,
        Func<T, T, bool> compareValues,
        Func<T, ValidationOutcome> validateObservedValue,
        Func<T, string> formatValue)
        where T : struct
    {
        try
        {
            writeValue(activeSymbol.Address, requestedValue);
        }
        catch (Exception ex)
        {
            return new WriteAttemptResult<T>(
                false,
                $"{attemptPrefix}_write_exception",
                $"Write failed for symbol '{symbol}' at {ToHex(activeSymbol.Address)}: {ex.Message}",
                false,
                default);
        }

        if (!verifyReadback)
        {
            return WriteAttemptResult<T>.SuccessWithoutObservation();
        }

        return BuildWriteAttemptReadbackResult(
            symbol,
            activeSymbol,
            requestedValue,
            attemptPrefix,
            readValue,
            compareValues,
            validateObservedValue,
            formatValue);
    }

    private WriteAttemptResult<T> BuildWriteAttemptReadbackResult<T>(
        string symbol,
        SymbolInfo activeSymbol,
        T requestedValue,
        string attemptPrefix,
        Func<nint, T> readValue,
        Func<T, T, bool> compareValues,
        Func<T, ValidationOutcome> validateObservedValue,
        Func<T, string> formatValue)
        where T : struct
    {
        T observed;
        try
        {
            observed = readValue(activeSymbol.Address);
        }
        catch (Exception ex)
        {
            return new WriteAttemptResult<T>(
                false,
                $"{attemptPrefix}_readback_exception",
                $"Readback failed for symbol '{symbol}' at {ToHex(activeSymbol.Address)}: {ex.Message}",
                false,
                default);
        }

        var matches = compareValues(requestedValue, observed);
        var observedValidation = validateObservedValue(observed);
        if (matches && observedValidation.IsValid)
        {
            return WriteAttemptResult<T>.SuccessWithObservation(observed);
        }

        if (!matches)
        {
            return new WriteAttemptResult<T>(
                false,
                $"{attemptPrefix}_readback_mismatch",
                $"Readback mismatch for {symbol}: expected {formatValue(requestedValue)}, got {formatValue(observed)}",
                true,
                observed);
        }

        return new WriteAttemptResult<T>(
            false,
            observedValidation.ReasonCode,
            observedValidation.Message,
            true,
            observed);
    }

    private async Task<(bool Succeeded, SymbolInfo? Symbol, string ReasonCode, string Message)> TryReResolveSymbolAsync(
        string symbol,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken)
    {
        if (!TryGetReResolveContext(out var context))
        {
            return BuildReResolveUnavailableResult();
        }

        try
        {
            var refreshedMap = await _signatureResolver.ResolveAsync(
                context.Session.Build,
                context.Profile.SignatureSets,
                context.Profile.FallbackOffsets,
                cancellationToken);
            if (!refreshedMap.TryGetValue(symbol, out var refreshedSymbol) ||
                refreshedSymbol is null ||
                refreshedSymbol.Address == nint.Zero)
            {
                return BuildReResolveUnresolvedResult(symbol);
            }

            var normalized = NormalizeReResolvedSymbol(refreshedSymbol, context.Profile, runtimeMode);
            UpdateSessionSymbol(normalized);
            LogReResolvedSymbol(symbol, normalized);
            return BuildReResolveSuccessResult(normalized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-resolve symbol {Symbol}.", symbol);
            return BuildReResolveExceptionResult(symbol, ex);
        }
    }

    private bool TryGetReResolveContext(out ReResolveContext context)
    {
        if (CurrentSession is null || _attachedProfile is null)
        {
            context = default;
            return false;
        }

        context = new ReResolveContext(CurrentSession, _attachedProfile);
        return true;
    }

    private static (bool Succeeded, SymbolInfo? Symbol, string ReasonCode, string Message) BuildReResolveUnavailableResult()
    {
        return (false, null, "reresolve_unavailable", "Re-resolve is unavailable without an active attach session.");
    }

    private static (bool Succeeded, SymbolInfo? Symbol, string ReasonCode, string Message) BuildReResolveUnresolvedResult(string symbol)
    {
        return (false, null, "reresolve_symbol_unresolved", $"Re-resolve could not find a usable address for symbol '{symbol}'.");
    }

    private SymbolInfo NormalizeReResolvedSymbol(
        SymbolInfo refreshedSymbol,
        TrainerProfile attachedProfile,
        RuntimeMode runtimeMode)
    {
        var evaluation = _symbolHealthService.Evaluate(refreshedSymbol, attachedProfile, runtimeMode);
        return refreshedSymbol with
        {
            Confidence = ClampConfidence(Math.Max(refreshedSymbol.Confidence, evaluation.Confidence)),
            HealthStatus = evaluation.Status,
            HealthReason = string.IsNullOrWhiteSpace(refreshedSymbol.HealthReason)
                ? evaluation.Reason
                : $"{refreshedSymbol.HealthReason}+{evaluation.Reason}",
            LastValidatedAt = DateTimeOffset.UtcNow
        };
    }

    private void LogReResolvedSymbol(string symbol, SymbolInfo normalized)
    {
        _logger.LogInformation(
            "Re-resolved symbol {Symbol} to {Address} (source={Source}, health={Health}, confidence={Confidence:0.00})",
            symbol,
            ToHex(normalized.Address),
            normalized.Source,
            normalized.HealthStatus,
            normalized.Confidence);
    }

    private static (bool Succeeded, SymbolInfo? Symbol, string ReasonCode, string Message) BuildReResolveSuccessResult(SymbolInfo normalized)
    {
        return (true, normalized, "reresolve_success", "Re-resolve succeeded.");
    }

    private static (bool Succeeded, SymbolInfo? Symbol, string ReasonCode, string Message) BuildReResolveExceptionResult(
        string symbol,
        Exception ex)
    {
        return (false, null, "reresolve_exception", $"Re-resolve failed for symbol '{symbol}': {ex.Message}");
    }

    private void UpdateSessionSymbol(SymbolInfo symbolInfo)
    {
        if (CurrentSession is null)
        {
            return;
        }

        var symbols = new Dictionary<string, SymbolInfo>(CurrentSession.Symbols.Symbols, StringComparer.OrdinalIgnoreCase)
        {
            [symbolInfo.Name] = symbolInfo
        };

        CurrentSession = CurrentSession with
        {
            Symbols = new SymbolMap(symbols)
        };
    }

    private Task<ActionExecutionResult> ExecuteHelperActionAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        // Runtime adapter only records helper action dispatch. Actual helper scripts are handled in SwfocTrainer.Helper.
        var helperId = request.Payload["helperHookId"]?.GetValue<string>() ?? request.Action.Id;
        return Task.FromResult(new ActionExecutionResult(
            true,
            $"Helper action '{helperId}' dispatched.",
            AddressSource.None,
            new Dictionary<string, object?> { ["dispatched"] = true }));
    }

    private Task<ActionExecutionResult> ExecuteSaveActionAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ActionExecutionResult(
            true,
            "Save action request accepted. Execute via save editor pipeline.",
            AddressSource.None));
    }

    /// <summary>
    /// Executes a code-patch toggle action. The payload must include:
    ///   - "symbol"       : signature name to resolve patch target address
    ///   - "enable"       : bool — true to apply patch, false to restore original bytes
    ///   - "patchBytes"   : hex string of bytes to write when enabling (e.g. "90 90 90 90 90")
    ///   - "originalBytes" : hex string of expected original bytes for validation (e.g. "48 8B 74 24 68")
    /// </summary>
    private Task<ActionExecutionResult> ExecuteCodePatchActionAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        if (TryDispatchSpecializedCodePatchAction(request, out var specializedResult))
        {
            return specializedResult!;
        }

        if (!TryBuildCodePatchContext(request.Payload, out var context, out var validationFailure))
        {
            return Task.FromResult(validationFailure!);
        }

        var result = context!.Enable
            ? EnableCodePatch(context)
            : DisableCodePatch(context);

        return Task.FromResult(result);
    }

    private bool TryDispatchSpecializedCodePatchAction(ActionExecutionRequest request, out Task<ActionExecutionResult>? result)
    {
        if (request.Action.Id.Equals(ActionIdSetUnitCap, StringComparison.OrdinalIgnoreCase))
        {
            result = ExecuteUnitCapHookAsync(request);
            return true;
        }

        if (request.Action.Id.Equals(ActionIdToggleInstantBuildPatch, StringComparison.OrdinalIgnoreCase))
        {
            result = ExecuteInstantBuildHookAsync(request);
            return true;
        }

        result = null;
        return false;
    }

    private bool TryBuildCodePatchContext(
        JsonObject payload,
        out CodePatchActionContext? context,
        out ActionExecutionResult? failure)
    {
        context = null;
        failure = null;
        if (!TryReadCodePatchSymbol(payload, out var symbol, out failure))
        {
            return false;
        }

        var enable = payload["enable"]?.GetValue<bool>() ?? true;
        if (!TryParseCodePatchBytes(payload, out var patchBytes, out var originalBytes, out failure))
        {
            return false;
        }

        var symbolInfo = ResolveSymbol(symbol!);
        context = new CodePatchActionContext(
            symbol!,
            enable,
            patchBytes!,
            originalBytes!,
            symbolInfo,
            symbolInfo.Address);
        return true;
    }

    private static bool TryReadCodePatchSymbol(
        JsonObject payload,
        out string? symbol,
        out ActionExecutionResult? failure)
    {
        symbol = payload["symbol"]?.GetValue<string>();
        failure = null;
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            return true;
        }

        failure = new ActionExecutionResult(false, "CodePatch action requires 'symbol' in payload.", AddressSource.None);
        return false;
    }

    private static bool TryParseCodePatchBytes(
        JsonObject payload,
        out byte[]? patchBytes,
        out byte[]? originalBytes,
        out ActionExecutionResult? failure)
    {
        patchBytes = null;
        originalBytes = null;
        failure = null;
        var patchBytesHex = payload["patchBytes"]?.GetValue<string>();
        var originalBytesHex = payload["originalBytes"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(patchBytesHex) || string.IsNullOrWhiteSpace(originalBytesHex))
        {
            failure = new ActionExecutionResult(
                false,
                "CodePatch action requires 'patchBytes' and 'originalBytes' in payload.",
                AddressSource.None);
            return false;
        }

        patchBytes = ParseHexBytes(patchBytesHex);
        originalBytes = ParseHexBytes(originalBytesHex);
        if (patchBytes.Length == originalBytes.Length)
        {
            return true;
        }

        failure = new ActionExecutionResult(
            false,
            $"CodePatch patchBytes length ({patchBytes.Length}) must match originalBytes length ({originalBytes.Length}).",
            AddressSource.None);
        return false;
    }

    private ActionExecutionResult EnableCodePatch(CodePatchActionContext context)
    {
        var currentBytes = _memory!.ReadBytes(context.Address, context.OriginalBytes.Length);
        var isAlreadyPatched = currentBytes.AsSpan().SequenceEqual(context.PatchBytes);
        var isOriginal = currentBytes.AsSpan().SequenceEqual(context.OriginalBytes);
        if (isAlreadyPatched)
        {
            return new ActionExecutionResult(
                true,
                $"Code patch '{context.Symbol}' is already active.",
                context.SymbolInfo.Source,
                new Dictionary<string, object?> { ["address"] = ToHex(context.Address), [DiagnosticKeyState] = "already_patched" });
        }

        if (!isOriginal)
        {
            return new ActionExecutionResult(
                false,
                $"Code patch '{context.Symbol}' refused: unexpected bytes at {ToHex(context.Address)}. Expected {BitConverter.ToString(context.OriginalBytes)} got {BitConverter.ToString(currentBytes)}.",
                context.SymbolInfo.Source);
        }

        _activeCodePatches[context.Symbol] = (context.Address, currentBytes.ToArray());
        _memory.WriteBytes(context.Address, context.PatchBytes, executablePatch: true);
        _logger.LogInformation("Code patch '{Symbol}' applied at {Address}", context.Symbol, ToHex(context.Address));
        return new ActionExecutionResult(
            true,
            $"Code patch '{context.Symbol}' enabled at {ToHex(context.Address)}.",
            context.SymbolInfo.Source,
            new Dictionary<string, object?>
            {
                ["address"] = ToHex(context.Address),
                [DiagnosticKeyState] = "patched",
                ["bytesWritten"] = BitConverter.ToString(context.PatchBytes)
            });
    }

    private ActionExecutionResult DisableCodePatch(CodePatchActionContext context)
    {
        if (_activeCodePatches.TryGetValue(context.Symbol, out var saved))
        {
            _memory!.WriteBytes(saved.Address, saved.OriginalBytes, executablePatch: true);
            _activeCodePatches.Remove(context.Symbol);
            _logger.LogInformation("Code patch '{Symbol}' restored at {Address}", context.Symbol, ToHex(saved.Address));
            return new ActionExecutionResult(
                true,
                $"Code patch '{context.Symbol}' disabled, original bytes restored.",
                context.SymbolInfo.Source,
                new Dictionary<string, object?>
                {
                    ["address"] = ToHex(saved.Address),
                    [DiagnosticKeyState] = "restored",
                    ["bytesWritten"] = BitConverter.ToString(saved.OriginalBytes)
                });
        }

        _memory!.WriteBytes(context.Address, context.OriginalBytes, executablePatch: true);
        return new ActionExecutionResult(
            true,
            $"Code patch '{context.Symbol}' was not active, wrote original bytes as safety restore.",
            context.SymbolInfo.Source,
            new Dictionary<string, object?> { ["address"] = ToHex(context.Address), [DiagnosticKeyState] = "force_restored" });
    }

    private static byte[] ParseHexBytes(string hex)
    {
        var parts = hex.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            bytes[i] = Convert.ToByte(parts[i], 16);
        }
        return bytes;
    }

    private Task<ActionExecutionResult> ExecuteUnitCapHookAsync(ActionExecutionRequest request)
    {
        var payload = request.Payload;
        if (!(payload["enable"]?.GetValue<bool>() ?? true))
        {
            return Task.FromResult(DisableUnitCapHook());
        }

        if (EnsureInstantBuildHookDisabledForUnitCap() is { } instantBuildFailure)
        {
            return Task.FromResult(instantBuildFailure);
        }

        if (DisableLegacyUnitCapPatch() is { } legacyPatchFailure)
        {
            return Task.FromResult(legacyPatchFailure);
        }

        var capValue = payload["intValue"]?.GetValue<int>() ?? 99999;
        return Task.FromResult(EnsureUnitCapHookInstalled(capValue));
    }

    private ActionExecutionResult? EnsureInstantBuildHookDisabledForUnitCap()
    {
        if (_instantBuildHookOriginalBytesBackup is null || _instantBuildHookInjectionAddress == nint.Zero)
        {
            return null;
        }

        var disabledInstantBuild = DisableInstantBuildHook();
        if (disabledInstantBuild.Succeeded)
        {
            return null;
        }

        return new ActionExecutionResult(
            false,
            $"Cannot enable unit cap while instant-build hook is active: {disabledInstantBuild.Message}",
            AddressSource.None);
    }

    private ActionExecutionResult? DisableLegacyUnitCapPatch()
    {
        if (!_activeCodePatches.TryGetValue("unit_cap", out var activePatch))
        {
            return null;
        }

        try
        {
            _memory!.WriteBytes(activePatch.Address, activePatch.OriginalBytes, executablePatch: true);
            _activeCodePatches.Remove("unit_cap");
            return null;
        }
        catch (Exception ex)
        {
            return new ActionExecutionResult(
                false,
                $"Failed to disable instant-build patch before unit cap hook install: {ex.Message}",
                AddressSource.None);
        }
    }

    private Task<ActionExecutionResult> ExecuteInstantBuildHookAsync(ActionExecutionRequest request)
    {
        var payload = request.Payload;
        var enable = payload["enable"]?.GetValue<bool>() ?? true;

        if (!enable)
        {
            var disabled = DisableInstantBuildHook();
            return Task.FromResult(disabled);
        }

        if (_unitCapHookOriginalBytesBackup is not null && _unitCapHookInjectionAddress != nint.Zero)
        {
            var disabledUnitCap = DisableUnitCapHook();
            if (!disabledUnitCap.Succeeded)
            {
                return Task.FromResult(new ActionExecutionResult(
                    false,
                    $"Cannot enable instant build while unit-cap hook is active: {disabledUnitCap.Message}",
                    AddressSource.None));
            }
        }

        var result = EnsureInstantBuildHookInstalled();
        return Task.FromResult(result);
    }

    private async Task<ActionExecutionResult> SetCreditsAsync(
        int value,
        bool lockCredits,
        bool verifyReadback,
        CancellationToken cancellationToken)
    {
        if (_memory is null)
        {
            return new ActionExecutionResult(false, "Credits write failed: memory accessor unavailable.", AddressSource.None);
        }

        var diagnostics = new Dictionary<string, object?>();

        if (TryResolveCreditsSymbolForWrite(diagnostics, out var creditsSymbol) is { } symbolFailure)
        {
            return symbolFailure;
        }

        if (EnsureCreditsHookAvailable(value, creditsSymbol, diagnostics) is { } hookFailure)
        {
            return hookFailure;
        }

        var pulseState = await ExecuteCreditsHookPulseAsync(value, diagnostics, cancellationToken);
        if (HandleCreditsHookTickState(value, lockCredits, creditsSymbol, pulseState.HookTickObserved, diagnostics) is { } pulseFailure)
        {
            return pulseFailure;
        }

        // Keep int mirror consistent with the now-authoritative forced float state.
        _memory.Write(creditsSymbol.Address, value);
        diagnostics["lockCredits"] = lockCredits;

        if (await VerifyCreditsReadbackAsync(
                value,
                lockCredits,
                verifyReadback,
                creditsSymbol,
                pulseState.CreditsFloatAddress,
                diagnostics,
                cancellationToken) is { } readbackFailure)
        {
            return readbackFailure;
        }

        return CreateCreditsWriteSuccessResult(value, lockCredits, creditsSymbol.Source, diagnostics);
    }

    private ActionExecutionResult? TryResolveCreditsSymbolForWrite(
        Dictionary<string, object?> diagnostics,
        out SymbolInfo creditsSymbol)
    {
        creditsSymbol = default!;
        try
        {
            creditsSymbol = ResolveSymbol(SymbolCredits);
            diagnostics["creditsAddress"] = ToHex(creditsSymbol.Address);
            return null;
        }
        catch (KeyNotFoundException)
        {
            return new ActionExecutionResult(
                false,
                "Credits symbol 'credits' was not resolved. Attach to the game and ensure the profile has the credits signature or fallback offset.",
                AddressSource.None,
                diagnostics);
        }
    }

    private ActionExecutionResult? EnsureCreditsHookAvailable(
        int requestedValue,
        SymbolInfo creditsSymbol,
        Dictionary<string, object?> diagnostics)
    {
        var patchResult = EnsureCreditsRuntimeHookInstalled();
        diagnostics["hookInstalled"] = patchResult.Succeeded;
        if (!patchResult.Succeeded)
        {
            diagnostics["hookError"] = patchResult.Message;
            diagnostics[DiagnosticKeyCreditsStateTag] = "HOOK_REQUIRED";
            diagnostics["creditsRequestedValue"] = requestedValue;
            return new ActionExecutionResult(
                false,
                $"Credits write aborted: hook install failed ({patchResult.Message}).",
                creditsSymbol.Source,
                diagnostics);
        }

        if (patchResult.Diagnostics is null)
        {
            return null;
        }

        foreach (var kv in patchResult.Diagnostics)
        {
            diagnostics[kv.Key] = kv.Value;
        }

        return null;
    }

    private async Task<CreditsWritePulseState> ExecuteCreditsHookPulseAsync(
        int value,
        Dictionary<string, object?> diagnostics,
        CancellationToken cancellationToken)
    {
        var forcedFloatBits = BitConverter.SingleToInt32Bits((float)value);
        _memory!.Write(_creditsHookForcedFloatBitsAddress, forcedFloatBits);
        var baselineHitCount = _memory.Read<int>(_creditsHookHitCountAddress);
        _memory.Write(_creditsHookLockEnabledAddress, 1);
        var hookPulse = await WaitForCreditsHookTickAsync(baselineHitCount, CreditsHookPulseTimeoutMs, cancellationToken);
        diagnostics["forcedFloatBits"] = $"0x{forcedFloatBits:X8}";
        diagnostics["forcedFloatValue"] = (float)value;
        diagnostics["hookHitCountStart"] = baselineHitCount;
        diagnostics["hookHitCountEnd"] = hookPulse.HitCount;
        diagnostics["hookTickObserved"] = hookPulse.Observed;
        var contextBase = (nint)_memory.Read<long>(_creditsHookLastContextAddress);
        diagnostics["creditsContextBase"] = contextBase == nint.Zero ? null : ToHex(contextBase);
        if (contextBase == nint.Zero)
        {
            return new CreditsWritePulseState(hookPulse.Observed, nint.Zero);
        }

        var creditsFloatAddress = contextBase + _creditsHookContextOffset;
        diagnostics["creditsFloatAddress"] = ToHex(creditsFloatAddress);
        return new CreditsWritePulseState(hookPulse.Observed, creditsFloatAddress);
    }

    private ActionExecutionResult? HandleCreditsHookTickState(
        int requestedValue,
        bool lockCredits,
        SymbolInfo creditsSymbol,
        bool hookTickObserved,
        Dictionary<string, object?> diagnostics)
    {
        if (!lockCredits)
        {
            _memory!.Write(_creditsHookLockEnabledAddress, 0);
        }

        if (hookTickObserved)
        {
            return null;
        }

        diagnostics[DiagnosticKeyCreditsStateTag] = "HOOK_REQUIRED";
        diagnostics["creditsRequestedValue"] = requestedValue;
        return new ActionExecutionResult(
            false,
            $"Credits write aborted: hook did not observe a sync tick within {CreditsHookPulseTimeoutMs}ms. Enter galactic/campaign view and retry.",
            creditsSymbol.Source,
            diagnostics);
    }

    private async Task<ActionExecutionResult?> VerifyCreditsReadbackAsync(
        int requestedValue,
        bool lockCredits,
        bool verifyReadback,
        SymbolInfo creditsSymbol,
        nint creditsFloatAddress,
        Dictionary<string, object?> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!verifyReadback)
        {
            return null;
        }

        await Task.Delay(lockCredits ? 180 : 120, cancellationToken);
        var settledInt = _memory!.Read<int>(creditsSymbol.Address);
        diagnostics["intReadbackSettled"] = settledInt;
        if (settledInt != requestedValue)
        {
            return new ActionExecutionResult(
                false,
                $"Credits hook sync failed int readback (expected={requestedValue}, got={settledInt}).",
                creditsSymbol.Source,
                diagnostics);
        }

        if (creditsFloatAddress == nint.Zero)
        {
            return null;
        }

        var settledFloat = _memory.Read<float>(creditsFloatAddress);
        diagnostics["floatReadbackSettled"] = settledFloat;
        if (!lockCredits || Math.Abs(settledFloat - requestedValue) <= CreditsFloatTolerance)
        {
            return null;
        }

        return new ActionExecutionResult(
            false,
            $"Credits lock did not persist real float value (expected~={requestedValue}, got={settledFloat}).",
            creditsSymbol.Source,
            diagnostics);
    }

    private static ActionExecutionResult CreateCreditsWriteSuccessResult(
        int requestedValue,
        bool lockCredits,
        AddressSource source,
        Dictionary<string, object?> diagnostics)
    {
        var stateTag = lockCredits ? "HOOK_LOCK" : "HOOK_ONESHOT";
        var message = lockCredits
            ? "[HOOK_LOCK] Set credits and enabled persistent lock (float+int sync)."
            : "[HOOK_ONESHOT] Set credits with one-shot float+int sync.";
        diagnostics[DiagnosticKeyCreditsStateTag] = stateTag;
        diagnostics["creditsRequestedValue"] = requestedValue;
        return new ActionExecutionResult(true, message, source, diagnostics);
    }

    private async Task<CreditsHookTickObservation> WaitForCreditsHookTickAsync(
        int baselineHitCount,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (_memory is null || _creditsHookHitCountAddress == nint.Zero)
        {
            return new CreditsHookTickObservation(false, baselineHitCount);
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentHits = _memory.Read<int>(_creditsHookHitCountAddress);
            if (currentHits > baselineHitCount)
            {
                return new CreditsHookTickObservation(true, currentHits);
            }

            await Task.Delay(CreditsHookPollingDelayMs, cancellationToken);
        }

        var finalHits = _memory.Read<int>(_creditsHookHitCountAddress);
        return new CreditsHookTickObservation(finalHits > baselineHitCount, finalHits);
    }

    private CreditsHookPatchResult EnsureCreditsRuntimeHookInstalled()
    {
        EnsureAttached();
        if (_memory is null)
        {
            return CreditsHookPatchResult.Fail("Credits hook patch failed: memory accessor unavailable.");
        }

        if (IsCreditsRuntimeHookInstalled())
        {
            return BuildCreditsHookAlreadyInstalledResult();
        }

        var resolution = ResolveCreditsHookInjectionAddress();
        if (!resolution.Succeeded)
        {
            return CreditsHookPatchResult.Fail(resolution.Message);
        }

        if (TryPrepareCreditsHookInstall(resolution, out var installContext) is { } preparationFailure)
        {
            return preparationFailure;
        }

        return InstallCreditsRuntimeHook(resolution, installContext!);
    }

    private bool IsCreditsRuntimeHookInstalled()
    {
        return _creditsHookOriginalBytesBackup is not null &&
               _creditsHookInjectionAddress != nint.Zero &&
               _creditsHookCodeCaveAddress != nint.Zero &&
               _creditsHookHitCountAddress != nint.Zero &&
               _creditsHookLockEnabledAddress != nint.Zero &&
               _creditsHookForcedFloatBitsAddress != nint.Zero;
    }

    private CreditsHookPatchResult BuildCreditsHookAlreadyInstalledResult()
    {
        return CreditsHookPatchResult.Ok(
            "Credits hook already installed.",
            new Dictionary<string, object?>
            {
                ["hookAddress"] = ToHex(_creditsHookInjectionAddress),
                ["hookCaveAddress"] = ToHex(_creditsHookCodeCaveAddress),
                [DiagnosticKeyHookState] = "already_installed"
            });
    }

    private CreditsHookPatchResult? TryPrepareCreditsHookInstall(
        CreditsHookResolution resolution,
        out CreditsHookInstallContext? installContext)
    {
        installContext = null;
        var injectionAddress = resolution.Address;
        var expectedOriginalBytes = resolution.OriginalInstruction;
        if (expectedOriginalBytes is null || expectedOriginalBytes.Length != CreditsHookJumpLength)
        {
            return CreditsHookPatchResult.Fail(
                $"Credits hook patch failed: invalid original instruction metadata at {ToHex(injectionAddress)}.");
        }

        byte[] currentBytes;
        try
        {
            currentBytes = _memory!.ReadBytes(injectionAddress, expectedOriginalBytes.Length);
        }
        catch (Exception ex)
        {
            return CreditsHookPatchResult.Fail($"Credits hook patch failed: unable to read injection bytes ({ex.Message}).");
        }

        if (!currentBytes.AsSpan().SequenceEqual(expectedOriginalBytes))
        {
            return CreditsHookPatchResult.Fail(
                $"Credits hook patch refused: injection bytes mismatch at {ToHex(injectionAddress)}. Expected {BitConverter.ToString(expectedOriginalBytes)} got {BitConverter.ToString(currentBytes)}.");
        }

        var caveAddress = AllocateExecutableCaveNear(injectionAddress, CreditsHookCaveSize);
        if (caveAddress == nint.Zero)
        {
            return CreditsHookPatchResult.Fail(
                "Credits hook patch failed: unable to allocate executable code cave within rel32 jump range.");
        }

        installContext = new CreditsHookInstallContext(injectionAddress, caveAddress, currentBytes, expectedOriginalBytes);
        return null;
    }

    private CreditsHookPatchResult InstallCreditsRuntimeHook(
        CreditsHookResolution resolution,
        CreditsHookInstallContext context)
    {
        try
        {
            var caveBytes = BuildCreditsHookCaveBytes(
                context.CaveAddress,
                context.InjectionAddress,
                context.ExpectedOriginalBytes,
                resolution.DetectedOffset,
                resolution.DestinationReg);
            var jumpPatch = BuildRelativeJumpBytes(context.InjectionAddress, context.CaveAddress);
            _memory!.WriteBytes(context.CaveAddress, caveBytes, executablePatch: true);
            _memory.WriteBytes(context.InjectionAddress, jumpPatch, executablePatch: true);
            ApplyCreditsHookInstallState(context, resolution.DetectedOffset);
            var diagnostics = BuildCreditsHookInstallDiagnostics(context, resolution, jumpPatch);
            return CreditsHookPatchResult.Ok(
                $"Credits hook installed at {ToHex(context.InjectionAddress)} with cave {ToHex(context.CaveAddress)}.",
                diagnostics);
        }
        catch (Exception ex)
        {
            TryRollbackCreditsHookInstall(context.InjectionAddress, context.CurrentBytes);
            _memory!.Free(context.CaveAddress);
            ClearCreditsHookState();
            return CreditsHookPatchResult.Fail($"Credits hook patch write failed: {ex.Message}");
        }
    }

    private void ApplyCreditsHookInstallState(CreditsHookInstallContext context, byte detectedOffset)
    {
        _creditsHookOriginalBytesBackup = context.CurrentBytes.ToArray();
        _creditsHookInjectionAddress = context.InjectionAddress;
        _creditsHookCodeCaveAddress = context.CaveAddress;
        _creditsHookLastContextAddress = context.CaveAddress + CreditsHookDataLastContextOffset;
        _creditsHookHitCountAddress = context.CaveAddress + CreditsHookDataHitCountOffset;
        _creditsHookLockEnabledAddress = context.CaveAddress + CreditsHookDataLockEnabledOffset;
        _creditsHookForcedFloatBitsAddress = context.CaveAddress + CreditsHookDataForcedFloatBitsOffset;
        _creditsHookContextOffset = detectedOffset;
        _memory!.Write(_creditsHookHitCountAddress, 0);
        _memory.Write(_creditsHookLockEnabledAddress, 0);
        _memory.Write(_creditsHookForcedFloatBitsAddress, 0);
        _memory.Write(_creditsHookLastContextAddress, 0L);
    }

    private static Dictionary<string, object?> BuildCreditsHookInstallDiagnostics(
        CreditsHookInstallContext context,
        CreditsHookResolution resolution,
        byte[] jumpPatch)
    {
        return new Dictionary<string, object?>
        {
            ["hookAddress"] = ToHex(context.InjectionAddress),
            ["hookCaveAddress"] = ToHex(context.CaveAddress),
            ["hookPatchBytes"] = BitConverter.ToString(jumpPatch),
            ["hookMode"] = "trampoline_real_float",
            ["hookContextOffset"] = $"0x{resolution.DetectedOffset:X2}",
            ["hookDestinationReg"] = $"0x{resolution.DestinationReg:X2}"
        };
    }

    private void TryRollbackCreditsHookInstall(nint injectionAddress, byte[] originalBytes)
    {
        try
        {
            _memory!.WriteBytes(injectionAddress, originalBytes, executablePatch: true);
        }
        catch
        {
            // Best-effort rollback.
        }
    }

    private nint AllocateExecutableCaveNear(nint injectionAddress, int caveSize)
    {
        if (_memory is null)
        {
            return nint.Zero;
        }

        var baseHint = injectionAddress.ToInt64() & ~0xFFFFL;
        const long nearRange = 0x02000000; // 32 MB, dense search
        const long maxRange = 0x70000000; // stay comfortably below rel32 max

        if (TryAllocateInSymmetricRange(baseHint, injectionAddress, caveSize, 0, nearRange, 0x10000, out var nearCave))
        {
            return nearCave;
        }

        if (TryAllocateInSymmetricRange(baseHint, injectionAddress, caveSize, nearRange + 0x100000, maxRange, 0x100000, out var farCave))
        {
            return farCave;
        }

        return TryAllocateFallbackCave(injectionAddress, caveSize);
    }

    private bool TryAllocateInSymmetricRange(
        long baseHint,
        nint injectionAddress,
        int caveSize,
        long startDelta,
        long endDelta,
        long step,
        out nint cave)
    {
        cave = nint.Zero;
        for (var delta = startDelta; delta <= endDelta; delta += step)
        {
            if (TryAllocateNear(baseHint + delta, injectionAddress, caveSize, out cave))
            {
                return true;
            }

            if (delta != 0 && TryAllocateNear(baseHint - delta, injectionAddress, caveSize, out cave))
            {
                return true;
            }
        }

        return false;
    }

    private nint TryAllocateFallbackCave(nint injectionAddress, int caveSize)
    {
        var fallback = _memory!.Allocate((nuint)caveSize, executable: true, preferredAddress: nint.Zero);
        if (fallback == nint.Zero)
        {
            return nint.Zero;
        }

        var jmpBackSource = fallback + (CreditsHookCodeSize - CreditsHookJumpLength);
        var jmpBackTarget = injectionAddress + CreditsHookJumpLength;
        if (!IsRel32Reachable(injectionAddress, CreditsHookJumpLength, fallback) ||
            !IsRel32Reachable(jmpBackSource, CreditsHookJumpLength, jmpBackTarget))
        {
            _memory!.Free(fallback);
            return nint.Zero;
        }

        return fallback;
    }

    private bool TryAllocateNear(long preferredAddress, nint injectionAddress, int caveSize, out nint allocated)
    {
        allocated = nint.Zero;
        if (_memory is null || preferredAddress <= 0)
        {
            return false;
        }

        nint hintAddress;
        try
        {
            hintAddress = (nint)preferredAddress;
        }
        catch
        {
            return false;
        }

        var candidate = _memory.Allocate((nuint)caveSize, executable: true, preferredAddress: hintAddress);
        if (candidate == nint.Zero)
        {
            return false;
        }

        var jmpBackSource = candidate + (CreditsHookCodeSize - CreditsHookJumpLength);
        var jmpBackTarget = injectionAddress + CreditsHookJumpLength;
        if (!IsRel32Reachable(injectionAddress, CreditsHookJumpLength, candidate) ||
            !IsRel32Reachable(jmpBackSource, CreditsHookJumpLength, jmpBackTarget))
        {
            _memory.Free(candidate);
            return false;
        }

        allocated = candidate;
        return true;
    }

    private static bool IsRel32Reachable(nint instructionAddress, int instructionLength, nint targetAddress)
    {
        var delta = targetAddress.ToInt64() - (instructionAddress.ToInt64() + instructionLength);
        return delta is >= int.MinValue and <= int.MaxValue;
    }

    private ActionExecutionResult EnsureUnitCapHookInstalled(int capValue)
    {
        EnsureAttached();
        if (_memory is null)
        {
            return new ActionExecutionResult(false, "Unit cap hook failed: memory accessor unavailable.", AddressSource.None);
        }

        if (IsUnitCapHookInstalled())
        {
            return UpdateUnitCapHookValue(capValue);
        }

        var resolution = ResolveUnitCapHookInjectionAddress();
        if (!resolution.Succeeded)
        {
            return new ActionExecutionResult(false, resolution.Message, AddressSource.None);
        }

        var injectionAddress = resolution.Address;
        if (TryReadUnitCapInjectionBytes(injectionAddress, out var currentBytes) is { } readFailure)
        {
            return readFailure;
        }

        if (!currentBytes.AsSpan().SequenceEqual(UnitCapHookOriginalBytes))
        {
            return new ActionExecutionResult(
                false,
                $"Unit cap hook refused: injection bytes mismatch at {ToHex(injectionAddress)}. Expected {BitConverter.ToString(UnitCapHookOriginalBytes)} got {BitConverter.ToString(currentBytes)}.",
                AddressSource.None);
        }

        var caveAddress = AllocateExecutableCaveNear(injectionAddress, UnitCapHookCaveSize);
        if (caveAddress == nint.Zero)
        {
            return new ActionExecutionResult(false, "Unit cap hook failed: unable to allocate executable code cave.", AddressSource.None);
        }

        return InstallUnitCapHook(capValue, injectionAddress, currentBytes, caveAddress);
    }

    private bool IsUnitCapHookInstalled()
    {
        return _unitCapHookOriginalBytesBackup is not null &&
               _unitCapHookInjectionAddress != nint.Zero &&
               _unitCapHookCodeCaveAddress != nint.Zero &&
               _unitCapHookValueAddress != nint.Zero;
    }

    private ActionExecutionResult UpdateUnitCapHookValue(int capValue)
    {
        _memory!.Write(_unitCapHookValueAddress, capValue);
        return new ActionExecutionResult(
            true,
            $"Unit cap updated to {capValue}.",
            AddressSource.Signature,
            new Dictionary<string, object?>
            {
                ["hookAddress"] = ToHex(_unitCapHookInjectionAddress),
                ["hookCaveAddress"] = ToHex(_unitCapHookCodeCaveAddress),
                ["unitCapValue"] = capValue,
                [DiagnosticKeyState] = "updated"
            });
    }

    private ActionExecutionResult? TryReadUnitCapInjectionBytes(nint injectionAddress, out byte[] currentBytes)
    {
        currentBytes = Array.Empty<byte>();
        try
        {
            currentBytes = _memory!.ReadBytes(injectionAddress, UnitCapHookOriginalBytes.Length);
            return null;
        }
        catch (Exception ex)
        {
            return new ActionExecutionResult(
                false,
                $"Unit cap hook failed: unable to read injection bytes ({ex.Message}).",
                AddressSource.None);
        }
    }

    private ActionExecutionResult InstallUnitCapHook(int capValue, nint injectionAddress, byte[] currentBytes, nint caveAddress)
    {
        try
        {
            var caveBytes = BuildUnitCapHookCaveBytes(caveAddress, injectionAddress, capValue);
            var jumpPatch = BuildRelativeJumpBytes(injectionAddress, caveAddress);
            _memory!.WriteBytes(caveAddress, caveBytes, executablePatch: true);
            _memory.WriteBytes(injectionAddress, jumpPatch, executablePatch: true);
            _unitCapHookOriginalBytesBackup = currentBytes.ToArray();
            _unitCapHookInjectionAddress = injectionAddress;
            _unitCapHookCodeCaveAddress = caveAddress;
            _unitCapHookValueAddress = caveAddress + 1;
            return new ActionExecutionResult(
                true,
                $"Unit cap hook installed ({capValue}).",
                AddressSource.Signature,
                new Dictionary<string, object?>
                {
                    ["hookAddress"] = ToHex(injectionAddress),
                    ["hookCaveAddress"] = ToHex(caveAddress),
                    ["unitCapValue"] = capValue,
                    [DiagnosticKeyState] = "installed"
                });
        }
        catch (Exception ex)
        {
            TryRestoreBytesAfterHookFailure(injectionAddress, currentBytes);
            _memory!.Free(caveAddress);
            ClearUnitCapHookState();
            return new ActionExecutionResult(false, $"Unit cap hook patch failed: {ex.Message}", AddressSource.None);
        }
    }

    private ActionExecutionResult DisableUnitCapHook()
    {
        if (_memory is null)
        {
            return new ActionExecutionResult(false, "Unit cap hook disable failed: memory accessor unavailable.", AddressSource.None);
        }

        if (_unitCapHookOriginalBytesBackup is null || _unitCapHookInjectionAddress == nint.Zero)
        {
            return new ActionExecutionResult(true, "Unit cap hook is not active.", AddressSource.None,
                new Dictionary<string, object?> { [DiagnosticKeyState] = "not_active" });
        }

        _memory.WriteBytes(_unitCapHookInjectionAddress, _unitCapHookOriginalBytesBackup, executablePatch: true);
        if (_unitCapHookCodeCaveAddress != nint.Zero)
        {
            _memory.Free(_unitCapHookCodeCaveAddress);
        }

        var address = _unitCapHookInjectionAddress;
        ClearUnitCapHookState();
        return new ActionExecutionResult(true, "Unit cap hook disabled and original bytes restored.", AddressSource.Signature,
            new Dictionary<string, object?> { ["hookAddress"] = ToHex(address), [DiagnosticKeyState] = "restored" });
    }

    private ActionExecutionResult EnsureInstantBuildHookInstalled()
    {
        EnsureAttached();
        if (_memory is null)
        {
            return new ActionExecutionResult(false, "Instant build hook failed: memory accessor unavailable.", AddressSource.None);
        }

        if (IsInstantBuildHookInstalled())
        {
            return BuildInstantBuildAlreadyInstalledResult();
        }

        var resolution = ResolveInstantBuildHookInjectionAddress();
        if (!resolution.Succeeded)
        {
            return new ActionExecutionResult(false, resolution.Message, AddressSource.None);
        }

        var injectionAddress = resolution.Address;
        if (TryReadInstantBuildInjectionBytes(injectionAddress, out var currentBytes) is { } readFailure)
        {
            return readFailure;
        }

        if (!currentBytes.AsSpan().SequenceEqual(resolution.OriginalBytes))
        {
            return new ActionExecutionResult(
                false,
                $"Instant build hook refused: injection bytes mismatch at {ToHex(injectionAddress)}. Expected {BitConverter.ToString(resolution.OriginalBytes)} got {BitConverter.ToString(currentBytes)}.",
                AddressSource.None);
        }

        var caveAddress = AllocateExecutableCaveNear(injectionAddress, InstantBuildHookCaveSize);
        if (caveAddress == nint.Zero)
        {
            return new ActionExecutionResult(false, "Instant build hook failed: unable to allocate executable code cave.", AddressSource.None);
        }

        return InstallInstantBuildHook(resolution, injectionAddress, currentBytes, caveAddress);
    }

    private bool IsInstantBuildHookInstalled()
    {
        return _instantBuildHookOriginalBytesBackup is not null &&
               _instantBuildHookInjectionAddress != nint.Zero &&
               _instantBuildHookCodeCaveAddress != nint.Zero;
    }

    private ActionExecutionResult BuildInstantBuildAlreadyInstalledResult()
    {
        return new ActionExecutionResult(
            true,
            "Instant build hook already installed.",
            AddressSource.Signature,
            new Dictionary<string, object?>
            {
                ["hookAddress"] = ToHex(_instantBuildHookInjectionAddress),
                ["hookCaveAddress"] = ToHex(_instantBuildHookCodeCaveAddress),
                [DiagnosticKeyState] = "already_installed"
            });
    }

    private ActionExecutionResult? TryReadInstantBuildInjectionBytes(nint injectionAddress, out byte[] currentBytes)
    {
        currentBytes = Array.Empty<byte>();
        try
        {
            currentBytes = _memory!.ReadBytes(injectionAddress, InstantBuildHookInstructionLength);
            return null;
        }
        catch (Exception ex)
        {
            return new ActionExecutionResult(
                false,
                $"Instant build hook failed: unable to read injection bytes ({ex.Message}).",
                AddressSource.None);
        }
    }

    private ActionExecutionResult InstallInstantBuildHook(
        InstantBuildHookResolution resolution,
        nint injectionAddress,
        byte[] currentBytes,
        nint caveAddress)
    {
        try
        {
            var caveBytes = BuildInstantBuildHookCaveBytes(caveAddress, injectionAddress, resolution.OriginalBytes);
            var jumpPatch = BuildInstantBuildJumpPatchBytes(injectionAddress, caveAddress);
            _memory!.WriteBytes(caveAddress, caveBytes, executablePatch: true);
            _memory.WriteBytes(injectionAddress, jumpPatch, executablePatch: true);
            _instantBuildHookOriginalBytesBackup = currentBytes.ToArray();
            _instantBuildHookInjectionAddress = injectionAddress;
            _instantBuildHookCodeCaveAddress = caveAddress;
            return new ActionExecutionResult(
                true,
                "Instant build hook installed (1 sec / 1 credit).",
                AddressSource.Signature,
                new Dictionary<string, object?>
                {
                    ["hookAddress"] = ToHex(injectionAddress),
                    ["hookCaveAddress"] = ToHex(caveAddress),
                    [DiagnosticKeyState] = "installed"
                });
        }
        catch (Exception ex)
        {
            TryRestoreBytesAfterHookFailure(injectionAddress, currentBytes);
            _memory!.Free(caveAddress);
            ClearInstantBuildHookState();
            return new ActionExecutionResult(false, $"Instant build hook patch failed: {ex.Message}", AddressSource.None);
        }
    }

    private void TryRestoreBytesAfterHookFailure(nint injectionAddress, byte[] originalBytes)
    {
        try
        {
            _memory!.WriteBytes(injectionAddress, originalBytes, executablePatch: true);
        }
        catch
        {
            // Best-effort rollback.
        }
    }

    private ActionExecutionResult DisableInstantBuildHook()
    {
        if (_memory is null)
        {
            return new ActionExecutionResult(false, "Instant build hook disable failed: memory accessor unavailable.", AddressSource.None);
        }

        if (_instantBuildHookOriginalBytesBackup is null || _instantBuildHookInjectionAddress == nint.Zero)
        {
            return new ActionExecutionResult(true, "Instant build hook is not active.", AddressSource.None,
                new Dictionary<string, object?> { [DiagnosticKeyState] = "not_active" });
        }

        _memory.WriteBytes(_instantBuildHookInjectionAddress, _instantBuildHookOriginalBytesBackup, executablePatch: true);
        if (_instantBuildHookCodeCaveAddress != nint.Zero)
        {
            _memory.Free(_instantBuildHookCodeCaveAddress);
        }

        var address = _instantBuildHookInjectionAddress;
        ClearInstantBuildHookState();
        return new ActionExecutionResult(true, "Instant build hook disabled and original bytes restored.", AddressSource.Signature,
            new Dictionary<string, object?> { ["hookAddress"] = ToHex(address), [DiagnosticKeyState] = "restored" });
    }

    private UnitCapHookResolution ResolveUnitCapHookInjectionAddress()
    {
        EnsureAttached();
        if (_memory is null || CurrentSession is null)
        {
            return UnitCapHookResolution.Fail("No active process for unit cap hook resolution.");
        }

        try
        {
            using var process = Process.GetProcessById(CurrentSession.Process.ProcessId);
            var module = process.MainModule;
            if (module is null)
            {
                return UnitCapHookResolution.Fail("Main module is unavailable for unit cap hook resolution.");
            }

            var baseAddress = module.BaseAddress;
            var moduleBytes = _memory.ReadBytes(baseAddress, module.ModuleMemorySize);
            var pattern = AobPattern.Parse(UnitCapHookPatternText);
            var hits = FindPatternOffsets(moduleBytes, pattern, maxHits: 3);
            if (hits.Count == 1)
            {
                return UnitCapHookResolution.Ok(baseAddress + hits[0]);
            }

            if (hits.Count == 0)
            {
                return UnitCapHookResolution.Fail("Unit cap hook pattern not found.");
            }

            return UnitCapHookResolution.Fail($"Unit cap hook pattern not unique (hits={hits.Count}).");
        }
        catch (Exception ex)
        {
            return UnitCapHookResolution.Fail($"Unit cap hook resolution failed: {ex.Message}");
        }
    }

    private InstantBuildHookResolution ResolveInstantBuildHookInjectionAddress()
    {
        EnsureAttached();
        if (_memory is null || CurrentSession is null)
        {
            return InstantBuildHookResolution.Fail("No active process for instant build hook resolution.");
        }

        try
        {
            using var process = Process.GetProcessById(CurrentSession.Process.ProcessId);
            var module = process.MainModule;
            if (module is null)
            {
                return InstantBuildHookResolution.Fail("Main module is unavailable for instant build hook resolution.");
            }

            var baseAddress = module.BaseAddress;
            var moduleBytes = _memory.ReadBytes(baseAddress, module.ModuleMemorySize);

            foreach (var patternBytes in InstantBuildHookPatterns)
            {
                var pattern = AobPattern.Parse(BitConverter.ToString(patternBytes).Replace("-", " "));
                var hits = FindPatternOffsets(moduleBytes, pattern, maxHits: 3);
                if (hits.Count == 1)
                {
                    return InstantBuildHookResolution.Ok(baseAddress + hits[0], patternBytes);
                }
            }

            return InstantBuildHookResolution.Fail("Instant build hook pattern not found or not unique.");
        }
        catch (Exception ex)
        {
            return InstantBuildHookResolution.Fail($"Instant build hook resolution failed: {ex.Message}");
        }
    }

    private static byte[] BuildUnitCapHookCaveBytes(nint caveAddress, nint injectionAddress, int capValue)
    {
        var bytes = new byte[UnitCapHookCaveSize];

        // mov edi, imm32
        bytes[0] = 0xBF;
        WriteInt32(bytes, 1, capValue);

        // original bytes: mov rsi, [rsp+68]
        bytes[5] = 0x48;
        bytes[6] = 0x8B;
        bytes[7] = 0x74;
        bytes[8] = 0x24;
        bytes[9] = 0x68;

        // jmp back to injection + 5
        bytes[10] = 0xE9;
        WriteInt32(
            bytes,
            11,
            ComputeRelativeDisplacement(
                caveAddress + UnitCapHookCaveSize,
                injectionAddress + UnitCapHookJumpLength));

        return bytes;
    }

    private static byte[] BuildInstantBuildHookCaveBytes(nint caveAddress, nint injectionAddress, byte[] originalBytes)
    {
        var bytes = new byte[InstantBuildHookCaveSize];
        var offset = 0;

        // mov [rbx+00000904], 1
        bytes[offset++] = 0xC7;
        bytes[offset++] = 0x83;
        bytes[offset++] = 0x04;
        bytes[offset++] = 0x09;
        bytes[offset++] = 0x00;
        bytes[offset++] = 0x00;
        WriteInt32(bytes, offset, 1);
        offset += 4;

        // mov [rbx+00000908], 1
        bytes[offset++] = 0xC7;
        bytes[offset++] = 0x83;
        bytes[offset++] = 0x08;
        bytes[offset++] = 0x09;
        bytes[offset++] = 0x00;
        bytes[offset++] = 0x00;
        WriteInt32(bytes, offset, 1);
        offset += 4;

        // original instruction: mov eax,[rbx+00000904] (or variant)
        Array.Copy(originalBytes, 0, bytes, offset, originalBytes.Length);
        offset += originalBytes.Length;

        // jmp back to injection + 6
        bytes[offset++] = 0xE9;
        WriteInt32(
            bytes,
            offset,
            ComputeRelativeDisplacement(
                caveAddress + InstantBuildHookCaveSize,
                injectionAddress + InstantBuildHookInstructionLength));

        return bytes;
    }

    private static byte[] BuildInstantBuildJumpPatchBytes(nint instructionAddress, nint targetAddress)
    {
        var jump = BuildRelativeJumpBytes(instructionAddress, targetAddress);
        if (jump.Length != InstantBuildHookJumpLength)
        {
            throw new InvalidOperationException("Instant build jump length mismatch.");
        }

        return [jump[0], jump[1], jump[2], jump[3], jump[4], 0x90];
    }

    private static byte[] BuildCreditsHookCaveBytes(
        nint caveAddress,
        nint injectionAddress,
        byte[] originalInstruction,
        byte contextOffset,
        byte destinationReg)
    {
        ValidateCreditsHookCaveInputs(originalInstruction, destinationReg);
        var bytes = new byte[CreditsHookCaveSize];
        WriteCreditsHookContextCapture(bytes, caveAddress);
        WriteCreditsHookLockGate(bytes, caveAddress);
        WriteCreditsHookForcedWrite(bytes, caveAddress, contextOffset, destinationReg);
        WriteCreditsHookReturn(bytes, caveAddress, injectionAddress, originalInstruction);
        return bytes;
    }

    private static void ValidateCreditsHookCaveInputs(byte[] originalInstruction, byte destinationReg)
    {
        if (originalInstruction.Length != CreditsHookJumpLength)
        {
            throw new InvalidOperationException("Credits hook expected a 5-byte cvttss2si instruction.");
        }

        if (destinationReg > 7)
        {
            throw new InvalidOperationException($"Credits hook destination register out of range: {destinationReg}");
        }
    }

    private static void WriteCreditsHookContextCapture(byte[] bytes, nint caveAddress)
    {
        bytes[0] = 0x48;
        bytes[1] = 0x89;
        bytes[2] = 0x05;
        WriteInt32(bytes, 3, ComputeRelativeDisplacement(caveAddress + 7, caveAddress + CreditsHookDataLastContextOffset));

        bytes[7] = 0xFF;
        bytes[8] = 0x05;
        WriteInt32(bytes, 9, ComputeRelativeDisplacement(caveAddress + 13, caveAddress + CreditsHookDataHitCountOffset));
    }

    private static void WriteCreditsHookLockGate(byte[] bytes, nint caveAddress)
    {
        bytes[13] = 0x83;
        bytes[14] = 0x3D;
        WriteInt32(bytes, 15, ComputeRelativeDisplacement(caveAddress + 20, caveAddress + CreditsHookDataLockEnabledOffset));
        bytes[19] = 0x00;
        bytes[20] = 0x74;
        bytes[21] = 0x09;
    }

    private static void WriteCreditsHookForcedWrite(byte[] bytes, nint caveAddress, byte contextOffset, byte destinationReg)
    {
        bytes[22] = 0x8B;
        bytes[23] = (byte)(0x05 | (destinationReg << 3));
        WriteInt32(bytes, 24, ComputeRelativeDisplacement(caveAddress + 28, caveAddress + CreditsHookDataForcedFloatBitsOffset));
        bytes[28] = 0x89;
        bytes[29] = (byte)(0x40 | (destinationReg << 3));
        bytes[30] = contextOffset;
    }

    private static void WriteCreditsHookReturn(byte[] bytes, nint caveAddress, nint injectionAddress, byte[] originalInstruction)
    {
        Array.Copy(originalInstruction, 0, bytes, 31, originalInstruction.Length);
        bytes[36] = 0xE9;
        WriteInt32(
            bytes,
            37,
            ComputeRelativeDisplacement(caveAddress + CreditsHookCodeSize, injectionAddress + CreditsHookJumpLength));
    }

    private static byte[] BuildRelativeJumpBytes(nint instructionAddress, nint targetAddress)
    {
        var bytes = new byte[CreditsHookJumpLength];
        bytes[0] = 0xE9;
        WriteInt32(
            bytes,
            1,
            ComputeRelativeDisplacement(
                instructionAddress + CreditsHookJumpLength,
                targetAddress));
        return bytes;
    }

    private static int ComputeRelativeDisplacement(nint nextInstructionAddress, nint targetAddress)
    {
        var delta = targetAddress.ToInt64() - nextInstructionAddress.ToInt64();
        if (delta < int.MinValue || delta > int.MaxValue)
        {
            throw new InvalidOperationException("Relative jump/call target is out of rel32 range.");
        }

        return (int)delta;
    }

    private static void WriteInt32(byte[] destination, int offset, int value)
    {
        destination[offset] = (byte)(value & 0xFF);
        destination[offset + 1] = (byte)((value >> 8) & 0xFF);
        destination[offset + 2] = (byte)((value >> 16) & 0xFF);
        destination[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private CreditsHookResolution ResolveCreditsHookInjectionAddress()
    {
        EnsureAttached();
        if (_memory is null || CurrentSession is null)
        {
            return CreditsHookResolution.Fail("No active process for credits hook resolution.");
        }

        try
        {
            if (!TryLoadCreditsHookModuleData(CurrentSession.Process.ProcessId, out var baseAddress, out var moduleBytes, out var moduleFailure))
            {
                return CreditsHookResolution.Fail(moduleFailure!);
            }

            var creditsRva = TryResolveCreditsRva(baseAddress);
            if (TryResolveCreditsHookExactPattern(baseAddress, moduleBytes) is { } exactResolution)
            {
                return exactResolution;
            }

            var allCandidates = FindAllCreditsHookCandidates(moduleBytes);
            return ResolveCreditsHookFromCandidateSet(baseAddress, moduleBytes, allCandidates, creditsRva);
        }
        catch (Exception ex)
        {
            return CreditsHookResolution.Fail($"Credits hook pattern resolution failed: {ex.Message}");
        }
    }

    private bool TryLoadCreditsHookModuleData(
        int processId,
        out nint baseAddress,
        out byte[] moduleBytes,
        out string? failureMessage)
    {
        baseAddress = nint.Zero;
        moduleBytes = Array.Empty<byte>();
        failureMessage = null;
        using var process = Process.GetProcessById(processId);
        var module = process.MainModule;
        if (module is null)
        {
            failureMessage = "Main module is unavailable for credits hook resolution.";
            return false;
        }

        baseAddress = module.BaseAddress;
        moduleBytes = _memory!.ReadBytes(baseAddress, module.ModuleMemorySize);
        return true;
    }

    private long TryResolveCreditsRva(nint baseAddress)
    {
        try
        {
            var creditsSymbol = ResolveSymbol(SymbolCredits);
            return creditsSymbol.Address.ToInt64() - baseAddress.ToInt64();
        }
        catch
        {
            return -1;
        }
    }

    private CreditsHookResolution? TryResolveCreditsHookExactPattern(nint baseAddress, byte[] moduleBytes)
    {
        var exactPattern = AobPattern.Parse(CreditsHookPatternText);
        var exactHits = FindPatternOffsets(moduleBytes, exactPattern, maxHits: 3);
        var exactCandidates = ParseCreditsHookCandidates(moduleBytes, exactHits);
        return TryResolveSingleCreditsHookCandidate(
            baseAddress,
            exactCandidates,
            "Credits hook: exact-pattern candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}");
    }

    private static List<CreditsHookCandidate> FindAllCreditsHookCandidates(byte[] moduleBytes)
    {
        var broadConvertPattern = AobPattern.Parse("F3 0F 2C ?? ??");
        var broadConvertHits = FindPatternOffsets(moduleBytes, broadConvertPattern, maxHits: 8000);
        return ParseCreditsHookCandidates(moduleBytes, broadConvertHits);
    }

    private CreditsHookResolution ResolveCreditsHookFromCandidateSet(
        nint baseAddress,
        byte[] moduleBytes,
        List<CreditsHookCandidate> allCandidates,
        long creditsRva)
    {
        var immediateStoreCandidates = SelectImmediateStoreCandidates(moduleBytes, allCandidates);
        if (TryResolveSingleCreditsHookCandidate(
                baseAddress,
                immediateStoreCandidates,
                "Credits hook: selected immediate-store candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}") is { } immediateResolution)
        {
            return immediateResolution;
        }

        if (TryResolveClassicOffsetPreference(
                baseAddress,
                immediateStoreCandidates,
                "Credits hook: selected classic-offset immediate-store candidate at RVA 0x{Rva:X}, reg={Reg}") is { } classicImmediateResolution)
        {
            return classicImmediateResolution;
        }

        if (TryResolveCreditsHookCorrelatedCandidate(baseAddress, moduleBytes, allCandidates, creditsRva) is { } correlatedResolution)
        {
            return correlatedResolution;
        }

        var classicOffsetCandidates = SelectClassicOffsetCandidates(allCandidates);
        if (TryResolveSingleCreditsHookCandidate(
                baseAddress,
                classicOffsetCandidates,
                "Credits hook: selected unique classic-offset candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}") is { } classicResolution)
        {
            return classicResolution;
        }

        if (TryResolveSingleCreditsHookCandidate(
                baseAddress,
                allCandidates,
                "Credits hook: selected unique fallback candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}") is { } fallbackResolution)
        {
            return fallbackResolution;
        }

        LogCreditsHookCandidateSummary(allCandidates.Count, immediateStoreCandidates.Count, classicOffsetCandidates.Count, creditsRva);
        return BuildCreditsHookPatternNotFoundResult(allCandidates.Count, immediateStoreCandidates.Count, classicOffsetCandidates.Count, creditsRva);
    }

    private static List<CreditsHookCandidate> ParseCreditsHookCandidates(byte[] moduleBytes, IEnumerable<int> offsets)
    {
        var candidates = new List<CreditsHookCandidate>();
        foreach (var offset in offsets)
        {
            if (TryParseCreditsCvttss2siInstruction(moduleBytes, offset, out var instruction))
            {
                candidates.Add(new CreditsHookCandidate(offset, instruction));
            }
        }

        return candidates;
    }

    private static List<CreditsHookCandidate> SelectImmediateStoreCandidates(
        byte[] moduleBytes,
        IEnumerable<CreditsHookCandidate> allCandidates)
    {
        return allCandidates
            .Where(c => LooksLikeImmediateStoreFromConvertedRegister(
                moduleBytes,
                c.Offset + CreditsHookJumpLength,
                c.Instruction.DestinationReg))
            .ToList();
    }

    private static List<CreditsHookCandidate> SelectClassicOffsetCandidates(IEnumerable<CreditsHookCandidate> candidates)
    {
        return candidates.Where(c => c.Instruction.ContextOffset == CreditsContextOffsetByte).ToList();
    }

    private CreditsHookResolution? TryResolveCreditsHookCorrelatedCandidate(
        nint baseAddress,
        byte[] moduleBytes,
        List<CreditsHookCandidate> allCandidates,
        long creditsRva)
    {
        if (creditsRva <= 0)
        {
            return null;
        }

        var correlatedCandidates = allCandidates
            .Where(c => HasNearbyStoreToCreditsRva(
                moduleBytes,
                c.Offset + CreditsHookJumpLength,
                CreditsStoreCorrelationWindowBytes,
                creditsRva))
            .ToList();
        if (TryResolveSingleCreditsHookCandidate(
                baseAddress,
                correlatedCandidates,
                "Credits hook: selected correlated candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}") is { } correlatedResolution)
        {
            return correlatedResolution;
        }

        return TryResolveClassicOffsetPreference(
            baseAddress,
            correlatedCandidates,
            "Credits hook: selected classic-offset correlated candidate at RVA 0x{Rva:X}, reg={Reg}");
    }

    private CreditsHookResolution? TryResolveSingleCreditsHookCandidate(
        nint baseAddress,
        List<CreditsHookCandidate> candidates,
        string logTemplate)
    {
        if (candidates.Count != 1)
        {
            return null;
        }

        var candidate = candidates[0];
        _logger.LogInformation(
            logTemplate,
            candidate.Offset,
            candidate.Instruction.ContextOffset,
            candidate.Instruction.DestinationReg);
        return BuildCreditsHookResolution(baseAddress, candidate);
    }

    private CreditsHookResolution? TryResolveClassicOffsetPreference(
        nint baseAddress,
        List<CreditsHookCandidate> candidates,
        string logTemplate)
    {
        var preferredClassic = SelectClassicOffsetCandidates(candidates);
        if (preferredClassic.Count != 1)
        {
            return null;
        }

        var candidate = preferredClassic[0];
        _logger.LogInformation(logTemplate, candidate.Offset, candidate.Instruction.DestinationReg);
        return BuildCreditsHookResolution(baseAddress, candidate);
    }

    private static CreditsHookResolution BuildCreditsHookResolution(nint baseAddress, CreditsHookCandidate candidate)
    {
        return CreditsHookResolution.Ok(
            baseAddress + candidate.Offset,
            candidate.Instruction.ContextOffset,
            candidate.Instruction.DestinationReg,
            candidate.Instruction.OriginalBytes);
    }

    private void LogCreditsHookCandidateSummary(int total, int immediateStore, int classicOffset, long creditsRva)
    {
        _logger.LogWarning(
            "Credits hook: candidates total={Total}, immediateStore={Immediate}, classicOffset={Classic}, creditsRva={Rva}",
            total,
            immediateStore,
            classicOffset,
            creditsRva > 0 ? $"0x{creditsRva:X}" : "unavailable");
    }

    private static CreditsHookResolution BuildCreditsHookPatternNotFoundResult(
        int totalCandidates,
        int immediateStoreCandidates,
        int classicOffsetCandidates,
        long creditsRva)
    {
        return CreditsHookResolution.Fail(
            $"Credits hook pattern not found. Tried exact ({CreditsHookPatternText}), " +
            $"register-agnostic immediate-store heuristics, and credits-RVA correlation scan. " +
            $"Candidates: total={totalCandidates}, immediateStore={immediateStoreCandidates}, classicOffset={classicOffsetCandidates}. " +
            (creditsRva > 0
                ? $"Credits int RVA=0x{creditsRva:X} was used for correlation."
                : "Credits RVA unavailable for correlation."));
    }

    private static bool TryParseCreditsCvttss2siInstruction(
        byte[] module,
        int offset,
        out CreditsCvttss2siInstruction instruction)
    {
        instruction = new CreditsCvttss2siInstruction(0, 0, Array.Empty<byte>());
        if (offset < 0 || offset + CreditsHookJumpLength > module.Length)
        {
            return false;
        }

        if (module[offset] != 0xF3 || module[offset + 1] != 0x0F || module[offset + 2] != 0x2C)
        {
            return false;
        }

        var modrm = module[offset + 3];
        var mod = (modrm >> 6) & 0x3;
        var rm = modrm & 0x7;
        if (mod != 0x1 || rm != 0x0)
        {
            return false;
        }

        var destinationReg = (byte)((modrm >> 3) & 0x7);
        var contextOffset = module[offset + 4];
        instruction = new CreditsCvttss2siInstruction(
            contextOffset,
            destinationReg,
            module.AsSpan(offset, CreditsHookJumpLength).ToArray());
        return true;
    }

    private static IReadOnlyList<int> FindPatternOffsets(byte[] memory, AobPattern pattern, int maxHits)
    {
        var results = new List<int>(Math.Min(maxHits, 4));
        var signature = pattern.Bytes;
        if (signature.Length == 0 || maxHits <= 0)
        {
            return results;
        }

        var maxIndex = memory.Length - signature.Length;
        for (var i = 0; i <= maxIndex; i++)
        {
            if (!IsPatternMatchAtOffset(memory, signature, i))
            {
                continue;
            }

            results.Add(i);
            if (results.Count >= maxHits)
            {
                break;
            }
        }

        return results;
    }

    private static bool IsPatternMatchAtOffset(byte[] memory, byte?[] signature, int offset)
    {
        for (var j = 0; j < signature.Length; j++)
        {
            var expected = signature[j];
            if (expected.HasValue && memory[offset + j] != expected.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check if there's a RIP-relative store instruction (89 ModRM disp32) within the next
    /// <paramref name="searchLen"/> bytes that targets the given credits RVA.
    /// This correlates a cvttss2si instruction with the store that writes the converted int.
    /// </summary>
    private static bool HasNearbyStoreToCreditsRva(byte[] module, int startOffset, int searchLen, long creditsRva)
    {
        for (var i = startOffset; i < startOffset + searchLen && i + 6 < module.Length; i++)
        {
            // mov [rip+disp32], reg32 — opcode 89, ModRM with mod=00, rm=101 (RIP-relative)
            if (module[i] == 0x89)
            {
                var modrm = module[i + 1];
                if ((modrm & 0xC7) == 0x05)
                {
                    var disp = BitConverter.ToInt32(module, i + 2);
                    var nextRip = (long)(i + 6);
                    if (nextRip + disp == creditsRva) return true;
                }
            }
        }

        return false;
    }

    private static bool LooksLikeImmediateStoreFromConvertedRegister(byte[] module, int offset, byte registerIndex)
    {
        if (offset + 1 >= module.Length || module[offset] != 0x89)
        {
            return false;
        }

        var modrm = module[offset + 1];
        var mod = (modrm >> 6) & 0x3;
        var reg = (modrm >> 3) & 0x7;
        if (reg != registerIndex || mod == 0x3)
        {
            return false;
        }

        // reg32 -> [mem] store with any base/disp form.
        return true;
    }

    private void TryRestoreCodePatchesOnDetach()
    {
        if (_memory is null || _activeCodePatches.Count == 0)
        {
            return;
        }

        foreach (var (symbol, (address, originalBytes)) in _activeCodePatches)
        {
            try
            {
                _memory.WriteBytes(address, originalBytes, executablePatch: true);
                _logger.LogInformation("Restored code patch '{Symbol}' at {Address} on detach.", symbol, $"0x{address.ToInt64():X}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore code patch '{Symbol}' at detach.", symbol);
            }
        }

        _activeCodePatches.Clear();
    }

    private void TryRestoreCreditsHookOnDetach()
    {
        if (_memory is null)
        {
            return;
        }

        if (_creditsHookOriginalBytesBackup is not null && _creditsHookInjectionAddress != nint.Zero)
        {
            try
            {
                _memory.WriteBytes(_creditsHookInjectionAddress, _creditsHookOriginalBytesBackup, executablePatch: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore credits hook bytes at detach.");
            }
        }

        if (_creditsHookCodeCaveAddress != nint.Zero)
        {
            try
            {
                if (!_memory.Free(_creditsHookCodeCaveAddress))
                {
                    _logger.LogWarning("Failed to free credits hook code cave at {Address}.", ToHex(_creditsHookCodeCaveAddress));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed while freeing credits hook code cave.");
            }
        }
    }

    private void ClearCreditsHookState()
    {
        _creditsHookInjectionAddress = nint.Zero;
        _creditsHookOriginalBytesBackup = null;
        _creditsHookCodeCaveAddress = nint.Zero;
        _creditsHookLastContextAddress = nint.Zero;
        _creditsHookHitCountAddress = nint.Zero;
        _creditsHookLockEnabledAddress = nint.Zero;
        _creditsHookForcedFloatBitsAddress = nint.Zero;
        _creditsHookContextOffset = CreditsContextOffsetByte;
    }

    private void TryRestoreUnitCapHookOnDetach()
    {
        if (_memory is null)
        {
            return;
        }

        if (_unitCapHookOriginalBytesBackup is not null && _unitCapHookInjectionAddress != nint.Zero)
        {
            try
            {
                _memory.WriteBytes(_unitCapHookInjectionAddress, _unitCapHookOriginalBytesBackup, executablePatch: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore unit cap hook bytes at detach.");
            }
        }

        if (_unitCapHookCodeCaveAddress != nint.Zero)
        {
            try
            {
                if (!_memory.Free(_unitCapHookCodeCaveAddress))
                {
                    _logger.LogWarning("Failed to free unit cap hook code cave at {Address}.", ToHex(_unitCapHookCodeCaveAddress));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed while freeing unit cap hook code cave.");
            }
        }
    }

    private void ClearUnitCapHookState()
    {
        _unitCapHookInjectionAddress = nint.Zero;
        _unitCapHookOriginalBytesBackup = null;
        _unitCapHookCodeCaveAddress = nint.Zero;
        _unitCapHookValueAddress = nint.Zero;
    }

    private void TryRestoreInstantBuildHookOnDetach()
    {
        if (_memory is null)
        {
            return;
        }

        if (_instantBuildHookOriginalBytesBackup is not null && _instantBuildHookInjectionAddress != nint.Zero)
        {
            try
            {
                _memory.WriteBytes(_instantBuildHookInjectionAddress, _instantBuildHookOriginalBytesBackup, executablePatch: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore instant build hook bytes at detach.");
            }
        }

        if (_instantBuildHookCodeCaveAddress != nint.Zero)
        {
            try
            {
                if (!_memory.Free(_instantBuildHookCodeCaveAddress))
                {
                    _logger.LogWarning("Failed to free instant build hook code cave at {Address}.", ToHex(_instantBuildHookCodeCaveAddress));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed while freeing instant build hook code cave.");
            }
        }
    }

    private void ClearInstantBuildHookState()
    {
        _instantBuildHookInjectionAddress = nint.Zero;
        _instantBuildHookOriginalBytesBackup = null;
        _instantBuildHookCodeCaveAddress = nint.Zero;
    }

    private static bool IsCreditsWrite(ActionExecutionRequest request, string symbol)
    {
        return request.Action.Id.Equals(ActionIdSetCredits, StringComparison.OrdinalIgnoreCase) ||
               symbol.Equals(SymbolCredits, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadBooleanPayload(JsonObject payload, string key, out bool value)
    {
        value = false;
        if (!payload.TryGetPropertyValue(key, out var node) || node is null)
        {
            return false;
        }

        try
        {
            value = node.GetValue<bool>();
            return true;
        }
        catch
        {
            // ignored
        }

        try
        {
            var asInt = node.GetValue<int>();
            value = asInt != 0;
            return true;
        }
        catch
        {
            // ignored
        }

        try
        {
            var asString = node.GetValue<string>();
            if (bool.TryParse(asString, out var parsed))
            {
                value = parsed;
                return true;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private void RecordActionTelemetry(ActionExecutionRequest request, ActionExecutionResult result)
    {
        var profileId = string.IsNullOrWhiteSpace(request.ProfileId)
            ? _attachedProfile?.Id ?? "unknown_profile"
            : request.ProfileId;
        var actionKey = $"{profileId}:{request.Action.Id}";
        var sourceKey = $"{profileId}:{result.AddressSource}";
        var sourceGlobalKey = $"global:{result.AddressSource}";

        lock (_telemetryLock)
        {
            IncrementCounter(_symbolSourceCounters, sourceGlobalKey);
            IncrementCounter(_symbolSourceCounters, sourceKey);
            if (result.Succeeded)
            {
                IncrementCounter(_actionSuccessCounters, actionKey);
            }
            else
            {
                IncrementCounter(_actionFailureCounters, actionKey);
            }
        }
    }

    private static void IncrementCounter(Dictionary<string, int> counters, string key)
    {
        if (counters.TryGetValue(key, out var existing))
        {
            counters[key] = existing + 1;
            return;
        }

        counters[key] = 1;
    }

    private static string ToHex(nint address) => $"0x{address.ToInt64():X}";

    private sealed record CodePatchActionContext(
        string Symbol,
        bool Enable,
        byte[] PatchBytes,
        byte[] OriginalBytes,
        SymbolInfo SymbolInfo,
        nint Address);

    private readonly record struct ReResolveContext(AttachSession Session, TrainerProfile Profile);

    private readonly record struct WriteAttemptResult<T>(
        bool Success,
        string ReasonCode,
        string Message,
        bool HasObservedValue,
        T ObservedValue)
    {
        public static WriteAttemptResult<T> SuccessWithoutObservation() =>
            new(true, "ok", string.Empty, false, default!);

        public static WriteAttemptResult<T> SuccessWithObservation(T observedValue) =>
            new(true, "ok", string.Empty, true, observedValue);
    }

    private sealed record CreditsWritePulseState(bool HookTickObserved, nint CreditsFloatAddress);

    private sealed record CreditsHookInstallContext(
        nint InjectionAddress,
        nint CaveAddress,
        byte[] CurrentBytes,
        byte[] ExpectedOriginalBytes);

    private sealed record UnitCapHookResolution(nint Address, string Message)
    {
        public bool Succeeded => Address != nint.Zero;

        public static UnitCapHookResolution Ok(nint address) => new(address, string.Empty);

        public static UnitCapHookResolution Fail(string message) => new(nint.Zero, message);
    }

    private sealed record InstantBuildHookResolution(nint Address, byte[] OriginalBytes, string Message)
    {
        public bool Succeeded => Address != nint.Zero;

        public static InstantBuildHookResolution Ok(nint address, byte[] originalBytes) => new(address, originalBytes, string.Empty);

        public static InstantBuildHookResolution Fail(string message) => new(nint.Zero, Array.Empty<byte>(), message);
    }

    private sealed record CreditsHookResolution(
        nint Address,
        string Message,
        byte DetectedOffset = 0x70,
        byte DestinationReg = 0x2,
        byte[]? OriginalInstruction = null)
    {
        public bool Succeeded => Address != nint.Zero;

        public static CreditsHookResolution Ok(
            nint address,
            byte detectedOffset,
            byte destinationReg,
            byte[] originalInstruction) => new(
                address,
                string.Empty,
                detectedOffset,
                destinationReg,
                originalInstruction);

        public static CreditsHookResolution Fail(string message) => new(nint.Zero, message);
    }

    private sealed record CreditsCvttss2siInstruction(
        byte ContextOffset,
        byte DestinationReg,
        byte[] OriginalBytes);

    private sealed record CreditsHookCandidate(int Offset, CreditsCvttss2siInstruction Instruction);

    private sealed record CreditsHookPatchResult(
        bool Succeeded,
        string Message,
        AddressSource AddressSource,
        IReadOnlyDictionary<string, object?>? Diagnostics)
    {
        public static CreditsHookPatchResult Ok(string message, IReadOnlyDictionary<string, object?> diagnostics) =>
            new(true, message, AddressSource.Signature, diagnostics);

        public static CreditsHookPatchResult Fail(string message) =>
            new(false, message, AddressSource.None, null);
    }

    private sealed record CreditsHookTickObservation(bool Observed, int HitCount);

    private void EnsureAttached()
    {
        if (!IsAttached || _memory is null || CurrentSession is null)
        {
            throw new InvalidOperationException("Runtime adapter is not attached.");
        }
    }

    private SymbolInfo ResolveSymbol(string symbol)
    {
        if (CurrentSession is null || !CurrentSession.Symbols.TryGetValue(symbol, out var info) || info is null)
        {
            throw new KeyNotFoundException($"Symbol '{symbol}' was not resolved for current session.");
        }

        if (info.Address == nint.Zero || info.HealthStatus == SymbolHealthStatus.Unresolved)
        {
            throw new KeyNotFoundException($"Symbol '{symbol}' is unresolved for current session.");
        }

        return info;
    }
}
