using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class SdkOperationRouterTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnFeatureFlagDisabled_WhenGateOff()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", null);

        try
        {
            var router = CreateRouter();
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Unknown,
                ProfileId: "universal_auto");

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.FeatureFlagDisabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnRuntimeNotAttached_WhenGateOnAndSessionMissing()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var router = CreateRouter();
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Unknown,
                ProfileId: "universal_auto");

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.RuntimeNotAttached);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBlockModeMismatch_ForTacticalOnlyOperationInGalacticMode()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var router = CreateRouter();
            var request = new SdkOperationRequest(
                OperationId: "set_hp",
                Payload: new JsonObject(),
                IsMutation: true,
                RuntimeMode: RuntimeMode.Galactic,
                ProfileId: "universal_auto",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/corruption/StarWarsG.exe",
                    ["processId"] = 420
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.ModeMismatch);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    private static SdkOperationRouter CreateRouter()
    {
        return new SdkOperationRouter(
            new FakeSdkRuntimeAdapter(),
            new FakeProfileVariantResolver(),
            new FakeBinaryFingerprintService(),
            new FakeCapabilityMapResolver(),
            new FakeSdkExecutionGuard(),
            new NullSdkDiagnosticsSink());
    }

    private sealed class FakeSdkRuntimeAdapter : ISdkRuntimeAdapter
    {
        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request)
        {
            return ExecuteAsync(request, CancellationToken.None);
        }

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new SdkOperationResult(true, "ok", CapabilityReasonCode.AllRequiredAnchorsPresent, SdkCapabilityStatus.Available));
        }
    }

    private sealed class FakeProfileVariantResolver : IProfileVariantResolver
    {
        public Task<ProfileVariantResolution> ResolveAsync(string requestedProfileId, CancellationToken cancellationToken)
        {
            return ResolveAsync(requestedProfileId, null, cancellationToken);
        }

        public Task<ProfileVariantResolution> ResolveAsync(string requestedProfileId, IReadOnlyList<ProcessMetadata>? processes, CancellationToken cancellationToken)
        {
            _ = processes;
            _ = cancellationToken;
            return Task.FromResult(new ProfileVariantResolution(requestedProfileId, "base_swfoc", "test", 1.0d));
        }
    }

    private sealed class FakeBinaryFingerprintService : IBinaryFingerprintService
    {
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath)
        {
            return CaptureFromPathAsync(modulePath, CancellationToken.None);
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, CancellationToken cancellationToken)
        {
            return CaptureFromPathAsync(modulePath, 0, cancellationToken);
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId)
        {
            return CaptureFromPathAsync(modulePath, processId, CancellationToken.None);
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId, CancellationToken cancellationToken)
        {
            _ = processId;
            _ = cancellationToken;
            return Task.FromResult(new BinaryFingerprint(
                "fp",
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                "swfoc.exe",
                "1",
                "1",
                DateTimeOffset.UtcNow,
                Array.Empty<string>(),
                modulePath));
        }
    }

    private sealed class FakeCapabilityMapResolver : ICapabilityMapResolver
    {
        public Task<CapabilityResolutionResult> ResolveAsync(BinaryFingerprint fingerprint, string requestedProfileId, string operationId, IReadOnlySet<string> resolvedAnchors)
        {
            return ResolveAsync(fingerprint, requestedProfileId, operationId, resolvedAnchors, CancellationToken.None);
        }

        public Task<CapabilityResolutionResult> ResolveAsync(BinaryFingerprint fingerprint, string requestedProfileId, string operationId, IReadOnlySet<string> resolvedAnchors, CancellationToken cancellationToken)
        {
            _ = resolvedAnchors;
            _ = cancellationToken;
            return Task.FromResult(new CapabilityResolutionResult(
                requestedProfileId,
                operationId,
                SdkCapabilityStatus.Available,
                CapabilityReasonCode.AllRequiredAnchorsPresent,
                1.0d,
                fingerprint.FingerprintId,
                Array.Empty<string>(),
                Array.Empty<string>(),
                CapabilityResolutionMetadata.Empty));
        }

        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint)
        {
            return ResolveDefaultProfileIdAsync(fingerprint, CancellationToken.None);
        }

        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult<string?>("base_swfoc");
        }
    }

    private sealed class FakeSdkExecutionGuard : ISdkExecutionGuard
    {
        public SdkExecutionDecision CanExecute(CapabilityResolutionResult resolution, bool isMutation)
        {
            _ = isMutation;
            return new SdkExecutionDecision(true, resolution.ReasonCode, "ok");
        }
    }
}
