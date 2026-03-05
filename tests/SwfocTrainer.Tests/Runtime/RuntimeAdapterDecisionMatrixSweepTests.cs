using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterDecisionMatrixSweepTests
{
    private static readonly RuntimeMode[] Modes =
    [
        RuntimeMode.Unknown,
        RuntimeMode.Galactic,
        RuntimeMode.TacticalLand,
        RuntimeMode.TacticalSpace,
        RuntimeMode.AnyTactical
    ];

    private static readonly ExecutionBackendKind[] Backends =
    [
        ExecutionBackendKind.Helper,
        ExecutionBackendKind.Extender,
        ExecutionBackendKind.Memory
    ];

    [Fact]
    public async Task ExecuteAsync_ShouldTraverseLargeDecisionMatrixWithoutUnhandledExceptions()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile();
        var actionIds = profile.Actions.Keys
            .Where(static id => !id.Contains("noop", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var executed = 0;
        var successful = 0;

        for (var actionIndex = 0; actionIndex < actionIds.Length; actionIndex++)
        {
            var actionId = actionIds[actionIndex];
            var action = profile.Actions[actionId];

            foreach (var mode in Modes)
            {
                foreach (var backend in Backends)
                {
                    foreach (var allowed in new[] { true, false })
                    {
                        var variant = actionIndex + executed;
                        var harness = BuildHarness(actionId, backend, allowed, variant);
                        var adapter = harness.CreateAdapter(profile, mode);

                        var payload = BuildPayload(variant, actionId);
                        var context = BuildContext(mode, variant);
                        var request = new ActionExecutionRequest(action, payload, profile.Id, mode, context);

                        var result = await adapter.ExecuteAsync(request, CancellationToken.None);
                        executed++;
                        if (result.Succeeded)
                        {
                            successful++;
                        }

                        result.Should().NotBeNull();
                        result.Diagnostics.Should().NotBeNull();
                    }
                }
            }
        }

        executed.Should().BeGreaterThan(400);
        successful.Should().BeGreaterThan(40);
    }

    private static AdapterHarness BuildHarness(string actionId, ExecutionBackendKind backend, bool allowed, int variant)
    {
        var routeReasonCode = allowed
            ? RuntimeReasonCode.CAPABILITY_PROBE_PASS
            : RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING;

        var harness = new AdapterHarness
        {
            Router = new StubBackendRouter(
                new BackendRouteDecision(
                    Allowed: allowed,
                    Backend: backend,
                    ReasonCode: routeReasonCode,
                    Message: allowed ? "allowed" : "blocked",
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["matrixActionId"] = actionId,
                        ["matrixVariant"] = variant,
                        ["matrixBackend"] = backend.ToString(),
                        ["matrixAllowed"] = allowed
                    })),
            HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ExecuteResult = new HelperBridgeExecutionResult(
                    Succeeded: variant % 5 != 0,
                    ReasonCode: variant % 5 == 0 ? RuntimeReasonCode.HELPER_VERIFICATION_FAILED : RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
                    Message: variant % 5 == 0 ? "verify_failed" : "applied",
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["helperVerifyState"] = variant % 5 == 0 ? "failed" : "applied",
                        ["operationToken"] = $"token-{variant:0000}"
                    })
            },
            MechanicDetectionService = new StubMechanicDetectionService(
                supported: variant % 7 != 0,
                actionId: actionId,
                reasonCode: variant % 7 == 0 ? RuntimeReasonCode.MECHANIC_NOT_SUPPORTED_FOR_CHAIN : RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                message: variant % 7 == 0 ? "unsupported" : "supported")
        };

        if (variant % 11 == 0)
        {
            harness.MechanicDetectionService = new ThrowingMechanicDetectionService();
        }

        if (variant % 13 == 0)
        {
            harness.HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ExecuteException = new InvalidOperationException("helper exception")
            };
        }

        return harness;
    }

    private static JsonObject BuildPayload(int variant, string actionId)
    {
        var payload = (JsonObject?)ReflectionCoverageVariantFactory.BuildArgument(typeof(JsonObject), variant) ?? new JsonObject();
        payload["actionId"] = actionId;
        payload["helperHookId"] = payload["helperHookId"] ?? "spawn_bridge";
        payload["entityId"] = payload["entityId"] ?? "EMP_STORMTROOPER";
        payload["targetFaction"] = payload["targetFaction"] ?? (variant % 2 == 0 ? "Empire" : "Rebel");
        payload["sourceFaction"] = payload["sourceFaction"] ?? (variant % 2 == 0 ? "Rebel" : "Empire");
        payload["placementMode"] = payload["placementMode"] ?? (variant % 2 == 0 ? "safe_rules" : "anywhere");
        payload["allowCrossFaction"] = payload["allowCrossFaction"] ?? (variant % 3 != 0);
        payload["forceOverride"] = payload["forceOverride"] ?? (variant % 4 == 0);
        return payload;
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(RuntimeMode mode, int variant)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeMode"] = mode.ToString(),
            ["runtimeModeOverride"] = mode.ToString(),
            ["allowExpertMutationOverride"] = variant % 4 == 0,
            ["selectedPlanetId"] = variant % 2 == 0 ? "Kuat" : "Coruscant",
            ["selectedFaction"] = variant % 2 == 0 ? "Empire" : "Rebel",
            ["chainId"] = "matrix",
            ["variant"] = variant
        };
    }
}
