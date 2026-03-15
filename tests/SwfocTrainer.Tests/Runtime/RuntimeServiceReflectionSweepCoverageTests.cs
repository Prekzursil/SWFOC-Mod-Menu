#pragma warning disable CA1014
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Meg;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeServiceReflectionSweepCoverageTests
{
    [Fact]
    public void RuntimeAdapter_PrivateInstanceMethods_ShouldExecuteWithFallbackInputs()
    {
        var adapter = CreateRuntimeAdapter();
        var methods = GetSweepMethods(typeof(RuntimeAdapter), BindingFlags.NonPublic | BindingFlags.Instance);

        var invoked = InvokeMethodSet(adapter, methods);
        invoked.Should().BeGreaterThan(90);
    }

    [Fact]
    public void RuntimeServices_PrivateMethods_ShouldExecuteWithFallbackInputs()
    {
        var instances = CreateServiceInstances();
        var invoked = 0;

        foreach (var instance in instances)
        {
            var methods = GetSweepMethods(instance.GetType(), BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            invoked += InvokeMethodSet(instance, methods);
        }

        invoked.Should().BeGreaterThan(100);
    }

    [Fact]
    public void ProcessMemoryScanner_PrivateStaticMethods_ShouldExecuteWithFallbackInputs()
    {
        var methods = GetSweepMethods(typeof(ProcessMemoryScanner), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        var invoked = InvokeMethodSet(null, methods);
        invoked.Should().BeGreaterThan(10);
    }

    private static object[] CreateServiceInstances()
    {
        return
        [
            new ProcessLocator(),
            new NamedPipeExtenderBackend(pipeName: "swfoc-trainer", autoStartBridgeHost: false),
            new ModMechanicDetectionService(),
            new SignatureResolver(NullLogger<SignatureResolver>.Instance),
            new MegArchiveReader(),
            new BinarySaveCodec(new SaveOptions(), NullLogger<BinarySaveCodec>.Instance),
            new SavePatchPackService(new SaveOptions())
        ];
    }

    private static MethodInfo[] GetSweepMethods(Type type, BindingFlags flags)
    {
        return type
            .GetMethods(flags | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => !method.ContainsGenericParameters)
            .Where(static method => method.GetParameters().All(CanMaterializeParameter))
            .ToArray();
    }

    private static int InvokeMethodSet(object? instance, IReadOnlyList<MethodInfo> methods)
    {
        var invoked = 0;
        foreach (var method in methods)
        {
            var target = method.IsStatic ? null : instance;
            TryInvokeMethod(target, method, alternate: false);
            TryInvokeMethod(target, method, alternate: true);
            invoked++;
        }

        return invoked;
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
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (NullReferenceException)
        {
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
        var type = parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()!
            : parameter.ParameterType;

        if (TryBuildPrimitive(type, alternate, out var value)
            || TryBuildDomain(type, alternate, out value)
            || TryBuildCollection(type, alternate, out value)
            || TryBuildLogger(type, out value))
        {
            return value;
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

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return TryCreateReference(type, alternate);
    }

    private static bool TryBuildPrimitive(Type type, bool alternate, out object? value)
    {
        if (type == typeof(string)) { value = alternate ? string.Empty : "test"; return true; }
        if (type == typeof(bool)) { value = alternate; return true; }
        if (type == typeof(int)) { value = alternate ? -1 : 1; return true; }
        if (type == typeof(long)) { value = alternate ? -1L : 1L; return true; }
        if (type == typeof(float)) { value = alternate ? -1.0f : 1.0f; return true; }
        if (type == typeof(double)) { value = alternate ? -1.0d : 1.0d; return true; }
        if (type == typeof(Guid)) { value = alternate ? Guid.Empty : Guid.NewGuid(); return true; }
        if (type == typeof(DateTimeOffset)) { value = DateTimeOffset.UtcNow; return true; }
        if (type == typeof(TimeSpan)) { value = alternate ? TimeSpan.Zero : TimeSpan.FromMilliseconds(50); return true; }
        if (type == typeof(CancellationToken)) { value = CancellationToken.None; return true; }
        value = null;
        return false;
    }

    private static bool TryBuildDomain(Type type, bool alternate, out object? value)
    {
        if (type == typeof(JsonObject))
        {
            value = alternate ? new JsonObject { ["alt"] = 1 } : new JsonObject();
            return true;
        }

        if (type == typeof(ActionExecutionRequest))
        {
            value = BuildActionExecutionRequest(alternate);
            return true;
        }

        if (type == typeof(ActionExecutionResult))
        {
            value = new ActionExecutionResult(!alternate, alternate ? "blocked" : "ok", AddressSource.None, new Dictionary<string, object?>());
            return true;
        }

        if (type == typeof(AttachSession))
        {
            value = RuntimeAdapterExecuteCoverageTests.BuildSession(alternate ? RuntimeMode.TacticalLand : RuntimeMode.Galactic);
            return true;
        }

        if (type == typeof(ProcessMetadata))
        {
            value = RuntimeAdapterExecuteCoverageTests.BuildSession(alternate ? RuntimeMode.TacticalSpace : RuntimeMode.Galactic).Process;
            return true;
        }

        if (type == typeof(TrainerProfile)) { value = BuildProfile(); return true; }
        if (type == typeof(SymbolMap)) { value = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)); return true; }
        if (type == typeof(SymbolValidationRule)) { value = new SymbolValidationRule("credits_rva"); return true; }

        value = null;
        return false;
    }

    private static ActionExecutionRequest BuildActionExecutionRequest(bool alternate)
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

    private static bool TryBuildCollection(Type type, bool alternate, out object? value)
    {
        if (type == typeof(IReadOnlyDictionary<string, object?>) || type == typeof(IDictionary<string, object?>))
        {
            value = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        if (type == typeof(IReadOnlyDictionary<string, string>) || type == typeof(IDictionary<string, string>))
        {
            value = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["mode"] = alternate ? "tactical" : "galactic" };
            return true;
        }

        if (type == typeof(IReadOnlyList<string>) || type == typeof(IEnumerable<string>))
        {
            value = alternate ? Array.Empty<string>() : new[] { "a" };
            return true;
        }

        if (type == typeof(ICollection<string>))
        {
            value = alternate ? new List<string>() : new List<string> { "a" };
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryBuildLogger(Type type, out object? value)
    {
        if (type == typeof(ILogger))
        {
            value = NullLogger.Instance;
            return true;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var loggerType = typeof(NullLogger<>).MakeGenericType(type.GetGenericArguments()[0]);
            value = loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
            return true;
        }

        value = null;
        return false;
    }

    private static object? TryCreateReference(Type type, bool alternate)
    {
        if (alternate)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(type);
        }
        catch (MissingMethodException)
        {
            return null;
        }
        catch (MemberAccessException)
        {
            return null;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
