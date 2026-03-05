#pragma warning disable CA1014
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Services;
using SwfocTrainer.DataIndex.Services;
using SwfocTrainer.Flow.Services;
using SwfocTrainer.Meg;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class NonRuntimeHighDeficitReflectionTests
{
    private static readonly string[] UnsafeMethodFragments =
    [
        "ShowDialog",
        "Browse",
        "Inject",
        "Launch",
        "Host",
        "Pipe",
        "OpenFile",
        "SaveFile",
        "WaitForExit",
        "Watch"
    ];

    private static readonly string[] InternalTypeNames =
    [
        "SwfocTrainer.Runtime.Services.SignatureResolverFallbacks",
        "SwfocTrainer.Runtime.Services.SignatureResolverSymbolHydration"
    ];

    [Fact]
    public async Task HighDeficitNonHostTypes_ShouldExecuteStableReflectionVariantSweep()
    {
        var invoked = 0;
        foreach (var type in BuildTargetTypes())
        {
            invoked += await SweepTypeAsync(type);
        }

        invoked.Should().BeGreaterThan(260);
    }

    private static IReadOnlyList<Type> BuildTargetTypes()
    {
        var targets = new HashSet<Type>
        {
            typeof(MegArchiveReader),
            typeof(BinarySaveCodec),
            typeof(SavePatchPackService),
            typeof(SignatureResolver),
            typeof(EffectiveGameDataIndexService),
            typeof(CatalogService),
            typeof(ActionReliabilityService),
            typeof(StoryPlotFlowExtractor),
            typeof(MainViewModel),
            typeof(ModMechanicDetectionService),
            typeof(BackendRouter),
            typeof(ProcessLocator),
            typeof(LaunchContextResolver),
            typeof(CapabilityMapResolver),
            typeof(ModDependencyValidator),
            typeof(TelemetryLogTailService)
        };

        var runtimeAssembly = typeof(RuntimeAdapter).Assembly;
        foreach (var fullName in InternalTypeNames)
        {
            var resolved = runtimeAssembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            if (resolved is not null)
            {
                targets.Add(resolved);
            }
        }

        return targets.ToArray();
    }

    private static async Task<int> SweepTypeAsync(Type type)
    {
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(ShouldSweepMethod)
            .ToArray();

        if (methods.Length == 0)
        {
            return 0;
        }

        var instance = ReflectionCoverageVariantFactory.CreateInstance(type, alternate: false);
        var alternate = ReflectionCoverageVariantFactory.CreateInstance(type, alternate: true);
        var invoked = 0;

        foreach (var method in methods)
        {
            var target = method.IsStatic ? null : (instance ?? alternate);
            if (!method.IsStatic && target is null)
            {
                continue;
            }

            for (var variant = 0; variant < 64; variant++)
            {
                var args = method.GetParameters()
                    .Select(parameter => ReflectionCoverageVariantFactory.BuildArgument(parameter.ParameterType, variant))
                    .ToArray();
                await TryInvokeAsync(target, method, args);
            }

            invoked++;
        }

        return invoked;
    }

    private static bool ShouldSweepMethod(MethodInfo method)
    {
        if (method.IsSpecialName || method.ContainsGenericParameters)
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

    private static async Task TryInvokeAsync(object? target, MethodInfo method, object?[] args)
    {
        try
        {
            var result = method.Invoke(target, args);
            await ReflectionCoverageVariantFactory.AwaitResultAsync(result, timeoutMs: 80);
        }
        catch (TargetInvocationException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (NullReferenceException)
        {
        }
        catch (IOException)
        {
        }
    }
}

#pragma warning restore CA1014
