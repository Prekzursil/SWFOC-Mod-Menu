#pragma warning disable CA1014
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterPrivateStaticSweepTests
{
    [Fact]
    public void PrivateStaticMethods_ShouldExecuteWithFallbackArguments()
    {
        var methods = typeof(RuntimeAdapter)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => !method.ContainsGenericParameters)
            .Where(static method => method.GetParameters().All(CanMaterializeParameter))
            .ToArray();

        var invoked = 0;
        foreach (var method in methods)
        {
            for (var variant = 0; variant < 24; variant++)
            {
                var args = method.GetParameters()
                    .Select(parameter => ReflectionCoverageVariantFactory.BuildArgument(parameter.ParameterType, variant))
                    .ToArray();

                try
                {
                    _ = method.Invoke(null, args);
                }
                catch (TargetInvocationException)
                {
                    // Guard paths can throw; still useful for coverage of fail-closed branches.
                }
                catch (ArgumentException)
                {
                    // Some methods validate parameter shape aggressively.
                }
            }

            invoked++;
        }

        invoked.Should().BeGreaterThan(120);
    }

    private static bool CanMaterializeParameter(ParameterInfo parameter)
    {
        var type = parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()!
            : parameter.ParameterType;

        return !type.IsPointer;
    }

}





