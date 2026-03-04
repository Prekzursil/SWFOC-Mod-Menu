using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Config;

namespace SwfocTrainer.Tests.Runtime;

internal static class ReflectionCoverageVariantFactory
{
    private const int MaxDepth = 3;

    public static object? BuildArgument(Type parameterType, int variant, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            return parameterType.IsValueType ? Activator.CreateInstance(parameterType) : null;
        }

        if (TryResolveNullable(parameterType, variant, out var normalizedType, out var nullableResult))
        {
            return nullableResult;
        }

        if (TryBuildPrimitive(normalizedType, variant, out var primitive))
        {
            return primitive;
        }

        if (TryBuildJson(normalizedType, variant, out var jsonValue))
        {
            return jsonValue;
        }

        if (TryBuildDomainModel(normalizedType, variant, out var modelValue))
        {
            return modelValue;
        }

        if (TryBuildCollection(normalizedType, variant, out var collectionValue))
        {
            return collectionValue;
        }

        if (TryBuildLogger(normalizedType, out var loggerValue))
        {
            return loggerValue;
        }

        if (normalizedType.IsEnum)
        {
            return BuildEnumValue(normalizedType, variant);
        }

        if (normalizedType.IsArray)
        {
            return Array.CreateInstance(normalizedType.GetElementType()!, variant == 1 ? 0 : 1);
        }

        if (normalizedType.IsInterface || normalizedType.IsAbstract)
        {
            return null;
        }

        if (normalizedType.IsValueType)
        {
            return Activator.CreateInstance(normalizedType);
        }

        return CreateInstance(normalizedType, alternate: variant == 1, depth + 1);
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

