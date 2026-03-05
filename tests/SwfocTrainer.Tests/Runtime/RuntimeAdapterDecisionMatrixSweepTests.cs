#pragma warning disable CA1014
using System;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

[CLSCompliant(false)]
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
        var harness = new AdapterHarness
        {
            Router = CreateRouter(actionId, backend, allowed, variant),
            HelperBridgeBackend = CreateHelperBridgeBackend(variant),
            MechanicDetectionService = CreateMechanicDetectionService(actionId, variant)
        };

        return ApplyExceptionalHarnessOverrides(harness, variant);
    }

    private static StubBackendRouter CreateRouter(string actionId, ExecutionBackendKind backend, bool allowed, int variant)
    {
        var routeReasonCode = allowed
            ? RuntimeReasonCode.CAPABILITY_PROBE_PASS
            : RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING;

        return new StubBackendRouter(
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
                }));
    }

    private static StubHelperBridgeBackend CreateHelperBridgeBackend(int variant)
    {
        var failed = variant % 5 == 0;
        return new StubHelperBridgeBackend
        {
            ExecuteResult = new HelperBridgeExecutionResult(
                Succeeded: !failed,
                ReasonCode: failed ? RuntimeReasonCode.HELPER_VERIFICATION_FAILED : RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
                Message: failed ? "verify_failed" : "applied",
                Diagnostics: new Dictionary<string, object?>
                {
                    ["helperVerifyState"] = failed ? "failed" : "applied",
                    ["operationToken"] = $"token-{variant:0000}"
                })
        };
    }

    private static IModMechanicDetectionService CreateMechanicDetectionService(string actionId, int variant)
    {
        var supported = variant % 7 != 0;
        return new StubMechanicDetectionService(
            supported,
            actionId,
            supported ? RuntimeReasonCode.CAPABILITY_PROBE_PASS : RuntimeReasonCode.MECHANIC_NOT_SUPPORTED_FOR_CHAIN,
            supported ? "supported" : "unsupported");
    }

    private static AdapterHarness ApplyExceptionalHarnessOverrides(AdapterHarness harness, int variant)
    {
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
        var factionPair = ResolveFactionPair(variant);

        ApplyPayloadDefaults(payload, actionId, factionPair, variant);
        return payload;
    }

    private static void ApplyPayloadDefaults(JsonObject payload, string actionId, (string target, string source) factionPair, int variant)
    {
        SetIfMissing(payload, "actionId", actionId);
        SetIfMissing(payload, "helperHookId", "spawn_bridge");
        SetIfMissing(payload, "entityId", "EMP_STORMTROOPER");
        SetIfMissing(payload, "targetFaction", factionPair.target);
        SetIfMissing(payload, "sourceFaction", factionPair.source);
        SetIfMissing(payload, "placementMode", ResolvePlacementMode(variant));
        SetIfMissing(payload, "allowCrossFaction", variant % 3 != 0);
        SetIfMissing(payload, "forceOverride", variant % 4 == 0);
    }

    private static void SetIfMissing(JsonObject payload, string key, string value)
    {
        if (payload[key] is null)
        {
            payload[key] = value;
        }
    }

    private static void SetIfMissing(JsonObject payload, string key, bool value)
    {
        if (payload[key] is null)
        {
            payload[key] = value;
        }
    }

    private static (string target, string source) ResolveFactionPair(int variant)
    {
        return variant % 2 == 0 ? ("Empire", "Rebel") : ("Rebel", "Empire");
    }

    private static string ResolvePlacementMode(int variant)
    {
        return variant % 2 == 0 ? "safe_rules" : "anywhere";
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

#pragma warning restore CA1014
