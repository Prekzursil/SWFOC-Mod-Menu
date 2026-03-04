#pragma warning disable CA1014
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
            [typeof(Guid)] = BuildGuidPrimitive,
            [typeof(DateTimeOffset)] = variant => variant == 1 ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow,
            [typeof(DateTime)] = variant => variant == 1 ? DateTime.MinValue : DateTime.UtcNow,
            [typeof(TimeSpan)] = variant => variant == 1 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(25),
            [typeof(CancellationToken)] = _ => CancellationToken.None,
            [typeof(byte[])] = variant => variant == 1 ? Array.Empty<byte>() : new byte[] { 1, 2, 3, 4 }
        };

    private static readonly IReadOnlyDictionary<Type, Func<int, object?>> DomainBuilders =
        new Dictionary<Type, Func<int, object?>>
        {
            [typeof(ActionExecutionRequest)] = ReflectionCoverageActionFactory.BuildActionExecutionRequest,
            [typeof(ActionExecutionResult)] = variant => new ActionExecutionResult(variant != 1, variant == 1 ? "blocked" : "ok", AddressSource.None, new Dictionary<string, object?>()),
            [typeof(TrainerProfile)] = _ => BuildProfile(),
            [typeof(ProcessMetadata)] = variant => BuildSession((variant % 3) switch { 1 => RuntimeMode.TacticalLand, 2 => RuntimeMode.TacticalSpace, _ => RuntimeMode.Galactic }).Process,
            [typeof(AttachSession)] = variant => BuildSession((variant % 3) switch { 1 => RuntimeMode.TacticalLand, 2 => RuntimeMode.TacticalSpace, _ => RuntimeMode.Galactic }),
            [typeof(SymbolMap)] = _ => new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            [typeof(SymbolInfo)] = _ => new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature),
            [typeof(SymbolValidationRule)] = _ => new SymbolValidationRule("credits", IntMin: 0, IntMax: 1_000_000),
            [typeof(MainViewModelDependencies)] = _ => CreateNullDependencies(),
            [typeof(SaveOptions)] = _ => new SaveOptions(),
            [typeof(LaunchContext)] = _ => ReflectionCoverageActionFactory.BuildLaunchContext(),
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
            [typeof(IServiceProvider)] = _ => ReflectionCoverageRuntimeFactory.BuildServiceProvider()
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
            Actions: ReflectionCoverageActionFactory.BuildActionMap(),
            FeatureFlags: ReflectionCoverageActionFactory.BuildFeatureFlags(),
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
            return ReflectionCoverageRuntimeFactory.CreateRuntimeAdapterInstance(alternate);
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

    private static object BuildGuidPrimitive(int variant)
    {
        return variant == 0 ? new Guid("11111111-1111-1111-1111-111111111111") : Guid.Empty;
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

}
#pragma warning restore CA1014
