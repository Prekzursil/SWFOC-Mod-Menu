using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;

namespace SwfocTrainer.Tests.Runtime;
internal sealed class AdapterHarness
{
    public IProcessLocator ProcessLocator { get; set; } = new StubProcessLocator();
    public bool IncludeExecutionBackend { get; set; } = true;

    public IBackendRouter Router { get; set; } = new StubBackendRouter(
        new BackendRouteDecision(
            Allowed: true,
            Backend: ExecutionBackendKind.Helper,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "ok"));

    public IExecutionBackend ExecutionBackend { get; set; } = new StubExecutionBackend();
    public IHelperBridgeBackend HelperBridgeBackend { get; set; } = new StubHelperBridgeBackend();

    public IModDependencyValidator DependencyValidator { get; set; } = new StubDependencyValidator(
        new DependencyValidationResult(
            DependencyValidationStatus.Pass,
            string.Empty,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

    public IModMechanicDetectionService? MechanicDetectionService { get; set; }

    public RuntimeAdapter CreateAdapter(TrainerProfile profile, RuntimeMode mode)
    {
        var services = new Dictionary<Type, object>
        {
            [typeof(IBackendRouter)] = Router,
            [typeof(IHelperBridgeBackend)] = HelperBridgeBackend,
            [typeof(IModDependencyValidator)] = DependencyValidator,
            [typeof(ITelemetryLogTailService)] = new StubTelemetryLogTailService()
        };

        if (IncludeExecutionBackend)
        {
            services[typeof(IExecutionBackend)] = ExecutionBackend;
        }

        if (MechanicDetectionService is not null)
        {
            services[typeof(IModMechanicDetectionService)] = MechanicDetectionService;
        }

        var adapter = new RuntimeAdapter(
            ProcessLocator,
            new StubProfileRepository(profile),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance,
            new MapServiceProvider(services));

        var session = RuntimeAdapterExecuteCoverageTests.BuildSession(mode);
        typeof(RuntimeAdapter)
            .GetProperty("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(adapter, session);

        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_attachedProfile", profile);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", RuntimeAdapterExecuteCoverageTests.CreateUninitializedMemoryAccessor());
        return adapter;
    }
}
internal sealed class StubBackendRouter(BackendRouteDecision decision) : IBackendRouter
{
    public BackendRouteDecision Decision { get; set; } = decision;

    public BackendRouteDecision Resolve(
        ActionExecutionRequest request,
        TrainerProfile profile,
        ProcessMetadata process,
        CapabilityReport capabilityReport)
    {
        _ = request;
        _ = profile;
        _ = process;
        _ = capabilityReport;
        return Decision;
    }
}

internal sealed class StubExecutionBackend : IExecutionBackend
{
    public ExecutionBackendKind BackendKind { get; set; } = ExecutionBackendKind.Extender;

    public CapabilityReport ProbeReport { get; set; } = new(
        "profile",
        DateTimeOffset.UtcNow,
        new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
        RuntimeReasonCode.CAPABILITY_PROBE_PASS);

    public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext)
    {
        _ = profileId;
        _ = processContext;
        return Task.FromResult(ProbeReport);
    }

    public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext, CancellationToken cancellationToken)
    {
        _ = profileId;
        _ = processContext;
        _ = cancellationToken;
        return Task.FromResult(ProbeReport);
    }

    public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport)
    {
        _ = command;
        _ = capabilityReport;
        return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.None));
    }

    public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport, CancellationToken cancellationToken)
    {
        _ = command;
        _ = capabilityReport;
        _ = cancellationToken;
        return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.None));
    }

    public Task<BackendHealth> GetHealthAsync()
        => Task.FromResult(new BackendHealth("stub", BackendKind, true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok"));

    public Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return GetHealthAsync();
    }
}

internal sealed class StubHelperBridgeBackend : IHelperBridgeBackend
{
    public HelperBridgeProbeResult ProbeResult { get; set; } = new(
        Available: true,
        ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
        Message: "ok");

