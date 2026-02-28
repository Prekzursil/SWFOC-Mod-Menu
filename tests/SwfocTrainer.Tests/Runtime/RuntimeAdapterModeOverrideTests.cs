using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterModeOverrideTests
{
    [Fact]
    public void ResolveEffectiveMode_ShouldUseManualOverride_WhenOverrideProvided()
    {
        var adapter = CreateAdapter();
        var request = new ActionExecutionRequest(
            BuildAction(),
            new JsonObject(),
            "test_profile",
            RuntimeMode.Galactic,
            new Dictionary<string, object?>
            {
                ["runtimeModeOverride"] = "Tactical"
            });

        var resolved = InvokeResolveEffectiveMode(adapter, request);

        resolved.Request.RuntimeMode.Should().Be(RuntimeMode.Tactical);
        resolved.Diagnostics["runtimeModeHint"].Should().Be("Galactic");
        resolved.Diagnostics["runtimeModeProbe"].Should().Be("Unknown");
        resolved.Diagnostics["runtimeModeEffective"].Should().Be("Tactical");
        resolved.Diagnostics["runtimeModeEffectiveSource"].Should().Be("manual_override");
    }

    [Fact]
    public void ResolveEffectiveMode_ShouldRemainAuto_WhenOverrideIsAuto()
    {
        var adapter = CreateAdapter();
        var request = new ActionExecutionRequest(
            BuildAction(),
            new JsonObject(),
            "test_profile",
            RuntimeMode.Galactic,
            new Dictionary<string, object?>
            {
                ["runtimeModeOverride"] = "Auto"
            });

        var resolved = InvokeResolveEffectiveMode(adapter, request);

        resolved.Request.RuntimeMode.Should().Be(RuntimeMode.Galactic);
        resolved.Diagnostics["runtimeModeEffectiveSource"].Should().Be("auto");
    }

    [Fact]
    public void ResolveEffectiveMode_ShouldPreferTelemetryMode_WhenTelemetryContextIsFresh()
    {
        var adapter = CreateAdapter();
        var request = new ActionExecutionRequest(
            BuildAction(),
            new JsonObject(),
            "test_profile",
            RuntimeMode.Galactic,
            new Dictionary<string, object?>
            {
                ["telemetryRuntimeMode"] = "Tactical"
            });

        var resolved = InvokeResolveEffectiveMode(adapter, request);

        resolved.Request.RuntimeMode.Should().Be(RuntimeMode.Tactical);
        resolved.Diagnostics["runtimeModeEffectiveSource"].Should().Be("telemetry");
        resolved.Diagnostics["runtimeModeTelemetry"].Should().Be("Tactical");
        resolved.Diagnostics["runtimeModeTelemetryReasonCode"].Should().Be("telemetry_context_override");
    }

    [Fact]
    public void ResolveEffectiveMode_ShouldKeepManualOverridePriority_OverTelemetryMode()
    {
        var adapter = CreateAdapter();
        var request = new ActionExecutionRequest(
            BuildAction(),
            new JsonObject(),
            "test_profile",
            RuntimeMode.Unknown,
            new Dictionary<string, object?>
            {
                ["runtimeModeOverride"] = "Galactic",
                ["telemetryRuntimeMode"] = "Tactical"
            });

        var resolved = InvokeResolveEffectiveMode(adapter, request);

        resolved.Request.RuntimeMode.Should().Be(RuntimeMode.Galactic);
        resolved.Diagnostics["runtimeModeEffectiveSource"].Should().Be("manual_override");
    }

    private static (ActionExecutionRequest Request, IReadOnlyDictionary<string, object?> Diagnostics) InvokeResolveEffectiveMode(RuntimeAdapter adapter, ActionExecutionRequest request)
    {
        var method = typeof(RuntimeAdapter).GetMethod("ResolveEffectiveMode", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var tuple = method!.Invoke(adapter, new object?[] { request });
        tuple.Should().NotBeNull();
        return ((ActionExecutionRequest Request, IReadOnlyDictionary<string, object?> Diagnostics))tuple!;
    }

    private static RuntimeAdapter CreateAdapter()
    {
        return new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance);
    }

    private static ActionSpec BuildAction() => new(
        "test_action",
        ActionCategory.Global,
        RuntimeMode.Unknown,
        ExecutionKind.Memory,
        new JsonObject(),
        VerifyReadback: false,
        CooldownMs: 0);

    private sealed class StubProcessLocator : IProcessLocator
    {
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProcessMetadata>>(Array.Empty<ProcessMetadata>());

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
            => Task.FromResult<ProcessMetadata?>(null);
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class StubSignatureResolver : ISignatureResolver
    {
        public Task<SymbolMap> ResolveAsync(ProfileBuild build, IReadOnlyList<SignatureSet> signatureSets, IReadOnlyDictionary<string, long> fallbackOffsets, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }
}
