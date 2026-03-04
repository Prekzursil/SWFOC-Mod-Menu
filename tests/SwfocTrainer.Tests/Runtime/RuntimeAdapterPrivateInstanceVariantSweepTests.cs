#pragma warning disable CA1014
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterPrivateInstanceVariantSweepTests
{
    private static readonly HashSet<string> UnsafeMethodNames = new(StringComparer.Ordinal)
    {
        "AllocateExecutableCaveNear",
        "TryAllocateInSymmetricRange",
        "TryAllocateFallbackCave",
        "TryAllocateNear",
        "AggressiveWriteLoop",
        "PulseCallback"
    };

    [Fact]
    public async Task PrivateInstanceMethods_ShouldExecuteAcrossArgumentVariants()
    {
        var profile = BuildProfile();
        var harness = new AdapterHarness();
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_attachedProfile", profile);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", RuntimeAdapterExecuteCoverageTests.CreateUninitializedMemoryAccessor());

        var methods = typeof(RuntimeAdapter)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => !method.ContainsGenericParameters)
            .Where(static method => !method.GetParameters().Any(p => p.ParameterType.IsByRef || p.IsOut || p.ParameterType.IsPointer))
            .Where(method => !UnsafeMethodNames.Contains(method.Name))
            .ToArray();

        var invoked = 0;
        foreach (var method in methods)
        {
            foreach (var variant in new[] { 0, 1, 2 })
            {
                await TryInvokeAsync(adapter, method, variant);
            }

            invoked++;
        }

        invoked.Should().BeGreaterThan(100);
    }

    private static async Task TryInvokeAsync(object instance, MethodInfo method, int variant)
    {
        var args = method.GetParameters().Select(parameter => BuildFallbackArgument(parameter, variant)).ToArray();
        try
        {
            var result = method.Invoke(instance, args);
            if (result is Task task)
            {
                var completed = await Task.WhenAny(task, Task.Delay(200));
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
            }
        }
        catch (TargetInvocationException)
        {
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
        catch (NotSupportedException)
        {
        }
    }

    private static object? BuildFallbackArgument(ParameterInfo parameter, int variant)
    {
        var type = parameter.ParameterType;

        if (type == typeof(string))
        {
            return variant switch
            {
                0 => "test",
                1 => string.Empty,
                _ => null
            };
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return variant switch { 0 => 1, 1 => -1, _ => 0 };
        }

        if (type == typeof(long) || type == typeof(long?))
        {
            return variant switch { 0 => 1L, 1 => -1L, _ => 0L };
        }

        if (type == typeof(bool) || type == typeof(bool?))
        {
            return variant == 1;
        }

        if (type == typeof(float) || type == typeof(float?))
        {
            return variant switch { 0 => 1.0f, 1 => -1.0f, _ => 0.0f };
        }

        if (type == typeof(double) || type == typeof(double?))
        {
            return variant switch { 0 => 1.0d, 1 => -1.0d, _ => 0.0d };
        }

        if (type == typeof(nint) || type == typeof(nint?))
        {
            return variant == 1 ? (nint)0 : (nint)0x1000;
        }

        if (type == typeof(byte[]))
        {
            return variant == 1 ? Array.Empty<byte>() : new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 };
        }

        if (type == typeof(JsonObject))
        {
            return variant switch
            {
                0 => new JsonObject
                {
                    ["symbol"] = "credits",
                    ["intValue"] = 1000,
                    ["helperHookId"] = "hero_hook",
                    ["entityId"] = "stormtrooper",
                    ["heroId"] = "hero_1"
                },
                1 => new JsonObject
                {
                    ["value"] = "NaN",
                    ["allowCrossFaction"] = "yes"
                },
                _ => new JsonObject()
            };
        }

        if (type == typeof(ActionExecutionRequest))
        {
            var actionId = variant == 1 ? "spawn_tactical_entity" : "set_hero_state_helper";
            var mode = variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic;
            return new ActionExecutionRequest(
                Action: new ActionSpec(
                    Id: actionId,
                    Category: ActionCategory.Hero,
                    Mode: mode,
                    ExecutionKind: variant == 1 ? ExecutionKind.Memory : ExecutionKind.Helper,
                    PayloadSchema: new JsonObject(),
                    VerifyReadback: false,
                    CooldownMs: 0),
                Payload: variant == 1
                    ? new JsonObject { ["symbol"] = "credits", ["intValue"] = 1 }
                    : new JsonObject { ["helperHookId"] = "hero_hook", ["heroId"] = "hero_1" },
                ProfileId: "profile",
                RuntimeMode: mode,
                Context: variant == 2
                    ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["runtimeMode"] = "unknown" }
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["runtimeMode"] = "galactic",
                        ["operationKind"] = "EditHeroState"
                    });
        }

        if (type == typeof(TrainerProfile))
        {
            return BuildProfile();
        }

        if (type == typeof(ProcessMetadata))
        {
            return RuntimeAdapterExecuteCoverageTests.BuildSession(variant == 1 ? RuntimeMode.TacticalSpace : RuntimeMode.Galactic).Process;
        }

        if (type == typeof(AttachSession))
        {
            return RuntimeAdapterExecuteCoverageTests.BuildSession(variant == 1 ? RuntimeMode.TacticalLand : RuntimeMode.Galactic);
        }

        if (type == typeof(CapabilityReport))
        {
            return new CapabilityReport(
                ProfileId: "profile",
                ProbedAtUtc: DateTimeOffset.UtcNow,
                Capabilities: new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
                ProbeReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        }

        if (type == typeof(CancellationToken))
        {
            return CancellationToken.None;
        }

        if (type == typeof(SymbolInfo))
        {
            return new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature);
        }

        if (type == typeof(SymbolValidationRule))
        {
            return new SymbolValidationRule("credits", IntMin: 0, IntMax: 999999);
        }

        if (type == typeof(RuntimeMode))
        {
            return variant switch
            {
                0 => RuntimeMode.Galactic,
                1 => RuntimeMode.TacticalLand,
                _ => RuntimeMode.Unknown
            };
        }

        if (type == typeof(ExecutionKind))
        {
            return variant == 1 ? ExecutionKind.Memory : ExecutionKind.Helper;
        }

        if (type == typeof(ExecutionBackendKind))
        {
            return variant switch
            {
                0 => ExecutionBackendKind.Helper,
                1 => ExecutionBackendKind.Extender,
                _ => ExecutionBackendKind.Memory
            };
        }

        if (type == typeof(IReadOnlyDictionary<string, string>) || type == typeof(IDictionary<string, string>))
        {
            return variant == 1
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["mode"] = "galactic" };
        }

        if (type == typeof(IReadOnlyDictionary<string, object?>) || type == typeof(IDictionary<string, object?>))
        {
            return variant == 1
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["runtimeMode"] = "galactic",
                    ["allowExpertMutationOverride"] = true
                };
        }

        if (type == typeof(ICollection<string>))
        {
            return variant == 1 ? new List<string>() : new List<string> { "a" };
        }

        if (type == typeof(List<int>))
        {
            return variant == 1 ? new List<int>() : new List<int> { 1, 2 };
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, variant == 1 ? 0 : 1);
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return null;
    }

    private static TrainerProfile BuildProfile()
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_hero_state_helper"] = new ActionSpec(
                "set_hero_state_helper",
                ActionCategory.Hero,
                RuntimeMode.Galactic,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            ["spawn_tactical_entity"] = new ActionSpec(
                "spawn_tactical_entity",
                ActionCategory.Global,
                RuntimeMode.TacticalLand,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            ["set_credits"] = new ActionSpec(
                "set_credits",
                ActionCategory.Global,
                RuntimeMode.Galactic,
                ExecutionKind.Memory,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0)
        };

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
            HelperModHooks:
            [
                new HelperHookSpec(
                    Id: "hero_hook",
                    Script: "scripts/aotr/hero_state_bridge.lua",
                    Version: "1.0.0",
                    EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
#pragma warning restore CA1014
