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

    [Fact]
    public async Task ExecuteAsync_SingleParamOverload_ShouldReturnFeatureFlagDisabled_WhenGateOff()
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
                ProfileId: "test");

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.FeatureFlagDisabled);
            result.CapabilityState.Should().Be(SdkCapabilityStatus.Unavailable);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnRuntimeNotAttached_WhenProcessPathIsWhitespace()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "   "
                });

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
    public async Task ExecuteAsync_ShouldReturnUnknownOperation_WhenOperationIdNotInCatalog()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var router = CreateRouter();
            var request = new SdkOperationRequest(
                OperationId: "nonexistent_operation_xyzzy",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Unknown,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 999
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.UnknownSdkOperation);
            result.Message.Should().Contain("nonexistent_operation_xyzzy");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnBlockedResult_WhenGuardDenies()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var router = CreateRouter(guard: new BlockingExecutionGuard());
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Unknown,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.MutationBlockedByCapabilityState);
            result.Diagnostics.Should().NotBeNull();
            result.Diagnostics!.Should().ContainKey("resolvedVariant");
            result.Diagnostics.Should().ContainKey("fingerprintId");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRouteToRuntimeAdapter_WhenAllGatesPass()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var adapter = new FakeSdkRuntimeAdapter();
            var router = CreateRouter(adapter: adapter);
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Galactic,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
            result.ReasonCode.Should().Be(CapabilityReasonCode.AllRequiredAnchorsPresent);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleProcessIdAsLong_InContext()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = (long)42
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleProcessIdAsString_InContext()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = "42"
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleProcessIdUnparseable_InContext()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = "not_a_number"
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleProcessIdAsNull_InContext()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = null
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleMissingProcessId_InContext()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe"
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldParseResolvedAnchors_AsStringEnumerable()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 42,
                    ["resolvedAnchors"] = new List<string> { "anchor_a", "anchor_b" }
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldParseResolvedAnchors_AsJsonArray()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var router = CreateRouter();
            var anchors = new JsonArray(JsonValue.Create("anchor_x")!, JsonValue.Create("anchor_y")!);
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Unknown,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 42,
                    ["resolvedAnchors"] = anchors
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldParseResolvedAnchors_AsSerializedJsonString()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 42,
                    ["resolvedAnchors"] = "[\"anchor_a\",\"anchor_b\"]"
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleResolvedAnchors_AsBadJsonString()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 42,
                    ["resolvedAnchors"] = "this-is-not-json"
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleResolvedAnchors_AsUnrecognizedType()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 42,
                    ["resolvedAnchors"] = 12345
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleResolvedAnchors_AsNull()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 42,
                    ["resolvedAnchors"] = null
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnRuntimeNotAttached_WhenNullContext()
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
                ProfileId: "test",
                Context: null);

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
    public async Task ExecuteAsync_ShouldAllowModeMatch_ForMutationInAllowedMode()
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
                RuntimeMode: RuntimeMode.TacticalLand,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMergeContextWithCapabilityAndVariant()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var capturingAdapter = new CapturingSdkRuntimeAdapter();
            var router = CreateRouter(adapter: capturingAdapter);
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Galactic,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100,
                    ["customKey"] = "customValue"
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
            capturingAdapter.CapturedRequest.Should().NotBeNull();
            capturingAdapter.CapturedRequest!.Context.Should().ContainKey("resolvedVariant");
            capturingAdapter.CapturedRequest.Context.Should().ContainKey("capabilityState");
            capturingAdapter.CapturedRequest.Context.Should().ContainKey("customKey");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleProcessIdAsLongOutOfIntRange()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = (long)int.MaxValue + 1
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldHandleProcessPathAsNonStringObject()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = 12345,
                    ["processId"] = 42
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMergeContextWithNullOriginal()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var capturingAdapter = new CapturingSdkRuntimeAdapter();
            var router = CreateRouter(adapter: capturingAdapter);
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Galactic,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
            capturingAdapter.CapturedRequest.Should().NotBeNull();
            capturingAdapter.CapturedRequest!.Context.Should().ContainKey("resolvedVariant");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public void Constructor_FiveParamOverload_ShouldUseNullDiagnosticsSink()
    {
        var router = new SdkOperationRouter(
            new FakeSdkRuntimeAdapter(),
            new FakeProfileVariantResolver(),
            new FakeBinaryFingerprintService(),
            new FakeCapabilityMapResolver(),
            new FakeSdkExecutionGuard());

        router.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenAnyDependencyIsNull()
    {
        var adapter = new FakeSdkRuntimeAdapter();
        var resolver = new FakeProfileVariantResolver();
        var fingerprint = new FakeBinaryFingerprintService();
        var capability = new FakeCapabilityMapResolver();
        var guard = new FakeSdkExecutionGuard();
        var sink = new NullSdkDiagnosticsSink();

        var act1 = () => new SdkOperationRouter(null!, resolver, fingerprint, capability, guard, sink);
        var act2 = () => new SdkOperationRouter(adapter, null!, fingerprint, capability, guard, sink);
        var act3 = () => new SdkOperationRouter(adapter, resolver, null!, capability, guard, sink);
        var act4 = () => new SdkOperationRouter(adapter, resolver, fingerprint, null!, guard, sink);
        var act5 = () => new SdkOperationRouter(adapter, resolver, fingerprint, capability, null!, sink);
        var act6 = () => new SdkOperationRouter(adapter, resolver, fingerprint, capability, guard, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
        act4.Should().Throw<ArgumentNullException>();
        act5.Should().Throw<ArgumentNullException>();
        act6.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenRequestIsNull()
    {
        var router = CreateRouter();

        var act1 = async () => await router.ExecuteAsync((SdkOperationRequest)null!);
        var act2 = async () => await router.ExecuteAsync((SdkOperationRequest)null!, CancellationToken.None);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBlockModeMismatch_WhenUnknownModeOnMutationNotAllowingUnknown()
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
                RuntimeMode: RuntimeMode.Unknown,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.ModeMismatch);
            result.Message.Should().Contain("blocked in mode");
            result.Diagnostics.Should().ContainKey("runtimeMode");
            result.Diagnostics.Should().ContainKey("allowedModes");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowReadOnly_InUnknownMode()
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
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ShouldAllowSpawn_InTacticalSpaceMode()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");

        try
        {
            var router = CreateRouter();
            var request = new SdkOperationRequest(
                OperationId: "spawn",
                Payload: new JsonObject(),
                IsMutation: true,
                RuntimeMode: RuntimeMode.TacticalSpace,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = "C:/games/swfoc.exe",
                    ["processId"] = 100
                });

            var result = await router.ExecuteAsync(request);

            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    private static SdkOperationRouter CreateRouter(
        ISdkRuntimeAdapter? adapter = null,
        ISdkExecutionGuard? guard = null)
    {
        return new SdkOperationRouter(
            adapter ?? new FakeSdkRuntimeAdapter(),
            new FakeProfileVariantResolver(),
            new FakeBinaryFingerprintService(),
            new FakeCapabilityMapResolver(),
            guard ?? new FakeSdkExecutionGuard(),
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

    private sealed class CapturingSdkRuntimeAdapter : ISdkRuntimeAdapter
    {
        public SdkOperationRequest? CapturedRequest { get; private set; }

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request)
        {
            return ExecuteAsync(request, CancellationToken.None);
        }

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
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
            _ = fingerprint;
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

    private sealed class BlockingExecutionGuard : ISdkExecutionGuard
    {
        public SdkExecutionDecision CanExecute(CapabilityResolutionResult resolution, bool isMutation)
        {
            _ = isMutation;
            return new SdkExecutionDecision(false, CapabilityReasonCode.MutationBlockedByCapabilityState, "blocked by test guard");
        }
    }
}
