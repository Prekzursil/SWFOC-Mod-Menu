using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private readonly IProcessLocator _processLocator;
    private readonly IProfileRepository _profileRepository;
    private readonly ISignatureResolver _signatureResolver;
    private readonly IModDependencyValidator _modDependencyValidator;
    private readonly ISymbolHealthService _symbolHealthService;
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
        IModDependencyValidator? modDependencyValidator,
        ISymbolHealthService? symbolHealthService,
        ILogger<RuntimeAdapter> logger)
    {
        _processLocator = processLocator;
        _profileRepository = profileRepository;
        _signatureResolver = signatureResolver;
        _modDependencyValidator = modDependencyValidator ?? new ModDependencyValidator();
        _symbolHealthService = symbolHealthService ?? new SymbolHealthService();
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
        : this(processLocator, profileRepository, signatureResolver, null, null, logger)
    {
    }

    public bool IsAttached => CurrentSession is not null;

    public AttachSession? CurrentSession { get; private set; }

    public async Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (IsAttached)
        {
            return CurrentSession!;
        }

        var profile = await _profileRepository.ResolveInheritedProfileAsync(profileId, cancellationToken);
        _attachedProfile = profile;
        var process = await SelectProcessForProfileAsync(profile, cancellationToken)
            ?? throw new InvalidOperationException($"No running process found for target {profile.ExeTarget}");

        var dependencyValidation = _modDependencyValidator.Validate(profile, process);
        _dependencyValidationStatus = dependencyValidation.Status;
        _dependencyValidationMessage = dependencyValidation.Message;
        _dependencySoftDisabledActions = dependencyValidation.DisabledActionIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(dependencyValidation.DisabledActionIds, StringComparer.OrdinalIgnoreCase);

        if (dependencyValidation.Status == DependencyValidationStatus.HardFail)
        {
            throw new InvalidOperationException(dependencyValidation.Message);
        }

        process = AttachDependencyDiagnostics(process, dependencyValidation);

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
        process = AttachSymbolHealthDiagnostics(process, symbols);

        var calibrationArtifactPath = TryEmitCalibrationSnapshot(profile, process, build, symbols);
        if (!string.IsNullOrWhiteSpace(calibrationArtifactPath))
        {
            process = AttachMetadataValue(process, "calibrationArtifactPath", calibrationArtifactPath);
        }

        _memory = new ProcessMemoryAccessor(process.ProcessId);
        ClearCreditsHookState();
        ClearUnitCapHookState();
        ClearInstantBuildHookState();
        CurrentSession = new AttachSession(profileId, process, build, symbols, DateTimeOffset.UtcNow);
        _logger.LogInformation("Attached to process {Pid} for profile {Profile}", process.ProcessId, profileId);
        return CurrentSession;
    }

    private async Task<ProcessMetadata?> SelectProcessForProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        var processes = await _processLocator.FindSupportedProcessesAsync(cancellationToken);
        var matches = processes.Where(x => x.ExeTarget == profile.ExeTarget).ToArray();
        if (matches.Length == 0)
        {
            // StarWarsG.exe can be ambiguous (or misclassified when command-line is unavailable).
            // Prefer profile-aware fallback instead of hard-failing attach.
            matches = processes
                .Where(IsStarWarsGProcess)
                .ToArray();

            if (matches.Length > 0)
            {
                _logger.LogInformation(
                    "No direct process match for target {Target}; using StarWarsG fallback candidates ({Count}).",
                    profile.ExeTarget, matches.Length);
            }
        }

        if (matches.Length == 0)
        {
            return null;
        }

        var requiredWorkshopIds = CollectRequiredWorkshopIds(profile);
        var strictWorkshopMatches = requiredWorkshopIds.Count == 0
            ? Array.Empty<ProcessMetadata>()
            : matches.Where(x => requiredWorkshopIds.All(id => ProcessContainsWorkshopId(x, id))).ToArray();
        var looseWorkshopMatches = requiredWorkshopIds.Count == 0
            ? Array.Empty<ProcessMetadata>()
            : matches.Where(x => requiredWorkshopIds.Any(id => ProcessContainsWorkshopId(x, id))).ToArray();

        var pool = strictWorkshopMatches.Length > 0
            ? strictWorkshopMatches
            : looseWorkshopMatches.Length > 0
                ? looseWorkshopMatches
                : matches;

        var recommendedMatches = pool
            .Where(x => x.LaunchContext?.Recommendation.ProfileId?.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        if (recommendedMatches.Length > 0)
        {
            pool = recommendedMatches;
        }

        // For FoC, StarWarsG is typically the real game host while swfoc.exe can be a thin launcher.
        // Prefer StarWarsG whenever both are present.
        if (profile.ExeTarget == ExeTarget.Swfoc)
        {
            var starWarsGCandidates = pool.Where(IsStarWarsGProcess).ToArray();
            if (starWarsGCandidates.Length > 0)
            {
                pool = starWarsGCandidates;
            }
        }

        if (pool.Length == 1)
        {
            return pool[0];
        }

        var ranked = pool
            .Select(candidate => new
            {
                Process = candidate,
                WorkshopMatchCount = requiredWorkshopIds.Count == 0
                    ? 0
                    : requiredWorkshopIds.Count(id => ProcessContainsWorkshopId(candidate, id)),
                RecommendationMatch = candidate.LaunchContext?.Recommendation.ProfileId?.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true,
                MainModuleSize = TryGetMainModuleSize(candidate.ProcessId),
                HasCommandLine = !string.IsNullOrWhiteSpace(candidate.CommandLine)
            })
            .OrderByDescending(x => x.WorkshopMatchCount)
            .ThenByDescending(x => x.RecommendationMatch)
            .ThenByDescending(x => x.MainModuleSize)
            .ThenByDescending(x => x.HasCommandLine)
            .ToArray();

        var selected = ranked[0].Process;
        _logger.LogInformation(
            "Selected process {Pid} ({Name}) for profile {Profile}. Candidates={Count}, chosenModuleSize=0x{ModuleSize:X}",
            selected.ProcessId,
            selected.ProcessName,
            profile.Id,
            ranked.Length,
            ranked[0].MainModuleSize);
        return selected;
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
            var now = DateTimeOffset.UtcNow;
            var safeTimestamp = now.ToString("yyyyMMdd_HHmmss");
            var safeProfile = profile.Id.Replace('/', '_').Replace('\\', '_');
            var filePath = Path.Combine(_calibrationArtifactRoot, $"attach_{safeProfile}_{safeTimestamp}.json");

            var moduleHash = ComputeFileSha256(process.ProcessPath);
            var payload = new
            {
                schemaVersion = "1.0",
                generatedAtUtc = now,
                trigger = "attach",
                profile = new
                {
                    id = profile.Id,
                    exeTarget = profile.ExeTarget.ToString()
                },
                process = new
                {
                    process.ProcessId,
                    process.ProcessName,
                    process.ProcessPath,
                    process.CommandLine,
                    launchContext = process.LaunchContext
                },
                moduleFingerprint = new
                {
                    path = process.ProcessPath,
                    sha256 = moduleHash,
                    sizeBytes = TryGetFileSize(process.ProcessPath),
                    lastWriteUtc = TryGetLastWriteUtc(process.ProcessPath)
                },
                build = new
                {
                    build.ProfileId,
                    build.GameBuild,
                    build.ExecutablePath,
                    build.ExeTarget,
                    build.ProcessId
                },
                symbolPolicy = new
                {
                    criticalSymbols = _criticalSymbols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                    validationRules = _symbolValidationRules.Values
                        .SelectMany(x => x)
                        .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                },
                symbols = symbols.Symbols
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
                    .ToArray()
            };

            File.WriteAllText(filePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit calibration snapshot.");
            return null;
        }
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

    public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken = default) where T : unmanaged
    {
        EnsureAttached();
        var sym = ResolveSymbol(symbol);
        var value = _memory!.Read<T>(sym.Address);
        return Task.FromResult(value);
    }

    public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken = default) where T : unmanaged
    {
        EnsureAttached();
        var sym = ResolveSymbol(symbol);
        _memory!.Write(sym.Address, value);
        return Task.CompletedTask;
    }

    public async Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAttached();
        if (_dependencySoftDisabledActions.Contains(request.Action.Id))
        {
            return new ActionExecutionResult(
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
        }

        try
        {
            var result = request.Action.ExecutionKind switch
            {
                ExecutionKind.Memory => await ExecuteMemoryActionAsync(request, cancellationToken),
                ExecutionKind.Helper => await ExecuteHelperActionAsync(request, cancellationToken),
                ExecutionKind.Save => await ExecuteSaveActionAsync(request, cancellationToken),
                ExecutionKind.CodePatch => await ExecuteCodePatchActionAsync(request, cancellationToken),
                ExecutionKind.Freeze => new ActionExecutionResult(false, "Freeze actions must be handled by the orchestrator, not the runtime adapter.", AddressSource.None),
                _ => new ActionExecutionResult(false, "Unsupported execution kind", AddressSource.None)
            };
            RecordActionTelemetry(request, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action execution failed for {Action}", request.Action.Id);
            var failed = new ActionExecutionResult(
                false,
                ex.Message,
                AddressSource.None,
                new Dictionary<string, object?>
                {
                    ["failureReasonCode"] = "action_exception",
                    ["exceptionType"] = ex.GetType().Name
                });
            RecordActionTelemetry(request, failed);
            return failed;
        }
    }

    public Task DetachAsync(CancellationToken cancellationToken = default)
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

    private async Task<ActionExecutionResult> ExecuteMemoryActionAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        var symbol = payload["symbol"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new InvalidOperationException("Memory action payload requires 'symbol'.");
        }

        // Route credits writes to the specialised handler BEFORE generic symbol resolution.
        // SetCreditsAsync performs its own resolution with late-fallback logic, so it must
        // not be blocked by a missing entry in the attach-time SymbolMap.
        if (payload["intValue"] is not null && IsCreditsWrite(request, symbol))
        {
            var value = payload["intValue"]!.GetValue<int>();
            var lockCredits =
                TryReadBooleanPayload(payload, "lockCredits", out var lockFromPayload) ? lockFromPayload :
                TryReadBooleanPayload(payload, "forcePatchHook", out var legacyForcePatchHook) && legacyForcePatchHook;
            return await SetCreditsAsync(
                value,
                lockCredits,
                verifyReadback: request.Action.VerifyReadback,
                cancellationToken);
        }

        var symbolInfo = ResolveSymbol(symbol);
        var validationRule = ResolveSymbolValidationRule(symbol, request.RuntimeMode);
        var isCriticalSymbol = IsCriticalSymbol(symbol, validationRule);

        if (payload["intValue"] is not null)
        {
            var value = payload["intValue"]!.GetValue<int>();
            var requestedValidation = ValidateRequestedIntValue(symbol, value, validationRule);
            if (!requestedValidation.IsValid)
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

            return await WriteWithOptionalRetryAsync(
                symbol,
                symbolInfo,
                value,
                request.Action.VerifyReadback,
                isCriticalSymbol,
                request.RuntimeMode,
                validationRule,
                readValue: address => _memory!.Read<int>(address),
                writeValue: (address, requestedValue) => _memory!.Write(address, requestedValue),
                compareValues: (expected, actual) => actual == expected,
                validateObservedValue: observed => ValidateObservedIntValue(symbol, observed, validationRule),
                formatValue: observed => observed.ToString(),
                cancellationToken: cancellationToken);
        }

        if (payload["floatValue"] is not null)
        {
            float value;
            try { value = payload["floatValue"]!.GetValue<float>(); }
            catch (InvalidOperationException) { value = (float)payload["floatValue"]!.GetValue<double>(); }
            var requestedValidation = ValidateRequestedFloatValue(symbol, value, validationRule);
            if (!requestedValidation.IsValid)
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

            return await WriteWithOptionalRetryAsync(
                symbol,
                symbolInfo,
                value,
                request.Action.VerifyReadback,
                isCriticalSymbol,
                request.RuntimeMode,
                validationRule,
                readValue: address => _memory!.Read<float>(address),
                writeValue: (address, requestedValue) => _memory!.Write(address, requestedValue),
                compareValues: (expected, actual) => Math.Abs(actual - expected) <= 0.0001f,
                validateObservedValue: observed => ValidateObservedFloatValue(symbol, observed, validationRule),
                formatValue: observed => observed.ToString("0.####"),
                cancellationToken: cancellationToken);
        }

        if (payload["boolValue"] is not null)
        {
            var value = payload["boolValue"]!.GetValue<bool>() ? (byte)1 : (byte)0;
            var requestedValidation = ValidateRequestedIntValue(symbol, value, validationRule);
            if (!requestedValidation.IsValid)
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

            return await WriteWithOptionalRetryAsync(
                symbol,
                symbolInfo,
                value,
                request.Action.VerifyReadback,
                isCriticalSymbol,
                request.RuntimeMode,
                validationRule,
                readValue: address => _memory!.Read<byte>(address),
                writeValue: (address, requestedValue) => _memory!.Write(address, requestedValue),
                compareValues: (expected, actual) => actual == expected,
                validateObservedValue: observed => ValidateObservedIntValue(symbol, observed, validationRule),
                formatValue: observed => (observed != 0).ToString(),
                cancellationToken: cancellationToken);
        }

        // Debug convenience: if no value is supplied, treat this as a read.
        // This is handy for calibration (e.g., verifying that a resolved symbol actually matches in-game values).
        var read = symbolInfo.ValueType switch
        {
            SymbolValueType.Int32 => (object)_memory!.Read<int>(symbolInfo.Address),
            SymbolValueType.Int64 => _memory!.Read<long>(symbolInfo.Address),
            SymbolValueType.Float => _memory!.Read<float>(symbolInfo.Address),
            SymbolValueType.Double => _memory!.Read<double>(symbolInfo.Address),
            SymbolValueType.Byte => _memory!.Read<byte>(symbolInfo.Address),
            SymbolValueType.Bool => _memory!.Read<byte>(symbolInfo.Address) != 0,
            SymbolValueType.Pointer => $"0x{_memory!.Read<long>(symbolInfo.Address):X}",
            _ => null
        };

        if (read is null)
        {
            throw new InvalidOperationException(
                $"Memory action payload must include one of: intValue, floatValue, boolValue. Read is unsupported for symbol value type {symbolInfo.ValueType}.");
        }

        var readDiagnostics = CreateSymbolDiagnostics(symbolInfo, validationRule, isCriticalSymbol);
        readDiagnostics["value"] = read;
        var observedValidation = ValidateObservedReadValue(symbol, read, symbolInfo.ValueType, validationRule);
        readDiagnostics["validationStatus"] = observedValidation.IsValid ? "pass" : "degraded";
        readDiagnostics["validationReasonCode"] = observedValidation.ReasonCode;

        if (!observedValidation.IsValid)
        {
            return new ActionExecutionResult(
                false,
                observedValidation.Message,
                symbolInfo.Source,
                readDiagnostics);
        }

        return new ActionExecutionResult(
            true,
            $"Read {symbolInfo.ValueType} value {read} from symbol {symbol}",
            symbolInfo.Source,
            readDiagnostics);
    }

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

        (bool Success, string ReasonCode, string Message, bool HasObservedValue, T ObservedValue) AttemptWrite(SymbolInfo activeSymbol, string attemptPrefix)
        {
            try
            {
                writeValue(activeSymbol.Address, requestedValue);
            }
            catch (Exception ex)
            {
                return (
                    false,
                    $"{attemptPrefix}_write_exception",
                    $"Write failed for symbol '{symbol}' at {ToHex(activeSymbol.Address)}: {ex.Message}",
                    false,
                    default);
            }

            if (!verifyReadback)
            {
                return (true, "ok", string.Empty, false, default);
            }

            T observed;
            try
            {
                observed = readValue(activeSymbol.Address);
            }
            catch (Exception ex)
            {
                return (
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
                return (true, "ok", string.Empty, true, observed);
            }

            if (!matches)
            {
                return (
                    false,
                    $"{attemptPrefix}_readback_mismatch",
                    $"Readback mismatch for {symbol}: expected {formatValue(requestedValue)}, got {formatValue(observed)}",
                    true,
                    observed);
            }

            return (false, observedValidation.ReasonCode, observedValidation.Message, true, observed);
        }

        var initial = AttemptWrite(symbolInfo, "initial");
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

        var retryAttempt = AttemptWrite(refreshedSymbol, "retry");
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

    private async Task<(bool Succeeded, SymbolInfo? Symbol, string ReasonCode, string Message)> TryReResolveSymbolAsync(
        string symbol,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken)
    {
        if (CurrentSession is null || _attachedProfile is null)
        {
            return (false, null, "reresolve_unavailable", "Re-resolve is unavailable without an active attach session.");
        }

        try
        {
            var refreshedMap = await _signatureResolver.ResolveAsync(
                CurrentSession.Build,
                _attachedProfile.SignatureSets,
                _attachedProfile.FallbackOffsets,
                cancellationToken);

            if (!refreshedMap.TryGetValue(symbol, out var refreshedSymbol) ||
                refreshedSymbol is null ||
                refreshedSymbol.Address == nint.Zero)
            {
                return (
                    false,
                    null,
                    "reresolve_symbol_unresolved",
                    $"Re-resolve could not find a usable address for symbol '{symbol}'.");
            }

            var evaluation = _symbolHealthService.Evaluate(refreshedSymbol, _attachedProfile, runtimeMode);
            var normalized = refreshedSymbol with
            {
                Confidence = ClampConfidence(Math.Max(refreshedSymbol.Confidence, evaluation.Confidence)),
                HealthStatus = evaluation.Status,
                HealthReason = string.IsNullOrWhiteSpace(refreshedSymbol.HealthReason)
                    ? evaluation.Reason
                    : $"{refreshedSymbol.HealthReason}+{evaluation.Reason}",
                LastValidatedAt = DateTimeOffset.UtcNow
            };

            UpdateSessionSymbol(normalized);
            _logger.LogInformation(
                "Re-resolved symbol {Symbol} to {Address} (source={Source}, health={Health}, confidence={Confidence:0.00})",
                symbol,
                ToHex(normalized.Address),
                normalized.Source,
                normalized.HealthStatus,
                normalized.Confidence);

            return (true, normalized, "reresolve_success", "Re-resolve succeeded.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-resolve symbol {Symbol}.", symbol);
            return (
                false,
                null,
                "reresolve_exception",
                $"Re-resolve failed for symbol '{symbol}': {ex.Message}");
        }
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
        if (request.Action.Id.Equals("set_unit_cap", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteUnitCapHookAsync(request);
        }

        if (request.Action.Id.Equals("toggle_instant_build_patch", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteInstantBuildHookAsync(request);
        }

        var payload = request.Payload;
        var symbol = payload["symbol"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Task.FromResult(new ActionExecutionResult(false, "CodePatch action requires 'symbol' in payload.", AddressSource.None));
        }

        var enable = payload["enable"]?.GetValue<bool>() ?? true;
        var patchBytesHex = payload["patchBytes"]?.GetValue<string>();
        var originalBytesHex = payload["originalBytes"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(patchBytesHex) || string.IsNullOrWhiteSpace(originalBytesHex))
        {
            return Task.FromResult(new ActionExecutionResult(false, "CodePatch action requires 'patchBytes' and 'originalBytes' in payload.", AddressSource.None));
        }

        var patchBytes = ParseHexBytes(patchBytesHex);
        var originalBytes = ParseHexBytes(originalBytesHex);

        if (patchBytes.Length != originalBytes.Length)
        {
            return Task.FromResult(new ActionExecutionResult(false, $"CodePatch patchBytes length ({patchBytes.Length}) must match originalBytes length ({originalBytes.Length}).", AddressSource.None));
        }

        var symbolInfo = ResolveSymbol(symbol);
        var address = symbolInfo.Address;

        if (enable)
        {
            // Verify current bytes match original before patching
            var currentBytes = _memory!.ReadBytes(address, originalBytes.Length);
            var isAlreadyPatched = currentBytes.AsSpan().SequenceEqual(patchBytes);
            var isOriginal = currentBytes.AsSpan().SequenceEqual(originalBytes);

            if (isAlreadyPatched)
            {
                return Task.FromResult(new ActionExecutionResult(true, $"Code patch '{symbol}' is already active.", symbolInfo.Source,
                    new Dictionary<string, object?> { ["address"] = $"0x{address.ToInt64():X}", ["state"] = "already_patched" }));
            }

            if (!isOriginal)
            {
                return Task.FromResult(new ActionExecutionResult(false,
                    $"Code patch '{symbol}' refused: unexpected bytes at {$"0x{address.ToInt64():X}"}. Expected {BitConverter.ToString(originalBytes)} got {BitConverter.ToString(currentBytes)}.",
                    symbolInfo.Source));
            }

            // Save original bytes and apply patch
            _activeCodePatches[symbol] = (address, currentBytes.ToArray());
            _memory.WriteBytes(address, patchBytes, executablePatch: true);
            _logger.LogInformation("Code patch '{Symbol}' applied at {Address}", symbol, $"0x{address.ToInt64():X}");

            return Task.FromResult(new ActionExecutionResult(true, $"Code patch '{symbol}' enabled at {$"0x{address.ToInt64():X}"}.", symbolInfo.Source,
                new Dictionary<string, object?>
                {
                    ["address"] = $"0x{address.ToInt64():X}",
                    ["state"] = "patched",
                    ["bytesWritten"] = BitConverter.ToString(patchBytes)
                }));
        }
        else
        {
            // Restore original bytes
            if (_activeCodePatches.TryGetValue(symbol, out var saved))
            {
                _memory!.WriteBytes(saved.Address, saved.OriginalBytes, executablePatch: true);
                _activeCodePatches.Remove(symbol);
                _logger.LogInformation("Code patch '{Symbol}' restored at {Address}", symbol, $"0x{saved.Address.ToInt64():X}");

                return Task.FromResult(new ActionExecutionResult(true, $"Code patch '{symbol}' disabled, original bytes restored.", symbolInfo.Source,
                    new Dictionary<string, object?>
                    {
                        ["address"] = $"0x{saved.Address.ToInt64():X}",
                        ["state"] = "restored",
                        ["bytesWritten"] = BitConverter.ToString(saved.OriginalBytes)
                    }));
            }

            // Patch not active — write original bytes anyway as a safety measure
            _memory!.WriteBytes(address, originalBytes, executablePatch: true);
            return Task.FromResult(new ActionExecutionResult(true, $"Code patch '{symbol}' was not active, wrote original bytes as safety restore.", symbolInfo.Source,
                new Dictionary<string, object?> { ["address"] = $"0x{address.ToInt64():X}", ["state"] = "force_restored" }));
        }
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
        var enable = payload["enable"]?.GetValue<bool>() ?? true;
        var capValue = payload["intValue"]?.GetValue<int>() ?? 99999;

        if (!enable)
        {
            var disabled = DisableUnitCapHook();
            return Task.FromResult(disabled);
        }

        if (_instantBuildHookOriginalBytesBackup is not null && _instantBuildHookInjectionAddress != nint.Zero)
        {
            var disabledInstantBuild = DisableInstantBuildHook();
            if (!disabledInstantBuild.Succeeded)
            {
                return Task.FromResult(new ActionExecutionResult(
                    false,
                    $"Cannot enable unit cap while instant-build hook is active: {disabledInstantBuild.Message}",
                    AddressSource.None));
            }
        }

        if (_activeCodePatches.TryGetValue("unit_cap", out var activePatch))
        {
            try
            {
                _memory!.WriteBytes(activePatch.Address, activePatch.OriginalBytes, executablePatch: true);
                _activeCodePatches.Remove("unit_cap");
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ActionExecutionResult(
                    false,
                    $"Failed to disable instant-build patch before unit cap hook install: {ex.Message}",
                    AddressSource.None));
            }
        }

        var result = EnsureUnitCapHookInstalled(capValue);
        return Task.FromResult(result);
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

        // Resolve integer symbol for readback validation and immediate UI consistency write.
        SymbolInfo creditsSymbol;
        try
        {
            creditsSymbol = ResolveSymbol("credits");
            diagnostics["creditsAddress"] = ToHex(creditsSymbol.Address);
        }
        catch (KeyNotFoundException)
        {
            return new ActionExecutionResult(false,
                "Credits symbol 'credits' was not resolved. Attach to the game and ensure the profile has the credits signature or fallback offset.",
                AddressSource.None, diagnostics);
        }

        // Credits updates must flow through the float source conversion path.
        // Do not degrade to direct-int fallback, because that is frequently overwritten.
        var patchResult = EnsureCreditsRuntimeHookInstalled();
        var hookAvailable = patchResult.Succeeded;
        diagnostics["hookInstalled"] = hookAvailable;
        if (!hookAvailable)
        {
            diagnostics["hookError"] = patchResult.Message;
            diagnostics["creditsStateTag"] = "HOOK_REQUIRED";
            diagnostics["creditsRequestedValue"] = value;
            return new ActionExecutionResult(
                false,
                $"Credits write aborted: hook install failed ({patchResult.Message}).",
                creditsSymbol.Source,
                diagnostics);
        }

        if (patchResult.Diagnostics is not null)
        {
            foreach (var kv in patchResult.Diagnostics)
            {
                diagnostics[kv.Key] = kv.Value;
            }
        }

        bool hookTickObserved = false;
        nint contextBase = nint.Zero;
        nint creditsFloatAddress = nint.Zero;

        var forcedFloatBits = BitConverter.SingleToInt32Bits((float)value);
        _memory.Write(_creditsHookForcedFloatBitsAddress, forcedFloatBits);

        var baselineHitCount = _memory.Read<int>(_creditsHookHitCountAddress);
        _memory.Write(_creditsHookLockEnabledAddress, 1);

        var hookPulse = await WaitForCreditsHookTickAsync(
            baselineHitCount,
            CreditsHookPulseTimeoutMs,
            cancellationToken);

        hookTickObserved = hookPulse.Observed;

        diagnostics["forcedFloatBits"] = $"0x{forcedFloatBits:X8}";
        diagnostics["forcedFloatValue"] = (float)value;
        diagnostics["hookHitCountStart"] = baselineHitCount;
        diagnostics["hookHitCountEnd"] = hookPulse.HitCount;
        diagnostics["hookTickObserved"] = hookPulse.Observed;

        var contextBaseRaw = _memory.Read<long>(_creditsHookLastContextAddress);
        contextBase = (nint)contextBaseRaw;
        diagnostics["creditsContextBase"] = contextBase == nint.Zero ? null : ToHex(contextBase);

        if (contextBase != nint.Zero)
        {
            creditsFloatAddress = contextBase + _creditsHookContextOffset;
            diagnostics["creditsFloatAddress"] = ToHex(creditsFloatAddress);
        }

        if (!hookTickObserved)
        {
            if (!lockCredits)
            {
                _memory.Write(_creditsHookLockEnabledAddress, 0);
            }

            diagnostics["creditsStateTag"] = "HOOK_REQUIRED";
            diagnostics["creditsRequestedValue"] = value;
            return new ActionExecutionResult(
                false,
                $"Credits write aborted: hook did not observe a sync tick within {CreditsHookPulseTimeoutMs}ms. Enter galactic/campaign view and retry.",
                creditsSymbol.Source,
                diagnostics);
        }

        if (!lockCredits)
        {
            _memory.Write(_creditsHookLockEnabledAddress, 0);
        }

        // Keep int mirror consistent with the now-authoritative forced float state.
        _memory.Write(creditsSymbol.Address, value);
        diagnostics["lockCredits"] = lockCredits;

        if (verifyReadback)
        {
            await Task.Delay(lockCredits ? 180 : 120, cancellationToken);

            var settledInt = _memory.Read<int>(creditsSymbol.Address);
            diagnostics["intReadbackSettled"] = settledInt;

            if (settledInt != value)
            {
                return new ActionExecutionResult(
                    false,
                    $"Credits hook sync failed int readback (expected={value}, got={settledInt}).",
                    creditsSymbol.Source,
                    diagnostics);
            }

            if (creditsFloatAddress != nint.Zero)
            {
                var settledFloat = _memory.Read<float>(creditsFloatAddress);
                diagnostics["floatReadbackSettled"] = settledFloat;
                if (lockCredits && Math.Abs(settledFloat - value) > CreditsFloatTolerance)
                {
                    return new ActionExecutionResult(
                        false,
                        $"Credits lock did not persist real float value (expected~={value}, got={settledFloat}).",
                        creditsSymbol.Source,
                        diagnostics);
                }
            }
        }

        // Build user-facing result message based on what actually happened.
        string message;
        string stateTag;
        stateTag = lockCredits ? "HOOK_LOCK" : "HOOK_ONESHOT";
        message = lockCredits
            ? "[HOOK_LOCK] Set credits and enabled persistent lock (float+int sync)."
            : "[HOOK_ONESHOT] Set credits with one-shot float+int sync.";
        diagnostics["creditsStateTag"] = stateTag;
        diagnostics["creditsRequestedValue"] = value;

        return new ActionExecutionResult(
            true,
            message,
            creditsSymbol.Source,
            diagnostics);
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

        var alreadyInstalled =
            _creditsHookOriginalBytesBackup is not null &&
            _creditsHookInjectionAddress != nint.Zero &&
            _creditsHookCodeCaveAddress != nint.Zero &&
            _creditsHookHitCountAddress != nint.Zero &&
            _creditsHookLockEnabledAddress != nint.Zero &&
            _creditsHookForcedFloatBitsAddress != nint.Zero;

        if (alreadyInstalled)
        {
            return CreditsHookPatchResult.Ok(
                "Credits hook already installed.",
                new Dictionary<string, object?>
                {
                    ["hookAddress"] = ToHex(_creditsHookInjectionAddress),
                    ["hookCaveAddress"] = ToHex(_creditsHookCodeCaveAddress),
                    ["hookState"] = "already_installed"
                });
        }

        var resolution = ResolveCreditsHookInjectionAddress();
        if (!resolution.Succeeded)
        {
            return CreditsHookPatchResult.Fail(resolution.Message);
        }

        var injectionAddress = resolution.Address;
        // Preserve exact original instruction bytes (register + offset variant).
        var expectedOriginalBytes = resolution.OriginalInstruction;
        if (expectedOriginalBytes is null || expectedOriginalBytes.Length != CreditsHookJumpLength)
        {
            return CreditsHookPatchResult.Fail(
                $"Credits hook patch failed: invalid original instruction metadata at {ToHex(injectionAddress)}.");
        }

        byte[] currentBytes;
        try
        {
            currentBytes = _memory.ReadBytes(injectionAddress, expectedOriginalBytes.Length);
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

        try
        {
            var caveBytes = BuildCreditsHookCaveBytes(
                caveAddress,
                injectionAddress,
                expectedOriginalBytes,
                resolution.DetectedOffset,
                resolution.DestinationReg);
            var jumpPatch = BuildRelativeJumpBytes(injectionAddress, caveAddress);

            _memory.WriteBytes(caveAddress, caveBytes, executablePatch: true);
            _memory.WriteBytes(injectionAddress, jumpPatch, executablePatch: true);

            _creditsHookOriginalBytesBackup = currentBytes.ToArray();
            _creditsHookInjectionAddress = injectionAddress;
            _creditsHookCodeCaveAddress = caveAddress;
            _creditsHookLastContextAddress = caveAddress + CreditsHookDataLastContextOffset;
            _creditsHookHitCountAddress = caveAddress + CreditsHookDataHitCountOffset;
            _creditsHookLockEnabledAddress = caveAddress + CreditsHookDataLockEnabledOffset;
            _creditsHookForcedFloatBitsAddress = caveAddress + CreditsHookDataForcedFloatBitsOffset;
            _creditsHookContextOffset = resolution.DetectedOffset;

            _memory.Write(_creditsHookHitCountAddress, 0);
            _memory.Write(_creditsHookLockEnabledAddress, 0);
            _memory.Write(_creditsHookForcedFloatBitsAddress, 0);
            _memory.Write(_creditsHookLastContextAddress, 0L);

            var diagnostics = new Dictionary<string, object?>
            {
                ["hookAddress"] = ToHex(injectionAddress),
                ["hookCaveAddress"] = ToHex(caveAddress),
                ["hookPatchBytes"] = BitConverter.ToString(jumpPatch),
                ["hookMode"] = "trampoline_real_float",
                ["hookContextOffset"] = $"0x{resolution.DetectedOffset:X2}",
                ["hookDestinationReg"] = $"0x{resolution.DestinationReg:X2}"
            };

            return CreditsHookPatchResult.Ok(
                $"Credits hook installed at {ToHex(injectionAddress)} with cave {ToHex(caveAddress)}.",
                diagnostics);
        }
        catch (Exception ex)
        {
            try
            {
                _memory.WriteBytes(injectionAddress, currentBytes, executablePatch: true);
            }
            catch
            {
                // Best-effort rollback.
            }

            _memory.Free(caveAddress);
            ClearCreditsHookState();
            return CreditsHookPatchResult.Fail($"Credits hook patch write failed: {ex.Message}");
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

        for (long delta = 0; delta <= nearRange; delta += 0x10000)
        {
            if (TryAllocateNear(baseHint + delta, injectionAddress, caveSize, out var cave))
            {
                return cave;
            }

            if (delta != 0 && TryAllocateNear(baseHint - delta, injectionAddress, caveSize, out cave))
            {
                return cave;
            }
        }

        for (long delta = nearRange + 0x100000; delta <= maxRange; delta += 0x100000)
        {
            if (TryAllocateNear(baseHint + delta, injectionAddress, caveSize, out var cave))
            {
                return cave;
            }

            if (TryAllocateNear(baseHint - delta, injectionAddress, caveSize, out cave))
            {
                return cave;
            }
        }

        // Last resort: regular allocation (may be out of rel32 range, so validate).
        var fallback = _memory.Allocate((nuint)caveSize, executable: true, preferredAddress: nint.Zero);
        if (fallback == nint.Zero)
        {
            return nint.Zero;
        }

        var jmpBackSource = fallback + (CreditsHookCodeSize - CreditsHookJumpLength);
        var jmpBackTarget = injectionAddress + CreditsHookJumpLength;
        if (!IsRel32Reachable(injectionAddress, CreditsHookJumpLength, fallback) ||
            !IsRel32Reachable(jmpBackSource, CreditsHookJumpLength, jmpBackTarget))
        {
            _memory.Free(fallback);
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

        if (_unitCapHookOriginalBytesBackup is not null &&
            _unitCapHookInjectionAddress != nint.Zero &&
            _unitCapHookCodeCaveAddress != nint.Zero &&
            _unitCapHookValueAddress != nint.Zero)
        {
            _memory.Write(_unitCapHookValueAddress, capValue);
            return new ActionExecutionResult(true, $"Unit cap updated to {capValue}.", AddressSource.Signature,
                new Dictionary<string, object?>
                {
                    ["hookAddress"] = ToHex(_unitCapHookInjectionAddress),
                    ["hookCaveAddress"] = ToHex(_unitCapHookCodeCaveAddress),
                    ["unitCapValue"] = capValue,
                    ["state"] = "updated"
                });
        }

        var resolution = ResolveUnitCapHookInjectionAddress();
        if (!resolution.Succeeded)
        {
            return new ActionExecutionResult(false, resolution.Message, AddressSource.None);
        }

        var injectionAddress = resolution.Address;
        byte[] currentBytes;
        try
        {
            currentBytes = _memory.ReadBytes(injectionAddress, UnitCapHookOriginalBytes.Length);
        }
        catch (Exception ex)
        {
            return new ActionExecutionResult(false, $"Unit cap hook failed: unable to read injection bytes ({ex.Message}).", AddressSource.None);
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

        try
        {
            var caveBytes = BuildUnitCapHookCaveBytes(caveAddress, injectionAddress, capValue);
            var jumpPatch = BuildRelativeJumpBytes(injectionAddress, caveAddress);

            _memory.WriteBytes(caveAddress, caveBytes, executablePatch: true);
            _memory.WriteBytes(injectionAddress, jumpPatch, executablePatch: true);

            _unitCapHookOriginalBytesBackup = currentBytes.ToArray();
            _unitCapHookInjectionAddress = injectionAddress;
            _unitCapHookCodeCaveAddress = caveAddress;
            _unitCapHookValueAddress = caveAddress + 1;

            return new ActionExecutionResult(true, $"Unit cap hook installed ({capValue}).", AddressSource.Signature,
                new Dictionary<string, object?>
                {
                    ["hookAddress"] = ToHex(injectionAddress),
                    ["hookCaveAddress"] = ToHex(caveAddress),
                    ["unitCapValue"] = capValue,
                    ["state"] = "installed"
                });
        }
        catch (Exception ex)
        {
            try
            {
                _memory.WriteBytes(injectionAddress, currentBytes, executablePatch: true);
            }
            catch
            {
                // Best-effort rollback.
            }

            _memory.Free(caveAddress);
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
                new Dictionary<string, object?> { ["state"] = "not_active" });
        }

        _memory.WriteBytes(_unitCapHookInjectionAddress, _unitCapHookOriginalBytesBackup, executablePatch: true);
        if (_unitCapHookCodeCaveAddress != nint.Zero)
        {
            _memory.Free(_unitCapHookCodeCaveAddress);
        }

        var address = _unitCapHookInjectionAddress;
        ClearUnitCapHookState();
        return new ActionExecutionResult(true, "Unit cap hook disabled and original bytes restored.", AddressSource.Signature,
            new Dictionary<string, object?> { ["hookAddress"] = ToHex(address), ["state"] = "restored" });
    }

    private ActionExecutionResult EnsureInstantBuildHookInstalled()
    {
        EnsureAttached();
        if (_memory is null)
        {
            return new ActionExecutionResult(false, "Instant build hook failed: memory accessor unavailable.", AddressSource.None);
        }

        if (_instantBuildHookOriginalBytesBackup is not null &&
            _instantBuildHookInjectionAddress != nint.Zero &&
            _instantBuildHookCodeCaveAddress != nint.Zero)
        {
            return new ActionExecutionResult(true, "Instant build hook already installed.", AddressSource.Signature,
                new Dictionary<string, object?>
                {
                    ["hookAddress"] = ToHex(_instantBuildHookInjectionAddress),
                    ["hookCaveAddress"] = ToHex(_instantBuildHookCodeCaveAddress),
                    ["state"] = "already_installed"
                });
        }

        var resolution = ResolveInstantBuildHookInjectionAddress();
        if (!resolution.Succeeded)
        {
            return new ActionExecutionResult(false, resolution.Message, AddressSource.None);
        }

        var injectionAddress = resolution.Address;
        byte[] currentBytes;
        try
        {
            currentBytes = _memory.ReadBytes(injectionAddress, InstantBuildHookInstructionLength);
        }
        catch (Exception ex)
        {
            return new ActionExecutionResult(false, $"Instant build hook failed: unable to read injection bytes ({ex.Message}).", AddressSource.None);
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

        try
        {
            var caveBytes = BuildInstantBuildHookCaveBytes(caveAddress, injectionAddress, resolution.OriginalBytes);
            var jumpPatch = BuildInstantBuildJumpPatchBytes(injectionAddress, caveAddress);

            _memory.WriteBytes(caveAddress, caveBytes, executablePatch: true);
            _memory.WriteBytes(injectionAddress, jumpPatch, executablePatch: true);

            _instantBuildHookOriginalBytesBackup = currentBytes.ToArray();
            _instantBuildHookInjectionAddress = injectionAddress;
            _instantBuildHookCodeCaveAddress = caveAddress;

            return new ActionExecutionResult(true, "Instant build hook installed (1 sec / 1 credit).", AddressSource.Signature,
                new Dictionary<string, object?>
                {
                    ["hookAddress"] = ToHex(injectionAddress),
                    ["hookCaveAddress"] = ToHex(caveAddress),
                    ["state"] = "installed"
                });
        }
        catch (Exception ex)
        {
            try
            {
                _memory.WriteBytes(injectionAddress, currentBytes, executablePatch: true);
            }
            catch
            {
                // Best-effort rollback.
            }

            _memory.Free(caveAddress);
            ClearInstantBuildHookState();
            return new ActionExecutionResult(false, $"Instant build hook patch failed: {ex.Message}", AddressSource.None);
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
                new Dictionary<string, object?> { ["state"] = "not_active" });
        }

        _memory.WriteBytes(_instantBuildHookInjectionAddress, _instantBuildHookOriginalBytesBackup, executablePatch: true);
        if (_instantBuildHookCodeCaveAddress != nint.Zero)
        {
            _memory.Free(_instantBuildHookCodeCaveAddress);
        }

        var address = _instantBuildHookInjectionAddress;
        ClearInstantBuildHookState();
        return new ActionExecutionResult(true, "Instant build hook disabled and original bytes restored.", AddressSource.Signature,
            new Dictionary<string, object?> { ["hookAddress"] = ToHex(address), ["state"] = "restored" });
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
        if (originalInstruction.Length != CreditsHookJumpLength)
        {
            throw new InvalidOperationException("Credits hook expected a 5-byte cvttss2si instruction.");
        }

        if (destinationReg > 7)
        {
            throw new InvalidOperationException($"Credits hook destination register out of range: {destinationReg}");
        }

        var bytes = new byte[CreditsHookCaveSize];

        // mov [rip+disp32], rax
        bytes[0] = 0x48;
        bytes[1] = 0x89;
        bytes[2] = 0x05;
        WriteInt32(
            bytes,
            3,
            ComputeRelativeDisplacement(
                caveAddress + 7,
                caveAddress + CreditsHookDataLastContextOffset));

        // inc dword ptr [rip+disp32]
        bytes[7] = 0xFF;
        bytes[8] = 0x05;
        WriteInt32(
            bytes,
            9,
            ComputeRelativeDisplacement(
                caveAddress + 13,
                caveAddress + CreditsHookDataHitCountOffset));

        // cmp dword ptr [rip+disp32], 0
        bytes[13] = 0x83;
        bytes[14] = 0x3D;
        WriteInt32(
            bytes,
            15,
            ComputeRelativeDisplacement(
                caveAddress + 20,
                caveAddress + CreditsHookDataLockEnabledOffset));
        bytes[19] = 0x00;

        // je +9 (skip forced write block)
        bytes[20] = 0x74;
        bytes[21] = 0x09;

        // mov r32, [rip+disp32] — use the same destination register as the original conversion instruction.
        bytes[22] = 0x8B;
        bytes[23] = (byte)(0x05 | (destinationReg << 3));
        WriteInt32(
            bytes,
            24,
            ComputeRelativeDisplacement(
                caveAddress + 28,
                caveAddress + CreditsHookDataForcedFloatBitsOffset));

        // mov [rax+offset], r32 — force our float bits into the game object
        bytes[28] = 0x89;
        bytes[29] = (byte)(0x40 | (destinationReg << 3));
        bytes[30] = contextOffset;

        // original bytes: cvttss2si r32,[rax+offset] (preserve exact encoding)
        Array.Copy(originalInstruction, 0, bytes, 31, originalInstruction.Length);

        // jmp back to injection + 5
        bytes[36] = 0xE9;
        WriteInt32(
            bytes,
            37,
            ComputeRelativeDisplacement(
                caveAddress + CreditsHookCodeSize,
                injectionAddress + CreditsHookJumpLength));

        return bytes;
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
            using var process = Process.GetProcessById(CurrentSession.Process.ProcessId);
            var module = process.MainModule;
            if (module is null)
            {
                return CreditsHookResolution.Fail("Main module is unavailable for credits hook resolution.");
            }

            var baseAddress = module.BaseAddress;
            var moduleBytes = _memory.ReadBytes(baseAddress, module.ModuleMemorySize);

            // Resolve credits int RVA for correlation-based matching.
            long creditsRva = -1;
            try
            {
                var creditsSymbol = ResolveSymbol("credits");
                creditsRva = creditsSymbol.Address.ToInt64() - baseAddress.ToInt64();
            }
            catch { /* credits symbol unavailable — correlation disabled */ }

            List<(int Offset, CreditsCvttss2siInstruction Instruction)> ParseCandidates(IEnumerable<int> offsets)
            {
                return offsets
                    .Select(hit =>
                    {
                        var parsed = TryParseCreditsCvttss2siInstruction(moduleBytes, hit, out var instruction);
                        return (hit, parsed, instruction);
                    })
                    .Where(x => x.parsed)
                    .Select(x => (x.hit, x.instruction))
                    .ToList();
            }

            CreditsHookResolution? ResolveSingleCandidate(
                List<(int Offset, CreditsCvttss2siInstruction Instruction)> candidates,
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
                return CreditsHookResolution.Ok(
                    baseAddress + candidate.Offset,
                    candidate.Instruction.ContextOffset,
                    candidate.Instruction.DestinationReg,
                    candidate.Instruction.OriginalBytes);
            }

            // Strategy 1: Try the original exact 7-byte pattern (fastest, most specific), but
            // parse the conversion instruction generically so we don't require EDX specifically.
            var exactPattern = AobPattern.Parse(CreditsHookPatternText);
            var exactHits = FindPatternOffsets(moduleBytes, exactPattern, maxHits: 3);
            var exactCandidates = ParseCandidates(exactHits);
            var exactResolution = ResolveSingleCandidate(
                exactCandidates,
                "Credits hook: exact-pattern candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}");
            if (exactResolution is not null)
            {
                return exactResolution;
            }

            // Gather all compatible conversion instructions in one pass:
            //   F3 0F 2C /r disp8, where /r uses [rax+disp8] and any destination reg32.
            var broadConvertPattern = AobPattern.Parse("F3 0F 2C ?? ??");
            var broadConvertHits = FindPatternOffsets(moduleBytes, broadConvertPattern, maxHits: 8000);
            var allCandidates = ParseCandidates(broadConvertHits);

            // Strategy 1b: prefer candidates where the immediate next instruction stores the same
            // converted register to memory (classic credits write path shape).
            var immediateStoreCandidates = allCandidates
                .Where(c => LooksLikeImmediateStoreFromConvertedRegister(moduleBytes, c.Offset + CreditsHookJumpLength, c.Instruction.DestinationReg))
                .ToList();
            var immediateResolution = ResolveSingleCandidate(
                immediateStoreCandidates,
                "Credits hook: selected immediate-store candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}");
            if (immediateResolution is not null)
            {
                return immediateResolution;
            }

            if (immediateStoreCandidates.Count > 1)
            {
                // Prefer the classic 0x70 offset when unique among candidates.
                var preferredClassic = immediateStoreCandidates
                    .Where(c => c.Instruction.ContextOffset == CreditsContextOffsetByte)
                    .ToList();
                if (preferredClassic.Count == 1)
                {
                    _logger.LogInformation(
                        "Credits hook: selected classic-offset immediate-store candidate at RVA 0x{Rva:X}, reg={Reg}",
                        preferredClassic[0].Offset,
                        preferredClassic[0].Instruction.DestinationReg);
                    return CreditsHookResolution.Ok(
                        baseAddress + preferredClassic[0].Offset,
                        preferredClassic[0].Instruction.ContextOffset,
                        preferredClassic[0].Instruction.DestinationReg,
                        preferredClassic[0].Instruction.OriginalBytes);
                }
            }

            // Strategy 2: correlate candidates with a nearby RIP-relative store to the known credits int RVA.
            if (creditsRva > 0)
            {
                var correlatedCandidates = allCandidates
                    .Where(c => HasNearbyStoreToCreditsRva(
                        moduleBytes,
                        c.Offset + CreditsHookJumpLength,
                        CreditsStoreCorrelationWindowBytes,
                        creditsRva))
                    .ToList();

                var correlatedResolution = ResolveSingleCandidate(
                    correlatedCandidates,
                    "Credits hook: selected correlated candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}");
                if (correlatedResolution is not null)
                {
                    return correlatedResolution;
                }

                if (correlatedCandidates.Count > 1)
                {
                    var preferredClassicCorrelated = correlatedCandidates
                        .Where(c => c.Instruction.ContextOffset == CreditsContextOffsetByte)
                        .ToList();
                    if (preferredClassicCorrelated.Count == 1)
                    {
                        var candidate = preferredClassicCorrelated[0];
                        _logger.LogInformation(
                            "Credits hook: selected classic-offset correlated candidate at RVA 0x{Rva:X}, reg={Reg}",
                            candidate.Offset,
                            candidate.Instruction.DestinationReg);
                        return CreditsHookResolution.Ok(
                            baseAddress + candidate.Offset,
                            candidate.Instruction.ContextOffset,
                            candidate.Instruction.DestinationReg,
                            candidate.Instruction.OriginalBytes);
                    }
                }
            }

            // Strategy 3: if only one classic-offset candidate exists, prefer it.
            var classicOffsetCandidates = allCandidates
                .Where(c => c.Instruction.ContextOffset == CreditsContextOffsetByte)
                .ToList();
            var classicResolution = ResolveSingleCandidate(
                classicOffsetCandidates,
                "Credits hook: selected unique classic-offset candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}");
            if (classicResolution is not null)
            {
                return classicResolution;
            }

            // Strategy 4: final fallback — only if there is exactly one compatible candidate in module.
            var singleFallbackResolution = ResolveSingleCandidate(
                allCandidates,
                "Credits hook: selected unique fallback candidate at RVA 0x{Rva:X}, offset=0x{Off:X2}, reg={Reg}");
            if (singleFallbackResolution is not null)
            {
                return singleFallbackResolution;
            }

            _logger.LogWarning(
                "Credits hook: candidates total={Total}, immediateStore={Immediate}, classicOffset={Classic}, creditsRva={Rva}",
                allCandidates.Count,
                immediateStoreCandidates.Count,
                classicOffsetCandidates.Count,
                creditsRva > 0 ? $"0x{creditsRva:X}" : "unavailable");

            return CreditsHookResolution.Fail(
                $"Credits hook pattern not found. Tried exact ({CreditsHookPatternText}), " +
                $"register-agnostic immediate-store heuristics, and credits-RVA correlation scan. " +
                $"Candidates: total={allCandidates.Count}, immediateStore={immediateStoreCandidates.Count}, classicOffset={classicOffsetCandidates.Count}. " +
                (creditsRva > 0
                    ? $"Credits int RVA=0x{creditsRva:X} was used for correlation."
                    : "Credits RVA unavailable for correlation."));
        }
        catch (Exception ex)
        {
            return CreditsHookResolution.Fail($"Credits hook pattern resolution failed: {ex.Message}");
        }
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
            var matched = true;
            for (var j = 0; j < signature.Length; j++)
            {
                var expected = signature[j];
                if (expected.HasValue && memory[i + j] != expected.Value)
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
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
        return request.Action.Id.Equals("set_credits", StringComparison.OrdinalIgnoreCase) ||
               symbol.Equals("credits", StringComparison.OrdinalIgnoreCase);
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