    public HelperBridgeExecutionResult ExecuteResult { get; set; } = new(
        Succeeded: true,
        ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
        Message: "applied");

    public Exception? ExecuteException { get; set; }

    public Task<HelperBridgeProbeResult> ProbeAsync(HelperBridgeProbeRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        return Task.FromResult(ProbeResult);
    }

    public Task<HelperBridgeExecutionResult> ExecuteAsync(HelperBridgeRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        if (ExecuteException is not null)
        {
            throw ExecuteException;
        }

        return Task.FromResult(ExecuteResult);
    }
}

internal sealed class StubDependencyValidator(DependencyValidationResult result) : IModDependencyValidator
{
    public DependencyValidationResult Result { get; set; } = result;

    public DependencyValidationResult Validate(TrainerProfile profile, ProcessMetadata process)
    {
        _ = profile;
        _ = process;
        return Result;
    }
}

internal sealed class StubMechanicDetectionService(
    bool supported,
    string actionId,
    RuntimeReasonCode reasonCode,
    string message) : IModMechanicDetectionService
{
    public Task<ModMechanicReport> DetectAsync(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        CancellationToken cancellationToken)
    {
        _ = session;
        _ = catalog;
        _ = cancellationToken;
        return Task.FromResult(new ModMechanicReport(
            ProfileId: profile.Id,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport:
            [
                new ModMechanicSupport(
                    ActionId: actionId,
                    Supported: supported,
                    ReasonCode: reasonCode,
                    Message: message,
                    Confidence: 0.9d)
            ],
            Diagnostics: new Dictionary<string, object?> { ["supportSource"] = "stub" }));
    }
}

internal sealed class StubTelemetryLogTailService : ITelemetryLogTailService
{
    public TelemetryModeResolution ResolveLatestMode(string? processPath, DateTimeOffset nowUtc, TimeSpan freshnessWindow)
    {
        _ = processPath;
        _ = nowUtc;
        _ = freshnessWindow;
        return TelemetryModeResolution.Unavailable("stub");
    }
}

internal sealed class StubProcessLocator : IProcessLocator
{
    private readonly ProcessMetadata? _bestMatch;

    public StubProcessLocator()
    {
    }

    public StubProcessLocator(ProcessMetadata bestMatch)
    {
        _bestMatch = bestMatch;
    }

    public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<ProcessMetadata>>(
            _bestMatch is null ? Array.Empty<ProcessMetadata>() : [_bestMatch]);
    }

    public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
    {
        _ = target;
        _ = cancellationToken;
        return Task.FromResult(_bestMatch);
    }
}

internal sealed class ThrowingMechanicDetectionService : IModMechanicDetectionService
{
    public Task<ModMechanicReport> DetectAsync(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        CancellationToken cancellationToken)
    {
        _ = profile;
        _ = session;
        _ = catalog;
        _ = cancellationToken;
        throw new InvalidOperationException("detector failed");
    }
}

internal sealed class StubProfileRepository(TrainerProfile profile) : IProfileRepository
{
    public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        throw new NotImplementedException();
    }

    public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        _ = profileId;
        _ = cancellationToken;
        return Task.FromResult(profile);
    }

    public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        _ = profileId;
        _ = cancellationToken;
        return Task.FromResult(profile);
    }

    public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
    {
        _ = profile;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}

internal sealed class StubSignatureResolver : ISignatureResolver
{
    public Task<SymbolMap> ResolveAsync(
        ProfileBuild build,
        IReadOnlyList<SignatureSet> signatureSets,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        CancellationToken cancellationToken)
    {
        _ = build;
        _ = signatureSets;
        _ = fallbackOffsets;
        _ = cancellationToken;
        return Task.FromResult(new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)));
    }
}

internal sealed class MapServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
{
    public object? GetService(Type serviceType)
        => services.TryGetValue(serviceType, out var service) ? service : null;
}


