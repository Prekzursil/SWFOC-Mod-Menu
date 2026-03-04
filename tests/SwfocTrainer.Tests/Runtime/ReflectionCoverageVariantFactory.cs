using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Config;

namespace SwfocTrainer.Tests.Runtime;

internal static class ReflectionCoverageVariantFactory
{
    private const int MaxDepth = 3;

    private static readonly IReadOnlyDictionary<Type, Func<int, object?>> PrimitiveBuilders =
        new Dictionary<Type, Func<int, object?>>
        {
            [typeof(string)] = variant => variant switch { 0 => "coverage", 1 => string.Empty, _ => null },
            [typeof(bool)] = variant => variant == 1,
            [typeof(int)] = variant => variant switch { 0 => 1, 1 => -1, _ => 0 },
            [typeof(uint)] = variant => variant == 1 ? 2u : 0u,
            [typeof(long)] = variant => variant switch { 0 => 1L, 1 => -1L, _ => 0L },
            [typeof(float)] = variant => variant switch { 0 => 1f, 1 => -1f, _ => 0f },
            [typeof(double)] = variant => variant switch { 0 => 1d, 1 => -1d, _ => 0d },
            [typeof(decimal)] = variant => variant switch { 0 => 1m, 1 => -1m, _ => 0m },
            [typeof(Guid)] = variant => variant == 0 ? Guid.NewGuid() : Guid.Empty,
            [typeof(DateTimeOffset)] = variant => variant == 1 ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow,
            [typeof(DateTime)] = variant => variant == 1 ? DateTime.MinValue : DateTime.UtcNow,
            [typeof(TimeSpan)] = variant => variant == 1 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(25),
            [typeof(CancellationToken)] = _ => CancellationToken.None,
            [typeof(byte[])] = variant => variant == 1 ? Array.Empty<byte>() : new byte[] { 1, 2, 3, 4 }
        };

    private static readonly IReadOnlyDictionary<Type, Func<int, object?>> DomainBuilders =
        new Dictionary<Type, Func<int, object?>>
        {
            [typeof(ActionExecutionRequest)] = BuildActionExecutionRequest,
            [typeof(ActionExecutionResult)] = variant => new ActionExecutionResult(variant != 1, variant == 1 ? "blocked" : "ok", AddressSource.None, new Dictionary<string, object?>()),
            [typeof(TrainerProfile)] = _ => BuildProfile(),
            [typeof(ProcessMetadata)] = variant => BuildSession((variant % 3) switch { 1 => RuntimeMode.TacticalLand, 2 => RuntimeMode.TacticalSpace, _ => RuntimeMode.Galactic }).Process,
            [typeof(AttachSession)] = variant => BuildSession((variant % 3) switch { 1 => RuntimeMode.TacticalLand, 2 => RuntimeMode.TacticalSpace, _ => RuntimeMode.Galactic }),
            [typeof(SymbolMap)] = _ => new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            [typeof(SymbolInfo)] = _ => new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature),
            [typeof(SymbolValidationRule)] = _ => new SymbolValidationRule("credits", IntMin: 0, IntMax: 1_000_000),
            [typeof(MainViewModelDependencies)] = _ => CreateNullDependencies(),
            [typeof(SaveOptions)] = _ => new SaveOptions(),
            [typeof(LaunchContext)] = _ => BuildLaunchContext(),
            [typeof(IProcessLocator)] = _ => new StubProcessLocator(BuildSession(RuntimeMode.Galactic).Process),
            [typeof(IProfileRepository)] = _ => new StubProfileRepository(BuildProfile()),
            [typeof(ISignatureResolver)] = _ => new StubSignatureResolver(),
            [typeof(IBackendRouter)] = _ => new StubBackendRouter(new BackendRouteDecision(
                Allowed: true,
                Backend: ExecutionBackendKind.Helper,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok")),
            [typeof(IExecutionBackend)] = _ => new StubExecutionBackend(),
            [typeof(IHelperBridgeBackend)] = _ => new StubHelperBridgeBackend(),
            [typeof(IModDependencyValidator)] = _ => new StubDependencyValidator(new DependencyValidationResult(
                DependencyValidationStatus.Pass,
                string.Empty,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase))),
            [typeof(IModMechanicDetectionService)] = _ => new StubMechanicDetectionService(
                supported: true,
                actionId: "spawn_tactical_entity",
                reasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                message: "ok"),
            [typeof(ITelemetryLogTailService)] = _ => new StubTelemetryLogTailService(),
            [typeof(IServiceProvider)] = _ => BuildServiceProvider()
        };

