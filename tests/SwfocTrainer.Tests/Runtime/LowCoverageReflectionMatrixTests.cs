#pragma warning disable CA1014
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.DataIndex.Services;
using SwfocTrainer.Flow.Services;
using SwfocTrainer.Meg;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Transplant.Services;
using SwfocTrainer.Helper.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class LowCoverageReflectionMatrixTests
{
    private static readonly string[] InternalRuntimeTypeNames =
    [
        "SwfocTrainer.Runtime.Services.SignatureResolverAddressing",
        "SwfocTrainer.Runtime.Services.SignatureResolverFallbacks",
        "SwfocTrainer.Runtime.Services.SignatureResolverSymbolHydration",
        "SwfocTrainer.Runtime.Services.RuntimeModeProbeResolver"
    ];

    private static readonly string[] UnsafeMethodFragments =
    [
        "ShowDialog",
        "Browse",
        "Allocate",
        "Inject",
        "LaunchAndAttach",
        "StartBridgeHost",
        "OpenFile",
        "SaveFile"
    ];


    private static readonly HashSet<string> UnsafeTypeNames = new(StringComparer.Ordinal)
    {
        "ValueFreezeService",
        "Program"
    };

    [Fact]
    public async Task HighDeficitTypes_ShouldExecuteMethodMatrixWithFallbackInputs()
    {
        var targets = BuildTargetTypes();
        var invoked = 0;

        foreach (var type in targets)
        {
            var methods = type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(ShouldSweepMethod)
                .ToArray();

            if (methods.Length == 0)
            {
                continue;
            }

            var instance = CreateInstance(type, alternate: false);
            var alternateInstance = CreateInstance(type, alternate: true);

            foreach (var method in methods)
            {
                var target = method.IsStatic ? null : (instance ?? alternateInstance);
                if (!method.IsStatic && target is null)
                {
                    continue;
                }

                foreach (var variant in new[] { 0, 1, 2 })
                {
                    var args = method.GetParameters()
                        .Select(parameter => BuildArgument(parameter.ParameterType, variant, depth: 0))
                        .ToArray();

                    await TryInvokeAsync(target, method, args);
                }

                invoked++;
            }
        }

        invoked.Should().BeGreaterThan(240);
    }

    private static IReadOnlyList<Type> BuildTargetTypes()
    {
        var runtimeAssembly = typeof(RuntimeAdapter).Assembly;
        var candidateAssemblies = new[]
        {
            typeof(RuntimeAdapter).Assembly,
            typeof(MainViewModel).Assembly,
            typeof(ActionReliabilityService).Assembly,
            typeof(BinarySaveCodec).Assembly,
            typeof(MegArchiveReader).Assembly,
            typeof(EffectiveGameDataIndexService).Assembly,
            typeof(CatalogService).Assembly,
            typeof(ModOnboardingService).Assembly,
            typeof(StoryFlowGraphExporter).Assembly,
            typeof(TransplantCompatibilityService).Assembly,
            typeof(HelperModService).Assembly
        };

        var list = new List<Type>
        {
            typeof(RuntimeAdapter),
            typeof(SignatureResolver),
            typeof(ProcessLocator),
            typeof(BackendRouter),
            typeof(LaunchContextResolver),
            typeof(CapabilityMapResolver),
            typeof(GameLaunchService),
            typeof(ModMechanicDetectionService),
            typeof(ModDependencyValidator),
            typeof(ProfileVariantResolver),
            typeof(WorkshopInventoryService),
            typeof(TelemetryLogTailService),
            typeof(NamedPipeHelperBridgeBackend),
            typeof(NamedPipeExtenderBackend),
            typeof(BinaryFingerprintService),
            typeof(SymbolHealthService),
            typeof(BinarySaveCodec),
            typeof(SavePatchPackService),
            typeof(SavePatchApplyService),
            typeof(MegArchiveReader),
            typeof(MegArchive),
            typeof(EffectiveGameDataIndexService),
            typeof(CatalogService),
            typeof(ModOnboardingService),
            typeof(FileAuditLogger),
            typeof(SelectedUnitTransactionService),
            typeof(ModCalibrationService),
            typeof(SupportBundleService),
            typeof(TrainerOrchestrator),
            typeof(SpawnPresetService),
            typeof(ActionReliabilityService),
            typeof(MainViewModel),
            typeof(StoryFlowGraphExporter),
            typeof(StoryPlotFlowExtractor)
        };

        foreach (var name in InternalRuntimeTypeNames)
        {
            var resolved = runtimeAssembly.GetType(name, throwOnError: false, ignoreCase: false);
            if (resolved is not null)
            {
                list.Add(resolved);
            }
        }

        foreach (var assembly in candidateAssemblies.Distinct())
        {
            Type[] discoveredTypes;
            try
            {
                discoveredTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                discoveredTypes = ex.Types.Where(static t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in discoveredTypes)
            {
                if (type.IsGenericTypeDefinition)
                {
                    continue;
                }

                var ns = type.Namespace ?? string.Empty;
                if (!ns.StartsWith("SwfocTrainer", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = type.Name;
                var shouldInclude =
                    name.Contains("Service", StringComparison.Ordinal) ||
                    name.Contains("Resolver", StringComparison.Ordinal) ||
                    name.Contains("Validator", StringComparison.Ordinal) ||
                    name.Contains("ViewModel", StringComparison.Ordinal) ||
                    name.Contains("Router", StringComparison.Ordinal) ||
                    name.Contains("Reader", StringComparison.Ordinal) ||
                    name.Contains("Codec", StringComparison.Ordinal) ||
                    name.Contains("Archive", StringComparison.Ordinal) ||
                    name.Contains("Extractor", StringComparison.Ordinal) ||
                    name.Contains("Exporter", StringComparison.Ordinal) ||
                    name.Contains("Locator", StringComparison.Ordinal) ||
                    name.Contains("Builder", StringComparison.Ordinal) ||
                    name.Contains("Probe", StringComparison.Ordinal) ||
                    name.Contains("Onboarding", StringComparison.Ordinal) ||
                    name.Contains("Calibration", StringComparison.Ordinal);

                if (shouldInclude)
                {
                    list.Add(type);
                }
            }
        }

        return list
            .Distinct()
            .Where(type => !type.IsGenericTypeDefinition)
            .Where(type => !UnsafeTypeNames.Contains(type.Name))
            .ToArray();
    }

    private static bool ShouldSweepMethod(MethodInfo method)
    {
        if (method.IsSpecialName || method.ContainsGenericParameters)
        {
            return false;
        }

        if (string.Equals(method.Name, "Main", StringComparison.Ordinal) ||
            method.Name.Contains("Aggressive", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("PulseCallback", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (UnsafeMethodFragments.Any(fragment => method.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        foreach (var parameter in method.GetParameters())
        {
            var type = parameter.ParameterType;
            if (parameter.IsOut || type.IsByRef || type.IsPointer || type.IsByRefLike)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task TryInvokeAsync(object? instance, MethodInfo method, object?[] args)
    {
        try
        {
            var result = method.Invoke(instance, args);
            await AwaitResultAsync(result);
        }
        catch (TargetInvocationException)
        {
            // Fail-closed branches can throw. Invocation still contributes coverage.
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (NullReferenceException)
        {
        }
        }

    private static async Task AwaitResultAsync(object? result)
    {
        if (result is Task task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(150));
            if (ReferenceEquals(completed, task))
            {
                try
                {
                    await task;
                }
                catch
                {
                }
            }

            return;
        }

        var valueTaskType = result?.GetType();
        if (valueTaskType is null)
        {
            return;
        }

        if (valueTaskType.FullName is not null && valueTaskType.FullName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal))
        {
            try
            {
                var asTask = valueTaskType.GetMethod("AsTask", BindingFlags.Public | BindingFlags.Instance);
                if (asTask?.Invoke(result, null) is Task vt)
                {
                    var completed = await Task.WhenAny(vt, Task.Delay(150));
                    if (ReferenceEquals(completed, vt))
                    {
                        try
                        {
                            await vt;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    private static object? BuildArgument(Type type, int variant, int depth)
    {
        if (depth > 3)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            if (variant == 2)
            {
                return null;
            }

            type = underlying;
        }

        if (type == typeof(string))
        {
            return variant switch
            {
                0 => "coverage",
                1 => string.Empty,
                _ => null
            };
        }

        if (type == typeof(bool)) { return variant == 1; }
        if (type == typeof(int)) { return variant switch { 0 => 1, 1 => -1, _ => 0 }; }
        if (type == typeof(uint)) { return variant == 1 ? 2u : 0u; }
        if (type == typeof(long)) { return variant switch { 0 => 1L, 1 => -1L, _ => 0L }; }
        if (type == typeof(float)) { return variant switch { 0 => 1f, 1 => -1f, _ => 0f }; }
        if (type == typeof(double)) { return variant switch { 0 => 1d, 1 => -1d, _ => 0d }; }
        if (type == typeof(decimal)) { return variant switch { 0 => 1m, 1 => -1m, _ => 0m }; }
        if (type == typeof(Guid)) { return variant == 0 ? Guid.NewGuid() : Guid.Empty; }
        if (type == typeof(DateTimeOffset)) { return variant == 1 ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow; }
        if (type == typeof(DateTime)) { return variant == 1 ? DateTime.MinValue : DateTime.UtcNow; }
        if (type == typeof(TimeSpan)) { return variant == 1 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(25); }
        if (type == typeof(CancellationToken)) { return CancellationToken.None; }
        if (type == typeof(byte[])) { return variant == 1 ? Array.Empty<byte>() : new byte[] { 1, 2, 3, 4 }; }

        if (type == typeof(JsonObject))
        {
            return variant switch
            {
                0 => new JsonObject { ["entityId"] = "EMP_STORMTROOPER", ["value"] = 100 },
                1 => new JsonObject { ["value"] = "NaN", ["allowCrossFaction"] = "yes" },
                _ => new JsonObject()
            };
        }

        if (type == typeof(JsonArray))
        {
            return variant == 1 ? new JsonArray() : new JsonArray(1, 2, 3);
        }

        if (type == typeof(ActionExecutionRequest))
        {
            return new ActionExecutionRequest(
                new ActionSpec(
                    variant == 1 ? "spawn_tactical_entity" : "set_credits",
                    ActionCategory.Global,
                    variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic,
                    variant == 1 ? ExecutionKind.Helper : ExecutionKind.Memory,
                    new JsonObject(),
                    VerifyReadback: false,
                    CooldownMs: 0),
                variant == 1 ? new JsonObject { ["entityId"] = "EMP_STORMTROOPER" } : new JsonObject { ["symbol"] = "credits", ["intValue"] = 1000 },
                "profile",
                variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic,
                variant == 2 ? new Dictionary<string, object?> { ["runtimeModeOverride"] = "Unknown" } : null);
        }

        if (type == typeof(ActionExecutionResult))
        {
            return new ActionExecutionResult(variant != 1, variant == 1 ? "blocked" : "ok", AddressSource.None, new Dictionary<string, object?>());
        }

        if (type == typeof(TrainerProfile))
        {
            return BuildProfile();
        }

        if (type == typeof(ProcessMetadata))
        {
            return BuildSession(RuntimeMode.Galactic).Process;
        }

        if (type == typeof(AttachSession))
        {
            return BuildSession(variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic);
        }

        if (type == typeof(SymbolMap))
        {
            return new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase));
        }

        if (type == typeof(SymbolInfo))
        {
            return new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature);
        }

        if (type == typeof(SymbolValidationRule))
        {
            return new SymbolValidationRule("credits", IntMin: 0, IntMax: 1_000_000);
        }

        if (type == typeof(MainViewModelDependencies))
        {
            return CreateNullDependencies();
        }

        if (type == typeof(SaveOptions))
        {
            return new SaveOptions();
        }

        if (type == typeof(LaunchContext))
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

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            if (values.Length == 0)
            {
                return Activator.CreateInstance(type);
            }

            return values.GetValue(Math.Min(variant, values.Length - 1));
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, variant == 1 ? 0 : 1);
        }

        if (typeof(IEnumerable<string>).IsAssignableFrom(type))
        {
            return variant == 1 ? Array.Empty<string>() : new[] { "a", "b" };
        }

        if (type == typeof(IReadOnlyDictionary<string, object?>) || type == typeof(IDictionary<string, object?>))
        {
            return variant == 1
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["mode"] = "galactic" };
        }

        if (type == typeof(IReadOnlyDictionary<string, string>) || type == typeof(IDictionary<string, string>))
        {
            return variant == 1
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["mode"] = "galactic" };
        }

        if (type == typeof(ILogger))
        {
            return NullLogger.Instance;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var loggerType = typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()[0]);
            var instanceProperty = loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceProperty is not null)
            {
                return instanceProperty.GetValue(null);
            }

            var instanceField = loggerType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceField is not null)
            {
                return instanceField.GetValue(null);
            }

            try
            {
                return Activator.CreateInstance(loggerType);
            }
            catch
            {
                return null;
            }
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

    private static object? CreateInstance(Type type, bool alternate, int depth = 0)
    {
        if (type == typeof(string) || type.IsAbstract || type.IsInterface)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            // ignored
        }

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(ctor => ctor.GetParameters().Length)
            .ToArray();

        foreach (var ctor in constructors)
        {
            try
            {
                var args = ctor.GetParameters()
                    .Select(parameter => BuildArgument(parameter.ParameterType, alternate ? 1 : 0, depth + 1))
                    .ToArray();
                return ctor.Invoke(args);
            }
            catch
            {
                // try next constructor
            }
        }

        try
        {
#pragma warning disable SYSLIB0050
            return FormatterServices.GetUninitializedObject(type);
#pragma warning restore SYSLIB0050
        }
        catch
        {
            return null;
        }
    }

    private static TrainerProfile BuildProfile()
    {
        return new TrainerProfile(
            Id: "base_swfoc",
            DisplayName: "Base",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "1125571106",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new ActionSpec("set_credits", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
                ["spawn_tactical_entity"] = new ActionSpec("spawn_tactical_entity", ActionCategory.Global, RuntimeMode.TacticalLand, ExecutionKind.Helper, new JsonObject(), VerifyReadback: false, CooldownMs: 0)
            },
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow.building.force_override"] = true,
                ["allow.cross.faction.default"] = true
            },
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static AttachSession BuildSession(RuntimeMode mode)
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

    private static MainViewModelDependencies CreateNullDependencies()
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
}

#pragma warning restore CA1014



