using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterApiShapeTests
{
    [Fact]
    public void RuntimeAdapter_ShouldUseOverloadsInsteadOfOptionalCancellationTokenParameters()
    {
        AssertHasNoTokenAndNonOptionalTokenOverloads(
            nameof(RuntimeAdapter.AttachAsync),
            HasAttachNoTokenSignature,
            HasAttachTokenSignature);
        AssertHasNoTokenAndNonOptionalTokenOverloads(
            nameof(RuntimeAdapter.ReadAsync),
            HasReadNoTokenSignature,
            HasReadTokenSignature);
        AssertHasNoTokenAndNonOptionalTokenOverloads(
            nameof(RuntimeAdapter.WriteAsync),
            HasWriteNoTokenSignature,
            HasWriteTokenSignature);
        AssertHasNoTokenAndNonOptionalTokenOverloads(
            nameof(RuntimeAdapter.ExecuteAsync),
            HasExecuteNoTokenSignature,
            HasExecuteTokenSignature);
        AssertHasNoTokenAndNonOptionalTokenOverloads(
            nameof(RuntimeAdapter.DetachAsync),
            HasDetachNoTokenSignature,
            HasDetachTokenSignature);
    }

    private static void AssertHasNoTokenAndNonOptionalTokenOverloads(
        string methodName,
        Func<MethodInfo, bool> noTokenOverloadMatcher,
        Func<MethodInfo, bool> tokenOverloadMatcher)
    {
        var runtimeAdapterType = typeof(RuntimeAdapter);
        var methods = runtimeAdapterType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == methodName)
            .ToArray();

        methods.Should().NotBeEmpty($"method '{methodName}' should exist.");

        var noTokenOverload = methods.SingleOrDefault(noTokenOverloadMatcher);
        noTokenOverload.Should().NotBeNull($"method '{methodName}' should expose an overload without CancellationToken.");

        var tokenOverload = methods.SingleOrDefault(tokenOverloadMatcher);
        tokenOverload.Should().NotBeNull($"method '{methodName}' should expose an overload with CancellationToken.");

        var tokenParameter = tokenOverload!.GetParameters().Last();
        tokenParameter.ParameterType.Should().Be(typeof(CancellationToken));
        tokenParameter.IsOptional.Should().BeFalse("optional CancellationToken parameters are flagged by SonarCSharp_S2360.");
        tokenParameter.HasDefaultValue.Should().BeFalse("the CancellationToken overload should require an explicit token.");
    }

    private static bool HasAttachNoTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 0 &&
               ParametersMatch(method.GetParameters(), [typeof(string)]);
    }

    private static bool HasAttachTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 0 &&
               ParametersMatch(method.GetParameters(), [typeof(string), typeof(CancellationToken)]);
    }

    private static bool HasReadNoTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 1 &&
               ParametersMatch(method.GetParameters(), [typeof(string)]);
    }

    private static bool HasReadTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 1 &&
               ParametersMatch(method.GetParameters(), [typeof(string), typeof(CancellationToken)]);
    }

    private static bool HasWriteNoTokenSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return method.GetGenericArguments().Length == 1 &&
               parameters.Length == 2 &&
               parameters[0].ParameterType == typeof(string) &&
               parameters[1].ParameterType.IsGenericParameter;
    }

    private static bool HasWriteTokenSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return method.GetGenericArguments().Length == 1 &&
               parameters.Length == 3 &&
               parameters[0].ParameterType == typeof(string) &&
               parameters[1].ParameterType.IsGenericParameter &&
               parameters[2].ParameterType == typeof(CancellationToken);
    }

    private static bool HasExecuteNoTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 0 &&
               ParametersMatch(method.GetParameters(), [typeof(ActionExecutionRequest)]);
    }

    private static bool HasExecuteTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 0 &&
               ParametersMatch(method.GetParameters(), [typeof(ActionExecutionRequest), typeof(CancellationToken)]);
    }

    private static bool HasDetachNoTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 0 &&
               ParametersMatch(method.GetParameters(), Type.EmptyTypes);
    }

    private static bool HasDetachTokenSignature(MethodInfo method)
    {
        return method.GetGenericArguments().Length == 0 &&
               ParametersMatch(method.GetParameters(), [typeof(CancellationToken)]);
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, Type[] expectedTypes)
    {
        if (parameters.Length != expectedTypes.Length)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].ParameterType != expectedTypes[index])
            {
                return false;
            }
        }

        return true;
    }
}