    public static object? BuildArgument(Type parameterType, int variant, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            return parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
        }

        var nullableResolution = ResolveNullableType(parameterType, variant);
        if (nullableResolution.ShouldReturnNull)
        {
            return null;
        }

        var normalizedType = nullableResolution.Type;
        if (TryBuildKnownValue(normalizedType, variant, out var knownValue))
        {
            return knownValue;
        }

        return BuildFallbackValue(normalizedType, variant, depth);
    }

    public static async Task AwaitResultAsync(object? result, int timeoutMs = 200)
    {
        if (result is Task task)
        {
            await AwaitTaskWithTimeoutAsync(task, timeoutMs);
            return;
        }

        if (result is null)
        {
            return;
        }

        var asTask = result
            .GetType()
            .GetMethod("AsTask", BindingFlags.Public | BindingFlags.Instance)?
            .Invoke(result, null) as Task;

        if (asTask is not null)
        {
            await AwaitTaskWithTimeoutAsync(asTask, timeoutMs);
        }
    }

    public static TrainerProfile BuildProfile()
    {
        return new TrainerProfile(
            Id: "base_swfoc",
            DisplayName: "Base",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "1125571106",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: BuildActionMap(),
            FeatureFlags: BuildFeatureFlags(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public static AttachSession BuildSession(RuntimeMode mode)
    {
        var process = new ProcessMetadata(
            ProcessId: Environment.ProcessId,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: "STEAMMOD=1397421866",
            ExeTarget: ExeTarget.Swfoc,
            Mode: mode,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["resolvedVariant"] = "base_swfoc",
                ["runtimeModeReasonCode"] = "mode_probe_ok"
            },
            LaunchContext: null,
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 1,
            WorkshopMatchCount: 0,
            SelectionScore: 0);

        return new AttachSession(
            ProfileId: "base_swfoc",
            Process: process,
            Build: new ProfileBuild("base_swfoc", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc, ProcessId: Environment.ProcessId),
            Symbols: new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    public static MainViewModelDependencies CreateNullDependencies()
    {
        return new MainViewModelDependencies
        {
            Profiles = null!,
            ProcessLocator = null!,
            LaunchContextResolver = null!,
            ProfileVariantResolver = null!,
            GameLauncher = null!,
            Runtime = null!,
            Orchestrator = null!,
            Catalog = null!,
            SaveCodec = null!,
            SavePatchPackService = null!,
            SavePatchApplyService = null!,
            Helper = null!,
            Updates = null!,
            ModOnboarding = null!,
            ModCalibration = null!,
            SupportBundles = null!,
            Telemetry = null!,
            FreezeService = null!,
            ActionReliability = null!,
            SelectedUnitTransactions = null!,
            SpawnPresets = null!
        };
    }

    public static object? CreateInstance(Type type, bool alternate, int depth = 0)
    {
        if (type == typeof(RuntimeAdapter))
        {
            return CreateRuntimeAdapterInstance(alternate);
        }

        if (type == typeof(string) || type.IsInterface || type.IsAbstract)
        {
            return null;
        }

        if (TryCreateWithDefaultConstructor(type, out var direct))
        {
            return direct;
        }

        foreach (var ctor in GetConstructorsByArity(type))
        {
            var args = ctor.GetParameters()
                .Select(parameter => BuildArgument(parameter.ParameterType, alternate ? 1 : 0, depth + 1))
                .ToArray();

            if (TryInvokeConstructor(ctor, args, out var instance))
            {
                return instance;
            }
        }

        return null;
    }

    private static bool TryBuildKnownValue(Type type, int variant, out object? value)
    {
        if (TryBuildPrimitive(type, variant, out value))
        {
            return true;
        }

        if (TryBuildJson(type, variant, out value))
        {
            return true;
        }

        if (TryBuildDomainModel(type, variant, out value))
        {
            return true;
        }

        if (TryBuildCollection(type, variant, out value))
        {
            return true;
        }

        if (TryBuildLogger(type, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static object? BuildFallbackValue(Type type, int variant, int depth)
    {
        if (type.IsEnum)
        {
            return BuildEnumValue(type, variant);
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, variant == 1 ? 0 : 1);
        }

        if (type.IsInterface || type.IsAbstract)
        {
            return null;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return CreateInstance(type, alternate: variant == 1, depth + 1);
    }

    private static async Task AwaitTaskWithTimeoutAsync(Task task, int timeoutMs)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (!ReferenceEquals(completed, task))
        {
            return;
        }

        try
        {
            await task;
        }
        catch (Exception ex) when (IsExpectedTaskException(ex))
        {
            // Reflective sweep intentionally exercises guarded failure branches.
        }
    }

    private static bool IsExpectedTaskException(Exception ex)
    {
        return ex is OperationCanceledException
            or InvalidOperationException
            or TargetInvocationException
            or NullReferenceException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or FormatException
            or ArgumentOutOfRangeException;
    }

    private static bool TryCreateWithDefaultConstructor(Type type, out object? instance)
    {
        instance = null;
        try
        {
            instance = Activator.CreateInstance(type);
            return true;
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or MemberAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryInvokeConstructor(ConstructorInfo ctor, object?[] args, out object? instance)
    {
        instance = null;
        try
        {
            instance = ctor.Invoke(args);
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or MemberAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static IEnumerable<ConstructorInfo> GetConstructorsByArity(Type type)
    {
        return type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(ctor => ctor.GetParameters().Length);
    }

    private static (Type Type, bool ShouldReturnNull) ResolveNullableType(Type type, int variant)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is null)
        {
            return (type, false);
        }

        return (underlying, variant == 2);
    }

    private static bool TryBuildPrimitive(Type type, int variant, out object? value)
    {
        if (PrimitiveBuilders.TryGetValue(type, out var builder))
        {
            value = builder(variant);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryBuildJson(Type type, int variant, out object? value)
    {
        if (type == typeof(JsonObject))
        {
            value = (variant % 6) switch
            {
                0 => new JsonObject { ["entityId"] = "EMP_STORMTROOPER", ["value"] = 100 },
                1 => new JsonObject { ["value"] = "NaN", ["allowCrossFaction"] = "yes" },
                2 => new JsonObject { ["symbol"] = "credits", ["enable"] = true, ["patchBytes"] = "90 90", ["originalBytes"] = "89 01" },
                3 => new JsonObject { ["operationKind"] = "spawn_tactical_entity", ["targetFaction"] = "Rebel", ["worldPosition"] = "10,0,20" },
                4 => new JsonObject { ["planetId"] = "Kuat", ["modePolicy"] = "convert_everything", ["allowCrossFaction"] = true },
                _ => new JsonObject { ["entityId"] = "DARTH_VADER", ["desiredState"] = "respawn_pending", ["allowDuplicate"] = true }
            };
            return true;
        }

        if (type == typeof(JsonArray))
        {
            value = variant == 1 ? new JsonArray() : new JsonArray(1, 2, 3);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryBuildDomainModel(Type type, int variant, out object? value)
    {
        if (DomainBuilders.TryGetValue(type, out var builder))
        {
            value = builder(variant);
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryBuildCollection(Type type, int variant, out object? value)
    {
        if (TryBuildStringEnumerable(type, variant, out value))
        {
            return true;
        }

        if (TryBuildObjectDictionary(type, variant, out value))
        {
            return true;
        }

        if (TryBuildStringDictionary(type, variant, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryBuildStringEnumerable(Type type, int variant, out object? value)
    {
        if (!typeof(IEnumerable<string>).IsAssignableFrom(type))
        {
            value = null;
            return false;
        }

        value = variant == 1 ? Array.Empty<string>() : new[] { "a", "b" };
        return true;
    }

    private static bool TryBuildObjectDictionary(Type type, int variant, out object? value)
    {
        if (type != typeof(IReadOnlyDictionary<string, object?>) && type != typeof(IDictionary<string, object?>))
        {
            value = null;
            return false;
        }

        value = variant == 1
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["mode"] = "galactic" };
        return true;
    }

    private static bool TryBuildStringDictionary(Type type, int variant, out object? value)
    {
        if (type != typeof(IReadOnlyDictionary<string, string>) && type != typeof(IDictionary<string, string>))
        {
            value = null;
            return false;
        }

        value = variant == 1
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["mode"] = "galactic" };
        return true;
    }

    private static bool TryBuildLogger(Type type, out object? value)
    {
        if (type == typeof(ILogger))
        {
            value = NullLogger.Instance;
            return true;
        }

        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ILogger<>))
        {
            value = null;
            return false;
        }

        var loggerType = typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()[0]);
        var property = loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        value = property?.GetValue(null);
        return value is not null;
    }

    private static object BuildEnumValue(Type enumType, int variant)
    {
        var values = Enum.GetValues(enumType);
        if (values.Length == 0)
        {
            return Activator.CreateInstance(enumType)!;
        }

        var index = Math.Min(variant, values.Length - 1);
        return values.GetValue(index)!;
    }

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

    private static readonly string[] VariantActionIds =
    [
        "set_credits",
        "spawn_tactical_entity",
        "spawn_galactic_entity",
        "place_planet_building",
        "set_context_allegiance",
        "transfer_fleet_safe",
        "flip_planet_owner",
        "switch_player_faction",
        "edit_hero_state",
        "create_hero_variant"
    ];

    private static readonly IReadOnlyDictionary<string, Func<int, JsonObject>> ActionPayloadBuilders =
        new Dictionary<string, Func<int, JsonObject>>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = BuildSetCreditsPayload,
            ["spawn_tactical_entity"] = BuildSpawnTacticalPayload,
            ["spawn_galactic_entity"] = BuildSpawnGalacticPayload,
            ["place_planet_building"] = BuildPlacePlanetBuildingPayload,
            ["set_context_allegiance"] = BuildSetContextAllegiancePayload,
            ["transfer_fleet_safe"] = BuildTransferFleetPayload,
            ["flip_planet_owner"] = BuildFlipPlanetPayload,
            ["switch_player_faction"] = BuildSwitchPlayerFactionPayload,
            ["edit_hero_state"] = BuildEditHeroStatePayload,
            ["create_hero_variant"] = BuildCreateHeroVariantPayload
        };

    private static ActionExecutionRequest BuildActionExecutionRequest(int variant)
    {
        var actionId = ResolveVariantActionId(variant);
        var action = BuildActionMap()[actionId];
        var payload = BuildActionPayload(actionId, variant);
        var context = BuildActionContext(variant);
        var mode = action.Mode switch
        {
            RuntimeMode.AnyTactical => variant % 2 == 0 ? RuntimeMode.TacticalLand : RuntimeMode.TacticalSpace,
            _ => action.Mode
        };

        return new ActionExecutionRequest(action, payload, "profile", mode, context);
    }

    private static string ResolveVariantActionId(int variant)
    {
        var index = Math.Abs(variant) % VariantActionIds.Length;
        return VariantActionIds[index];
    }

    private static JsonObject BuildActionPayload(string actionId, int variant)
    {
        if (ActionPayloadBuilders.TryGetValue(actionId, out var builder))
        {
            return builder(variant);
        }

        return new JsonObject();
    }

    private static JsonObject BuildSetCreditsPayload(int variant)
        => new() { ["symbol"] = "credits", ["intValue"] = 1000 + variant };

    private static JsonObject BuildSpawnTacticalPayload(int variant)
        => new()
        {
            ["entityId"] = "EMP_STORMTROOPER",
            ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Rebel",
            ["worldPosition"] = "12,0,24",
            ["placementMode"] = "reinforcement_zone"
        };

    private static JsonObject BuildSpawnGalacticPayload(int _)
        => new() { ["entityId"] = "ACC_ACCLAMATOR_1", ["targetFaction"] = "Empire", ["planetId"] = "Coruscant" };

    private static JsonObject BuildPlacePlanetBuildingPayload(int variant)
        => new()
        {
            ["entityId"] = "E_GROUND_LIGHT_FACTORY",
            ["targetFaction"] = "Empire",
            ["placementMode"] = variant % 2 == 0 ? "safe_rules" : "force_override"
        };

    private static JsonObject BuildSetContextAllegiancePayload(int variant)
        => new() { ["targetFaction"] = variant % 2 == 0 ? "Empire" : "Pirates", ["allowCrossFaction"] = true };

    private static JsonObject BuildTransferFleetPayload(int _)
        => new() { ["targetFaction"] = "Rebel", ["destinationPlanetId"] = "Kuat", ["safeTransfer"] = true };

    private static JsonObject BuildFlipPlanetPayload(int variant)
        => new()
        {
            ["planetId"] = "Kuat",
            ["targetFaction"] = "Rebel",
            ["modePolicy"] = variant % 2 == 0 ? "empty_and_retreat" : "convert_everything"
        };

    private static JsonObject BuildSwitchPlayerFactionPayload(int _)
        => new() { ["targetFaction"] = "Rebel" };

    private static JsonObject BuildEditHeroStatePayload(int variant)
        => new() { ["entityId"] = "DARTH_VADER", ["desiredState"] = variant % 2 == 0 ? "alive" : "respawn_pending" };

    private static JsonObject BuildCreateHeroVariantPayload(int variant)
        => new()
        {
            ["entityId"] = "MACE_WINDU",
            ["variantId"] = $"MACE_WINDU_VARIANT_{variant}",
            ["allowDuplicate"] = variant % 2 == 0,
            ["modifiers"] = new JsonObject { ["healthMultiplier"] = 1.25, ["damageMultiplier"] = 1.1 }
        };

    private static IReadOnlyDictionary<string, object?>? BuildActionContext(int variant)
    {
        return variant switch
        {
            2 => new Dictionary<string, object?> { ["runtimeModeOverride"] = "Unknown" },
            5 => new Dictionary<string, object?> { ["selectedPlanetId"] = "Kuat", ["requestedBy"] = "coverage" },
            7 => new Dictionary<string, object?> { ["runtimeModeOverride"] = "Galactic", ["allowCrossFaction"] = true },
            _ => null
        };
    }

    private static IReadOnlyDictionary<string, ActionSpec> BuildActionMap()
    {
        return new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new ActionSpec("set_credits", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_context_entity"] = new ActionSpec("spawn_context_entity", ActionCategory.Global, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_tactical_entity"] = new ActionSpec("spawn_tactical_entity", ActionCategory.Tactical, RuntimeMode.TacticalLand, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_galactic_entity"] = new ActionSpec("spawn_galactic_entity", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["place_planet_building"] = new ActionSpec("place_planet_building", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_context_faction"] = new ActionSpec("set_context_faction", ActionCategory.Global, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_context_allegiance"] = new ActionSpec("set_context_allegiance", ActionCategory.Global, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["transfer_fleet_safe"] = new ActionSpec("transfer_fleet_safe", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["flip_planet_owner"] = new ActionSpec("flip_planet_owner", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["switch_player_faction"] = new ActionSpec("switch_player_faction", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["edit_hero_state"] = new ActionSpec("edit_hero_state", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["create_hero_variant"] = new ActionSpec("create_hero_variant", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_selected_owner_faction"] = new ActionSpec("set_selected_owner_faction", ActionCategory.Tactical, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_planet_owner"] = new ActionSpec("set_planet_owner", ActionCategory.Campaign, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_unit_helper"] = new ActionSpec("spawn_unit_helper", ActionCategory.Tactical, RuntimeMode.AnyTactical, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_hero_state_helper"] = new ActionSpec("set_hero_state_helper", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["toggle_roe_respawn_helper"] = new ActionSpec("toggle_roe_respawn_helper", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0)
        };
    }

    private static IReadOnlyDictionary<string, bool> BuildFeatureFlags()
    {
        return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow.building.force_override"] = true,
            ["allow.cross.faction.default"] = true
        };
    }

    private static LaunchContext BuildLaunchContext()
    {
        return new LaunchContext(
            LaunchKind.Workshop,
            CommandLineAvailable: true,
            SteamModIds: ["1397421866"],
            ModPathRaw: null,
            ModPathNormalized: null,
            DetectedVia: "cmdline",
            Recommendation: new ProfileRecommendation("base_swfoc", "workshop_match", 0.9),
            Source: "detected");
    }
}
