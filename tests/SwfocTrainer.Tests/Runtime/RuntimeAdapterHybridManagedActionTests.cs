using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterHybridManagedActionTests
{
    [Theory]
    [InlineData("freeze_timer")]
    [InlineData("toggle_fog_reveal")]
    [InlineData("toggle_ai")]
    [InlineData("set_unit_cap")]
    [InlineData("toggle_instant_build_patch")]
    public async Task ExecuteHybridManagedActionAsync_ShouldRoutePromotedSdkActions_ThroughSdkPath(string actionId)
    {
        var sdkRouter = new RecordingSdkOperationRouter();
        var adapter = CreateAdapter(sdkRouter);
        var request = BuildSdkRequest(actionId);

        var result = await InvokeExecuteHybridManagedActionAsync(adapter, request);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("sdk ok");
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics.Should().ContainKey("sdkReasonCode");
        result.Diagnostics!["sdkReasonCode"].Should().Be(CapabilityReasonCode.AllRequiredAnchorsPresent.ToString());
        result.Diagnostics.Should().ContainKey("sdkCapabilityState");
        result.Diagnostics!["sdkCapabilityState"].Should().Be(SdkCapabilityStatus.Available.ToString());
        sdkRouter.CallCount.Should().Be(1);
        sdkRouter.LastRequest.Should().NotBeNull();
        sdkRouter.LastRequest!.OperationId.Should().Be(actionId);
    }

    private static ActionExecutionRequest BuildSdkRequest(string actionId)
    {
        var action = new ActionSpec(
            Id: actionId,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Galactic,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0,
            Description: "test");

        return new ActionExecutionRequest(
            Action: action,
            Payload: new JsonObject { ["enable"] = true },
            ProfileId: "roe_3447786229_swfoc",
            RuntimeMode: RuntimeMode.Galactic);
    }

    private static RuntimeAdapter CreateAdapter(ISdkOperationRouter sdkRouter)
    {
        return new RuntimeAdapter(
            processLocator: new NoOpProcessLocator(),
            profileRepository: new ThrowingProfileRepository(),
            signatureResolver: new EmptySignatureResolver(),
            logger: NullLogger<RuntimeAdapter>.Instance,
            serviceProvider: new TestServiceProvider(sdkRouter));
    }

    private static async Task<ActionExecutionResult> InvokeExecuteHybridManagedActionAsync(
        RuntimeAdapter adapter,
        ActionExecutionRequest request)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ExecuteHybridManagedActionAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Should().NotBeNull("RuntimeAdapter should route promoted hybrid actions through managed handlers.");
        var invoked = method!.Invoke(adapter, new object?[] { request, CancellationToken.None });
        invoked.Should().BeAssignableTo<Task<ActionExecutionResult>>();
        return await (Task<ActionExecutionResult>)invoked!;
    }

    private sealed class RecordingSdkOperationRouter : ISdkOperationRouter
    {
        public int CallCount { get; private set; }

        public SdkOperationRequest? LastRequest { get; private set; }

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request)
        {
            return ExecuteAsync(request, CancellationToken.None);
        }

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new SdkOperationResult(
                Succeeded: true,
                Message: "sdk ok",
                ReasonCode: CapabilityReasonCode.AllRequiredAnchorsPresent,
                CapabilityState: SdkCapabilityStatus.Available));
        }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly ISdkOperationRouter _sdkRouter;

        public TestServiceProvider(ISdkOperationRouter sdkRouter)
        {
            _sdkRouter = sdkRouter;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ISdkOperationRouter))
            {
                return _sdkRouter;
            }

            return null;
        }
    }

    private sealed class NoOpProcessLocator : IProcessLocator
    {
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ProcessMetadata>>(Array.Empty<ProcessMetadata>());
        }

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ProcessMetadata?>(null);
        }
    }

    private sealed class ThrowingProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken = default) => throw CreateNotUsedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw CreateNotUsedException();

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw CreateNotUsedException();

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken = default) => throw CreateNotUsedException();

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken = default) => throw CreateNotUsedException();

        private static NotSupportedException CreateNotUsedException()
        {
            return new NotSupportedException("Profile repository should not be called in this unit test.");
        }
    }

    private sealed class EmptySignatureResolver : ISignatureResolver
    {
        public Task<SymbolMap> ResolveAsync(
            ProfileBuild profileBuild,
            IReadOnlyList<SignatureSet> signatureSets,
            IReadOnlyDictionary<string, long> fallbackOffsets,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)));
        }
    }
}
