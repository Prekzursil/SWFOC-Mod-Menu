using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class RuntimeAdapter
{
    private readonly IProcessLocator _processLocator;
    private readonly IProfileRepository _profileRepository;
    private readonly ISignatureResolver _signatureResolver;
    private readonly IModDependencyValidator _modDependencyValidator;
    private readonly ISymbolHealthService _symbolHealthService;
    private readonly IProfileVariantResolver? _profileVariantResolver;
    private readonly ISdkOperationRouter? _sdkOperationRouter;
    private readonly IBackendRouter _backendRouter;
    private readonly IExecutionBackend? _extenderBackend;
    private readonly ITelemetryLogTailService _telemetryLogTailService;
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
    private nint _fogPatchFallbackAddress;
    private byte _fogPatchFallbackOriginalByte;
    private byte _fogPatchFallbackPatchedByte;

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
}
