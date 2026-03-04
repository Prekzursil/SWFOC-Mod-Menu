#pragma warning disable CA1014
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;

namespace SwfocTrainer.Tests.Runtime;

internal static partial class ReflectionCoverageVariantFactory
{
    private static RuntimeAdapter CreateRuntimeAdapterInstance(bool alternate)
    {
        var profile = BuildProfile();
        var harness = new AdapterHarness();
        if (alternate)
        {
            harness.Router = new StubBackendRouter(new BackendRouteDecision(
                Allowed: false,
                Backend: ExecutionBackendKind.Helper,
                ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
                Message: "blocked"));
            harness.HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ProbeResult = new HelperBridgeProbeResult(
                    Available: false,
                    ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                    Message: "bridge unavailable")
            };
            harness.DependencyValidator = new StubDependencyValidator(new DependencyValidationResult(
                DependencyValidationStatus.SoftFail,
                "missing_parent",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "2313576303" }));
            harness.MechanicDetectionService = new StubMechanicDetectionService(
                supported: false,
                actionId: "spawn_tactical_entity",
                reasonCode: RuntimeReasonCode.MECHANIC_NOT_SUPPORTED_FOR_CHAIN,
                message: "unsupported");
        }

        return harness.CreateAdapter(profile, alternate ? RuntimeMode.TacticalLand : RuntimeMode.Galactic);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new Dictionary<Type, object>
        {
            [typeof(IBackendRouter)] = new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Helper,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok")),
            [typeof(IExecutionBackend)] = new StubExecutionBackend(),
            [typeof(IHelperBridgeBackend)] = new StubHelperBridgeBackend(),
            [typeof(IModDependencyValidator)] = new StubDependencyValidator(new DependencyValidationResult(
                DependencyValidationStatus.Pass,
                string.Empty,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase))),
            [typeof(ITelemetryLogTailService)] = new StubTelemetryLogTailService()
        };

        return new MapServiceProvider(services);
    }
}
#pragma warning restore CA1014
