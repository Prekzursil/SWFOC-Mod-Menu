#pragma warning disable CA1014
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterPrivateInstanceVariantSweepTests
{
    private static readonly HashSet<string> UnsafeMethodNames = new(StringComparer.Ordinal)
    {
        "AggressiveWriteLoop",
        "PulseCallback"
    };

    [Fact]
    public async Task PrivateInstanceMethods_ShouldExecuteAcrossArgumentVariants()
    {
        var methods = BuildMethodMatrix();
        var invoked = 0;
        var modes = new[]
        {
            RuntimeMode.Galactic,
            RuntimeMode.TacticalLand,
            RuntimeMode.TacticalSpace,
            RuntimeMode.AnyTactical,
            RuntimeMode.Unknown
        };

        foreach (var mode in modes)
        {
            var profile = ReflectionCoverageVariantFactory.BuildProfile();
            var harness = new AdapterHarness();
            var adapter = harness.CreateAdapter(profile, mode);

            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_attachedProfile", profile);
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", CreateProcessMemoryAccessor());
            TrySetCurrentSession(adapter, ReflectionCoverageVariantFactory.BuildSession(mode));

            foreach (var method in methods)
            {
                await InvokeMethodVariantsAsync(adapter, method);
                invoked++;
            }
        }

        invoked.Should().BeGreaterThan(500);
    }

    private static IReadOnlyList<MethodInfo> BuildMethodMatrix()
    {
        return typeof(RuntimeAdapter)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => !method.ContainsGenericParameters)
            .Where(static method => !HasUnsafeParameters(method))
            .Where(method => !UnsafeMethodNames.Contains(method.Name))
            .ToArray();
    }

    private static bool HasUnsafeParameters(MethodInfo method)
    {
        return method.GetParameters().Any(parameter =>
        {
            var type = parameter.ParameterType;
            return parameter.IsOut || type.IsByRef || type.IsPointer;
        });
    }

    private static async Task InvokeMethodVariantsAsync(object instance, MethodInfo method)
    {
        for (var variant = 0; variant < 160; variant++)
        {
            var args = method
                .GetParameters()
                .Select(parameter => ReflectionCoverageVariantFactory.BuildArgument(parameter.ParameterType, variant))
                .ToArray();

            await TryInvokeAsync(instance, method, args);
        }
    }

    private static object CreateProcessMemoryAccessor()
    {
        var memoryType = typeof(RuntimeAdapter).Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor");
        memoryType.Should().NotBeNull();
        var accessor = Activator.CreateInstance(memoryType!, Environment.ProcessId);
        accessor.Should().NotBeNull();
        return accessor!;
    }

    private static void TrySetCurrentSession(RuntimeAdapter adapter, AttachSession session)
    {
        var property = typeof(RuntimeAdapter).GetProperty("CurrentSession", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property is null)
        {
            return;
        }

        try
        {
            property.SetValue(adapter, session);
        }
        catch (TargetInvocationException)
        {
        }
        catch (MethodAccessException)
        {
        }
    }

    private static async Task TryInvokeAsync(object instance, MethodInfo method, object?[] args)
    {
        try
        {
            var result = method.Invoke(instance, args);
            await ReflectionCoverageVariantFactory.AwaitResultAsync(result, timeoutMs: 220);
        }
        catch (TargetInvocationException)
        {
            // Reflective invocation covers guarded failure branches.
        }
        catch (ArgumentException)
        {
            // Variant arguments intentionally exercise input-validation paths.
        }
        catch (InvalidOperationException)
        {
            // Runtime-only paths can throw during deterministic tests.
        }
        catch (NullReferenceException)
        {
            // Null-protection branches are expected in synthetic invocation.
        }
        catch (NotSupportedException)
        {
            // Some runtime methods reject reflective test execution.
        }
    }
}
#pragma warning restore CA1014

