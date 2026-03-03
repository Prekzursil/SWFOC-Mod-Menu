using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterContextSpawnDefaultsTests
{
    [Theory]
    [InlineData("spawn_tactical_entity", "ForceZeroTactical", "EphemeralBattleOnly")]
    [InlineData("spawn_galactic_entity", "Normal", "PersistentGalactic")]
    public void ApplyContextSpawnPayloadDefaults_ShouldSetExpectedPolicies(string targetActionId, string expectedPopulation, string expectedPersistence)
    {
        var payload = new JsonObject();

        InvokePrivateStatic("ApplyContextSpawnPayloadDefaults", payload, targetActionId);

        payload["helperHookId"]!.GetValue<string>().Should().Be("spawn_bridge");
        payload["populationPolicy"]!.GetValue<string>().Should().Be(expectedPopulation);
        payload["persistencePolicy"]!.GetValue<string>().Should().Be(expectedPersistence);
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ApplyContextSymbolHint_ShouldInjectSymbol_WhenMissing()
    {
        var payload = new JsonObject();

        InvokePrivateStatic("ApplyContextSymbolHint", payload, "set_planet_owner");

        payload["symbol"]!.GetValue<string>().Should().Be("planet_owner");
    }

    [Theory]
    [InlineData("set_context_faction", "Faction")]
    [InlineData("set_context_allegiance", "Faction")]
    [InlineData("spawn_context_entity", "Spawn")]
    [InlineData("set_credits", "None")]
    public void ResolveContextRouteType_ShouldMapExpectedRoute(string actionId, string expectedRoute)
    {
        var routeType = InvokePrivateStatic("ResolveContextRouteType", actionId);
        routeType!.ToString().Should().Be(expectedRoute);
    }

    [Theory]
    [InlineData("runtimeModeOverride", "TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("runtimeModeOverride", "TacticalSpace", RuntimeMode.TacticalSpace)]
    [InlineData("runtimeModeOverride", "AnyTactical", RuntimeMode.AnyTactical)]
    [InlineData("runtimeModeOverride", "Auto", null)]
    public void ResolveManualOverrideMode_ShouldHandleKnownValues(string key, string value, RuntimeMode? expected)
    {
        var context = new Dictionary<string, object?>
        {
            [key] = value
        };

        var resolved = (RuntimeMode?)InvokePrivateStatic("ResolveManualOverrideMode", context);

        resolved.Should().Be(expected);
    }

    [Theory]
    [InlineData("Land", RuntimeMode.TacticalLand, true)]
    [InlineData("Space", RuntimeMode.TacticalSpace, true)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical, true)]
    [InlineData("Unknown", RuntimeMode.Unknown, false)]
    public void TryResolveTelemetryModeFromContext_ShouldParseContextOverride(string telemetry, RuntimeMode expectedMode, bool expectedParsed)
    {
        var context = new Dictionary<string, object?>
        {
            ["telemetryRuntimeMode"] = telemetry
        };

        var args = new object?[] { context, RuntimeMode.Unknown };
        var parsed = (bool)InvokePrivateStatic("TryResolveTelemetryModeFromContext", args)!;
        var mode = (RuntimeMode)args[1]!;

        parsed.Should().Be(expectedParsed);
        mode.Should().Be(expectedMode);
    }

    [Fact]
    public void CreateContextModeBlockedResult_ShouldUseStrictModeReasonCode()
    {
        var enumType = GetPrivateNestedType("ContextRouteType");
        var routeType = Enum.Parse(enumType, "Spawn");
        var result = (ActionExecutionResult)InvokePrivateStatic("CreateContextModeBlockedResult", routeType, RuntimeMode.Unknown)!;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.MODE_STRICT_TACTICAL_UNSPECIFIED.ToString());
    }

    [Fact]
    public void CreateContextMissingActionResult_ShouldIncludeRoutedActionIdDiagnostic()
    {
        var result = (ActionExecutionResult)InvokePrivateStatic(
            "CreateContextMissingActionResult",
            "profile_test",
            "set_planet_owner")!;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("routedActionId");
        result.Diagnostics!["routedActionId"]!.ToString().Should().Be("set_planet_owner");
    }

    [Fact]
    public void ClonePayload_ShouldDeepCloneNodes()
    {
        var payload = new JsonObject
        {
            ["entityId"] = "unit_a",
            ["nested"] = new JsonObject
            {
                ["count"] = 3
            }
        };

        var cloned = (JsonObject)InvokePrivateStatic("ClonePayload", payload)!;
        cloned["entityId"]!.GetValue<string>().Should().Be("unit_a");
        cloned["nested"]!.AsObject()["count"]!.GetValue<int>().Should().Be(3);

        payload["nested"]!.AsObject()["count"] = 99;
        cloned["nested"]!.AsObject()["count"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void ResolveHelperHookId_ShouldPreferPayloadHook_WhenProvided()
    {
        var request = BuildRequest("spawn_context_entity", new JsonObject
        {
            ["helperHookId"] = "custom_hook"
        });

        var hookId = (string?)InvokePrivateStatic("ResolveHelperHookId", request);

        hookId.Should().Be("custom_hook");
    }

    [Theory]
    [InlineData("spawn_context_entity", "spawn_bridge")]
    [InlineData("spawn_tactical_entity", "spawn_bridge")]
    [InlineData("spawn_galactic_entity", "spawn_bridge")]
    [InlineData("place_planet_building", "spawn_bridge")]
    [InlineData("toggle_ai", "toggle_ai")]
    public void ResolveHelperHookId_ShouldUseExpectedDefaults(string actionId, string expectedHook)
    {
        var request = BuildRequest(actionId, new JsonObject());

        var hookId = (string?)InvokePrivateStatic("ResolveHelperHookId", request);

        hookId.Should().Be(expectedHook);
    }

    [Theory]
    [InlineData("spawn_unit_helper", HelperBridgeOperationKind.SpawnUnitHelper)]
    [InlineData("spawn_context_entity", HelperBridgeOperationKind.SpawnContextEntity)]
    [InlineData("spawn_tactical_entity", HelperBridgeOperationKind.SpawnTacticalEntity)]
    [InlineData("spawn_galactic_entity", HelperBridgeOperationKind.SpawnGalacticEntity)]
    [InlineData("place_planet_building", HelperBridgeOperationKind.PlacePlanetBuilding)]
    [InlineData("set_context_faction", HelperBridgeOperationKind.SetContextAllegiance)]
    [InlineData("set_context_allegiance", HelperBridgeOperationKind.SetContextAllegiance)]
    [InlineData("set_hero_state_helper", HelperBridgeOperationKind.SetHeroStateHelper)]
    [InlineData("toggle_roe_respawn_helper", HelperBridgeOperationKind.ToggleRoeRespawnHelper)]
    [InlineData("unknown_action", HelperBridgeOperationKind.Unknown)]
    public void ResolveHelperOperationKind_ShouldMapKnownActions(string actionId, HelperBridgeOperationKind expected)
    {
        var operationKind = (HelperBridgeOperationKind)InvokePrivateStatic("ResolveHelperOperationKind", actionId)!;

        operationKind.Should().Be(expected);
    }

    [Fact]
    public void TryResolveContextFactionRequest_ShouldReturnNone_WhenActionIsNotContextRouted()
    {
        var adapter = CreateAdapter();
        var request = BuildRequest("set_credits", new JsonObject());

        var resolution = InvokePrivateInstance(adapter, "TryResolveContextFactionRequest", request);

        ReadProperty<ActionExecutionRequest?>(resolution, "RedirectedRequest").Should().BeNull();
        ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult").Should().BeNull();
    }

    [Fact]
    public void TryResolveContextFactionRequest_ShouldBlock_WhenRuntimeModeCannotRoute()
    {
        var adapter = CreateAdapter();
        var request = BuildRequest("spawn_context_entity", new JsonObject(), runtimeMode: RuntimeMode.Unknown);

        var resolution = InvokePrivateInstance(adapter, "TryResolveContextFactionRequest", request);
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");

        blocked.Should().NotBeNull();
        blocked!.Succeeded.Should().BeFalse();
        blocked.Diagnostics.Should().ContainKey("reasonCode");
        blocked.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.MODE_STRICT_TACTICAL_UNSPECIFIED.ToString());
    }

    [Fact]
    public void TryResolveContextFactionRequest_ShouldRedirect_WhenProfileHasTargetAction()
    {
        var adapter = CreateAdapter();
        SetAttachedProfile(adapter, BuildProfileWithActions("spawn_tactical_entity"));

        var request = BuildRequest("spawn_context_entity", new JsonObject(), runtimeMode: RuntimeMode.TacticalLand);
        var resolution = InvokePrivateInstance(adapter, "TryResolveContextFactionRequest", request);
        var redirected = ReadProperty<ActionExecutionRequest?>(resolution, "RedirectedRequest");

        redirected.Should().NotBeNull();
        redirected!.Action.Id.Should().Be("spawn_tactical_entity");
        redirected.Payload["helperHookId"]!.GetValue<string>().Should().Be("spawn_bridge");
        redirected.Payload["populationPolicy"]!.GetValue<string>().Should().Be("ForceZeroTactical");
        redirected.Payload["persistencePolicy"]!.GetValue<string>().Should().Be("EphemeralBattleOnly");
        redirected.Payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }

    private static Type GetPrivateNestedType(string typeName)
    {
        var type = typeof(RuntimeAdapter).GetNestedType(typeName, BindingFlags.NonPublic);
        type.Should().NotBeNull($"nested type '{typeName}' should exist.");
        return type!;
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] arguments)
    {
        var method = typeof(RuntimeAdapter).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"private static method '{methodName}' should exist.");
        return method!.Invoke(null, arguments);
    }

    private static object InvokePrivateInstance(object instance, string methodName, params object?[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"private instance method '{methodName}' should exist.");
        return method!.Invoke(instance, arguments)!;
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull($"property '{propertyName}' should exist.");
        return (T)property!.GetValue(instance)!;
    }

    private static RuntimeAdapter CreateAdapter()
    {
        return new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance);
    }

    private static void SetAttachedProfile(RuntimeAdapter adapter, TrainerProfile profile)
    {
        var field = typeof(RuntimeAdapter).GetField("_attachedProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(adapter, profile);
    }

    private static TrainerProfile BuildProfileWithActions(params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            static id => id,
            static id => new ActionSpec(
                id,
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static ActionExecutionRequest BuildRequest(string actionId, JsonObject payload, RuntimeMode runtimeMode = RuntimeMode.Galactic)
    {
        return new ActionExecutionRequest(
            Action: new ActionSpec(
                actionId,
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: runtimeMode,
            Context: null);
    }

    private sealed class StubProcessLocator : IProcessLocator
    {
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
            {
                _ = cancellationToken;
                return Task.FromResult<IReadOnlyList<ProcessMetadata>>(Array.Empty<ProcessMetadata>());
            }

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
            {
                _ = target;
                _ = cancellationToken;
                return Task.FromResult<ProcessMetadata?>(null);
            }
    }

    private sealed class StubProfileRepository : IProfileRepository
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
                throw new NotImplementedException();
            }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
            {
                _ = profileId;
                _ = cancellationToken;
                throw new NotImplementedException();
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

    private sealed class StubSignatureResolver : ISignatureResolver
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
            throw new NotImplementedException();
        }
    }
}


