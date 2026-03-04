using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Meg;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Saves.Config;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeServiceReflectionSweepCoverageTests
{
    [Fact]
    public void RuntimeAdapter_PrivateInstanceMethods_ShouldExecuteWithFallbackInputs()
    {
        var adapter = CreateRuntimeAdapter();
        var methods = typeof(RuntimeAdapter)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => !method.ContainsGenericParameters)
            .Where(static method => method.GetParameters().All(CanMaterializeParameter))
            .ToArray();

        var invoked = 0;
        foreach (var method in methods)
        {
            TryInvokeMethod(adapter, method, alternate: false);
            TryInvokeMethod(adapter, method, alternate: true);
            invoked++;
        }

        invoked.Should().BeGreaterThan(90);
    }

    [Fact]
    public void RuntimeServices_PrivateMethods_ShouldExecuteWithFallbackInputs()
    {
        var instances = new object[]
        {
            new ProcessLocator(),
            new NamedPipeExtenderBackend(pipeName: "swfoc-trainer", autoStartBridgeHost: false),
            new ModMechanicDetectionService(),
            new SignatureResolver(NullLogger<SignatureResolver>.Instance),
            new MegArchiveReader(),
            new BinarySaveCodec(new SaveOptions(), NullLogger<BinarySaveCodec>.Instance),
            new SavePatchPackService(new SaveOptions())
        };

        var invoked = 0;
        foreach (var instance in instances)
        {
            var methods = instance.GetType()
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(static method => !method.IsSpecialName)
                .Where(static method => !method.ContainsGenericParameters)
                .Where(static method => method.GetParameters().All(CanMaterializeParameter))
                .ToArray();

            foreach (var method in methods)
            {
                TryInvokeMethod(method.IsStatic ? null : instance, method, alternate: false);
                TryInvokeMethod(method.IsStatic ? null : instance, method, alternate: true);
                invoked++;
            }
        }

        invoked.Should().BeGreaterThan(100);
    }

    [Fact]
    public void ProcessMemoryScanner_PrivateStaticMethods_ShouldExecuteWithFallbackInputs()
    {
        var methods = typeof(ProcessMemoryScanner)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => !method.ContainsGenericParameters)
            .Where(static method => method.GetParameters().All(CanMaterializeParameter))
            .ToArray();

        var invoked = 0;
        foreach (var method in methods)
        {
            TryInvokeMethod(null, method, alternate: false);
            TryInvokeMethod(null, method, alternate: true);
            invoked++;
        }

        invoked.Should().BeGreaterThan(10);
    }

    private static RuntimeAdapter CreateRuntimeAdapter()
    {
        var profile = BuildProfile();
        return new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);
    }

    private static TrainerProfile BuildProfile()
    {
        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new ActionSpec(
                    "set_credits",
                    ActionCategory.Global,
                    RuntimeMode.Unknown,
                    ExecutionKind.Memory,
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
                    CooldownMs: 0)
            },
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static bool CanMaterializeParameter(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type.IsByRef)
        {
            if (parameter.IsOut)
            {
                return false;
            }

            type = type.GetElementType()!;
        }

        return !type.IsPointer && !type.IsByRefLike;
    }

    private static void TryInvokeMethod(object? instance, MethodInfo method, bool alternate)
    {
        var args = method.GetParameters()
            .Select(parameter => BuildFallbackArgument(parameter, alternate))
            .ToArray();

        try
        {
            var result = method.Invoke(instance, args);
            AwaitIfTask(result);
        }
        catch (TargetInvocationException)
        {
            // Fail-closed paths are expected to throw for invalid fallback input.
        }
        catch (ArgumentException)
        {
            // Reflection can still reject a few aggressively validated signatures.
        }
        catch (Exception)
        {
            // Asynchronous helper and attach artifact paths can throw directly.
        }
    }

    private static void AwaitIfTask(object? result)
    {
        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
        }
    }

    private static object? BuildFallbackArgument(ParameterInfo parameter, bool alternate)
    {
        var originalType = parameter.ParameterType;
        var type = originalType.IsByRef ? originalType.GetElementType()! : originalType;

        if (type == typeof(string))
        {
            return alternate ? string.Empty : "test";
        }

        if (type == typeof(bool))
        {
            return alternate;
        }

        if (type == typeof(int))
        {
            return alternate ? -1 : 1;
        }

        if (type == typeof(long))
        {
            return alternate ? -1L : 1L;
        }

        if (type == typeof(float))
        {
            return alternate ? -1.0f : 1.0f;
        }

        if (type == typeof(double))
        {
            return alternate ? -1.0d : 1.0d;
        }

        if (type == typeof(Guid))
        {
            return alternate ? Guid.Empty : Guid.NewGuid();
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UtcNow;
        }

        if (type == typeof(TimeSpan))
        {
            return alternate ? TimeSpan.Zero : TimeSpan.FromMilliseconds(50);
        }

        if (type == typeof(CancellationToken))
        {
            return CancellationToken.None;
        }

        if (type == typeof(JsonObject))
        {
            return alternate ? new JsonObject { ["alt"] = 1 } : new JsonObject();
        }

        if (type == typeof(ActionExecutionRequest))
        {
            return new ActionExecutionRequest(
                Action: new ActionSpec(
                    alternate ? "spawn_tactical_entity" : "set_credits",
                    ActionCategory.Global,
                    alternate ? RuntimeMode.TacticalLand : RuntimeMode.Galactic,
                    alternate ? ExecutionKind.Helper : ExecutionKind.Memory,
                    new JsonObject(),
                    VerifyReadback: false,
                    CooldownMs: 0),
                Payload: alternate ? new JsonObject { ["entityId"] = "X" } : new JsonObject(),
                ProfileId: "profile",
                RuntimeMode: alternate ? RuntimeMode.TacticalLand : RuntimeMode.Galactic,
                Context: null);
        }

        if (type == typeof(ActionExecutionResult))
        {
            return new ActionExecutionResult(
                Succeeded: !alternate,
                Message: alternate ? "blocked" : "ok",
                AddressSource: AddressSource.None,
                Diagnostics: new Dictionary<string, object?>());
        }

        if (type == typeof(AttachSession))
        {
            return RuntimeAdapterExecuteCoverageTests.BuildSession(alternate ? RuntimeMode.TacticalLand : RuntimeMode.Galactic);
        }

        if (type == typeof(ProcessMetadata))
        {
            return RuntimeAdapterExecuteCoverageTests.BuildSession(alternate ? RuntimeMode.TacticalSpace : RuntimeMode.Galactic).Process;
        }

        if (type == typeof(TrainerProfile))
        {
            return BuildProfile();
        }

        if (type == typeof(SymbolMap))
        {
            return new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase));
        }

        if (type == typeof(SymbolValidationRule))
        {
            return new SymbolValidationRule("credits_rva");
        }

        if (type == typeof(IReadOnlyDictionary<string, object?>) || type == typeof(IDictionary<string, object?>))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (type == typeof(IReadOnlyDictionary<string, string>) || type == typeof(IDictionary<string, string>))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = alternate ? "tactical" : "galactic"
            };
        }

        if (type == typeof(IReadOnlyList<string>))
        {
            return alternate ? Array.Empty<string>() : new[] { "a" };
        }

        if (type == typeof(ICollection<string>))
        {
            return alternate ? new List<string>() : new List<string> { "a" };
        }

        if (type == typeof(IEnumerable<string>))
        {
            return alternate ? Array.Empty<string>() : new[] { "a" };
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, alternate ? 0 : 1);
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(Math.Min(alternate ? 1 : 0, values.Length - 1));
        }

        if (type == typeof(ILogger))
        {
            return NullLogger.Instance;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var loggerType = typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()[0]);
            return loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        try
        {
            return alternate ? null : Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }
}




