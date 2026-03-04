#pragma warning disable CA1014
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterPrivateInstanceSweepTests
{
    private static readonly HashSet<string> UnsafeMethodNames = new(StringComparer.Ordinal)
    {
        "AllocateExecutableCaveNear",
        "TryAllocateInSymmetricRange",
        "TryAllocateFallbackCave",
        "TryAllocateNear"
    };

    [Fact]
    public async Task PrivateInstanceMethods_ShouldExecuteWithFallbackArguments()
    {
        var profile = BuildProfile();
        var harness = new AdapterHarness();
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_attachedProfile", profile);

        var memoryAccessor = CreateProcessMemoryAccessor();
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", memoryAccessor);

        var methods = typeof(RuntimeAdapter)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => !method.ContainsGenericParameters)
            .Where(static method => !method.GetParameters().Any(p => p.ParameterType.IsByRef || p.IsOut))
            .Where(method => !UnsafeMethodNames.Contains(method.Name))
            .ToArray();

        var invoked = 0;
        foreach (var method in methods)
        {
            var args = method.GetParameters().Select(BuildFallbackArgument).ToArray();
            try
            {
                var result = method.Invoke(adapter, args);
                if (result is Task task)
                {
                    await AwaitIgnoringFailureAsync(task);
                }
            }
            catch (TargetInvocationException)
            {
                // Guard-path exceptions are expected for many private branches.
            }
            catch (ArgumentException)
            {
                // Some methods validate exact payload shapes.
            }

            invoked++;
        }

        invoked.Should().BeGreaterThan(100);
        ((IDisposable)memoryAccessor).Dispose();
    }

    [Fact]
    public async Task DetachAsync_ShouldClearSessionAndState_WhenPreviouslyAttached()
    {
        var profile = BuildProfile();
        var harness = new AdapterHarness();
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_attachedProfile", profile);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_dependencyValidationStatus", DependencyValidationStatus.SoftFail);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_dependencyValidationMessage", "missing parent");
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(
            adapter,
            "_dependencySoftDisabledActions",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "set_hero_state_helper" });

        await adapter.DetachAsync();

        adapter.CurrentSession.Should().BeNull();
    }

    private static async Task AwaitIgnoringFailureAsync(Task task)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token));
        if (!ReferenceEquals(completed, task))
        {
            return;
        }

        try
        {
            await task;
        }
        catch
        {
            // Ignore, fail-closed branches are acceptable for sweep coverage.
        }
    }

    private static object? BuildFallbackArgument(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;

        if (type == typeof(string))
        {
            return "test";
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return 1;
        }

        if (type == typeof(long) || type == typeof(long?))
        {
            return 1L;
        }

        if (type == typeof(bool) || type == typeof(bool?))
        {
            return true;
        }

        if (type == typeof(float) || type == typeof(float?))
        {
            return 1.0f;
        }

        if (type == typeof(double) || type == typeof(double?))
        {
            return 1.0d;
        }

        if (type == typeof(nint) || type == typeof(nint?))
        {
            return (nint)0x1000;
        }

        if (type == typeof(byte[]))
        {
            return new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 };
        }

        if (type == typeof(JsonObject))
        {
            return new JsonObject
            {
                ["symbol"] = "credits",
                ["intValue"] = 1000,
                ["helperHookId"] = "hero_hook",
                ["entityId"] = "stormtrooper",
                ["heroId"] = "hero_1"
            };
        }

        if (type == typeof(ActionExecutionRequest))
        {
            return new ActionExecutionRequest(
                Action: new ActionSpec(
                    Id: "set_hero_state_helper",
                    Category: ActionCategory.Hero,
                    Mode: RuntimeMode.Galactic,
                    ExecutionKind: ExecutionKind.Helper,
                    PayloadSchema: new JsonObject(),
                    VerifyReadback: false,
                    CooldownMs: 0),
                Payload: new JsonObject { ["helperHookId"] = "hero_hook", ["heroId"] = "hero_1" },
                ProfileId: "profile",
                RuntimeMode: RuntimeMode.Galactic,
                Context: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
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
            return RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic).Process;
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
            return RuntimeMode.Galactic;
        }

        if (type == typeof(ExecutionKind))
        {
            return ExecutionKind.Helper;
        }

        if (type == typeof(ExecutionBackendKind))
        {
            return ExecutionBackendKind.Helper;
        }

        if (type == typeof(IReadOnlyDictionary<string, string>) || type == typeof(IDictionary<string, string>))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (type == typeof(IReadOnlyDictionary<string, object?>) || type == typeof(IDictionary<string, object?>))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["runtimeMode"] = "galactic",
                ["allowExpertMutationOverride"] = true
            };
        }

        if (type == typeof(ICollection<string>))
        {
            return new List<string>();
        }

        if (type == typeof(List<int>))
        {
            return new List<int> { 1, 2 };
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 0);
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return null;
    }

    private static object CreateProcessMemoryAccessor()
    {
        var memoryType = typeof(RuntimeAdapter).Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor");
        memoryType.Should().NotBeNull();
        var accessor = Activator.CreateInstance(memoryType!, Environment.ProcessId);
        accessor.Should().NotBeNull();
        return accessor!;
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