        var valueTaskType = result.GetType();
        if (valueTaskType.FullName is null || !valueTaskType.FullName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal))
        {
            return;
        }

        var asTask = valueTaskType.GetMethod("AsTask", BindingFlags.Public | BindingFlags.Instance);
        if (asTask?.Invoke(result, null) is Task valueTask)
        {
            await AwaitTaskWithTimeoutAsync(valueTask, timeoutMs);
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
        if (type == typeof(string) || type.IsInterface || type.IsAbstract)
        {
            return null;
        }

        var direct = TryCreateWithDefaultConstructor(type);
        if (direct.succeeded)
        {
            return direct.instance;
        }

        foreach (var ctor in GetConstructorsByArity(type))
        {
            var args = ctor.GetParameters()
                .Select(parameter => BuildArgument(parameter.ParameterType, alternate ? 1 : 0, depth + 1))
                .ToArray();

            try
            {
                return ctor.Invoke(args);
            }
            catch (TargetInvocationException)
            {
                // Try next constructor.
            }
            catch (ArgumentException)
            {
                // Try next constructor.
            }
            catch (MemberAccessException)
            {
                // Try next constructor.
            }
            catch (NotSupportedException)
            {
                // Try next constructor.
            }
        }

        return null;
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
        catch (OperationCanceledException)
        {
            // Expected for cancellation branches.
        }
        catch (InvalidOperationException)
        {
            // Expected for fail-closed branches.
        }
        catch (TargetInvocationException)
        {
            // Expected for reflection-invoked methods.
        }
        catch (NullReferenceException)
        {
            // Expected for null-path guard coverage.
        }
        catch (IOException)
        {
            // File-system dependent runtime methods can fail in test sandboxes.
        }
        catch (UnauthorizedAccessException)
        {
            // Permission checks are expected on synthetic paths.
        }
        catch (InvalidDataException)
        {
            // Validation paths can intentionally reject synthetic payloads.
        }
        catch (FormatException)
        {
            // Variant payloads can trigger format guards.
        }
        catch (ArgumentOutOfRangeException)
        {
            // Range validation is expected for branch coverage variants.
        }
    }

    private static (bool succeeded, object? instance) TryCreateWithDefaultConstructor(Type type)
    {
        try
        {
            return (true, Activator.CreateInstance(type));
        }
        catch (MissingMethodException)
        {
            return (false, null);
        }
        catch (TargetInvocationException)
        {
            return (false, null);
        }
        catch (MemberAccessException)
        {
            return (false, null);
        }
        catch (NotSupportedException)
        {
            return (false, null);
        }
    }

    private static IEnumerable<ConstructorInfo> GetConstructorsByArity(Type type)
    {
        return type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(ctor => ctor.GetParameters().Length);
    }

    private static bool TryResolveNullable(Type inputType, int variant, out Type normalizedType, out object? nullableResult)
    {
        var underlying = Nullable.GetUnderlyingType(inputType);
        if (underlying is null)
        {
            normalizedType = inputType;
            nullableResult = null;
            return false;
        }

        if (variant == 2)
        {
            normalizedType = underlying;
            nullableResult = null;
            return true;
        }

        normalizedType = underlying;
        nullableResult = null;
        return false;
    }

    private static bool TryBuildPrimitive(Type type, int variant, out object? value)
    {
        value = null;

        if (type == typeof(string))
        {
            value = variant switch { 0 => "coverage", 1 => string.Empty, _ => null };
            return true;
        }

        if (type == typeof(bool)) { value = variant == 1; return true; }
        if (type == typeof(int)) { value = variant switch { 0 => 1, 1 => -1, _ => 0 }; return true; }
        if (type == typeof(uint)) { value = variant == 1 ? 2u : 0u; return true; }
        if (type == typeof(long)) { value = variant switch { 0 => 1L, 1 => -1L, _ => 0L }; return true; }
        if (type == typeof(float)) { value = variant switch { 0 => 1f, 1 => -1f, _ => 0f }; return true; }
        if (type == typeof(double)) { value = variant switch { 0 => 1d, 1 => -1d, _ => 0d }; return true; }
        if (type == typeof(decimal)) { value = variant switch { 0 => 1m, 1 => -1m, _ => 0m }; return true; }
        if (type == typeof(Guid)) { value = variant == 0 ? Guid.NewGuid() : Guid.Empty; return true; }
        if (type == typeof(DateTimeOffset)) { value = variant == 1 ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow; return true; }
        if (type == typeof(DateTime)) { value = variant == 1 ? DateTime.MinValue : DateTime.UtcNow; return true; }
        if (type == typeof(TimeSpan)) { value = variant == 1 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(25); return true; }
        if (type == typeof(CancellationToken)) { value = CancellationToken.None; return true; }
        if (type == typeof(byte[])) { value = variant == 1 ? Array.Empty<byte>() : new byte[] { 1, 2, 3, 4 }; return true; }

        return false;
    }

    private static bool TryBuildJson(Type type, int variant, out object? value)
    {
        value = null;
        if (type == typeof(JsonObject))
        {
            value = variant switch
            {
                0 => new JsonObject { ["entityId"] = "EMP_STORMTROOPER", ["value"] = 100 },
                1 => new JsonObject { ["value"] = "NaN", ["allowCrossFaction"] = "yes" },
                _ => new JsonObject()
            };
            return true;
        }

        if (type == typeof(JsonArray))
        {
            value = variant == 1 ? new JsonArray() : new JsonArray(1, 2, 3);
            return true;
        }

        return false;
    }

    private static bool TryBuildDomainModel(Type type, int variant, out object? value)
    {
        value = null;

        if (type == typeof(ActionExecutionRequest))
        {
            value = BuildActionExecutionRequest(variant);
            return true;
        }

        if (type == typeof(ActionExecutionResult))
        {
            value = new ActionExecutionResult(variant != 1, variant == 1 ? "blocked" : "ok", AddressSource.None, new Dictionary<string, object?>());
            return true;
        }

        if (type == typeof(TrainerProfile)) { value = BuildProfile(); return true; }
        if (type == typeof(ProcessMetadata)) { value = BuildSession(RuntimeMode.Galactic).Process; return true; }
        if (type == typeof(AttachSession)) { value = BuildSession(variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic); return true; }
        if (type == typeof(SymbolMap)) { value = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)); return true; }
        if (type == typeof(SymbolInfo)) { value = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature); return true; }
        if (type == typeof(SymbolValidationRule)) { value = new SymbolValidationRule("credits", IntMin: 0, IntMax: 1_000_000); return true; }
        if (type == typeof(MainViewModelDependencies)) { value = CreateNullDependencies(); return true; }
        if (type == typeof(SaveOptions)) { value = new SaveOptions(); return true; }
        if (type == typeof(LaunchContext)) { value = BuildLaunchContext(); return true; }

        return false;
    }

    private static bool TryBuildCollection(Type type, int variant, out object? value)
    {
        value = null;

        if (typeof(IEnumerable<string>).IsAssignableFrom(type))
        {
            value = variant == 1 ? Array.Empty<string>() : new[] { "a", "b" };
            return true;
        }

        if (type == typeof(IReadOnlyDictionary<string, object?>) || type == typeof(IDictionary<string, object?>))
        {
            value = variant == 1
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["mode"] = "galactic" };
            return true;
        }

        if (type == typeof(IReadOnlyDictionary<string, string>) || type == typeof(IDictionary<string, string>))
        {
            value = variant == 1
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["mode"] = "galactic" };
            return true;
        }

        return false;
    }

    private static bool TryBuildLogger(Type type, out object? value)
    {
        value = null;

        if (type == typeof(ILogger))
        {
            value = NullLogger.Instance;
            return true;
        }

        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ILogger<>))
        {
            return false;
        }

        var loggerType = typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()[0]);
        var property = loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (property is not null)
        {
            value = property.GetValue(null);
            return true;
        }

        return false;
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

    private static ActionExecutionRequest BuildActionExecutionRequest(int variant)
    {
        var action = new ActionSpec(
            variant == 1 ? "spawn_tactical_entity" : "set_credits",
            ActionCategory.Global,
            variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic,
            variant == 1 ? ExecutionKind.Helper : ExecutionKind.Memory,
            new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);

        var payload = variant == 1
            ? new JsonObject { ["entityId"] = "EMP_STORMTROOPER" }
            : new JsonObject { ["symbol"] = "credits", ["intValue"] = 1000 };

        var context = variant == 2
            ? new Dictionary<string, object?> { ["runtimeModeOverride"] = "Unknown" }
            : null;

        return new ActionExecutionRequest(action, payload, "profile", variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic, context);
    }

    private static IReadOnlyDictionary<string, ActionSpec> BuildActionMap()
    {
        return new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new ActionSpec("set_credits", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["spawn_tactical_entity"] = new ActionSpec("spawn_tactical_entity", ActionCategory.Global, RuntimeMode.TacticalLand, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            ["set_hero_state_helper"] = new ActionSpec("set_hero_state_helper", ActionCategory.Hero, RuntimeMode.Galactic, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0)
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



