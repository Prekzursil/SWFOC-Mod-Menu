using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class PrivateRecordCoverageTests
{
    [Fact]
    public void RuntimeAdapter_PrivateNestedRecordConstructors_ShouldBeInstantiable()
    {
        var runtimeType = typeof(RuntimeAdapter);
        var nested = runtimeType.GetNestedTypes(BindingFlags.NonPublic)
            .Where(type => type.Name is "CodePatchActionContext" or "CreditsHookInstallContext" or "WriteAttemptResult`1")
            .ToArray();

        nested.Should().NotBeEmpty();

        foreach (var type in nested)
        {
            var concreteType = type.IsGenericTypeDefinition ? type.MakeGenericType(typeof(int)) : type;
            var instance = CreateWithDefaults(concreteType);
            instance.Should().NotBeNull($"nested type {concreteType.FullName} should construct with default args");
        }
    }

    [Fact]
    public void SavePatchHelper_SelectorApplyAttempt_StaticPaths_ShouldBeInvokable()
    {
        var savesAssembly = typeof(SwfocTrainer.Saves.Services.SavePatchApplyService).Assembly;
        var selectorType = savesAssembly.GetType(
            "SwfocTrainer.Saves.Services.SavePatchApplyServiceHelper+SelectorApplyAttempt",
            throwOnError: true,
            ignoreCase: false);

        selectorType.Should().NotBeNull();

        var notAttempted = selectorType!
            .GetProperty("NotAttempted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null);
        var applied = selectorType
            .GetProperty("AppliedAttempt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null);
        var mismatch = selectorType
            .GetMethod("Mismatch", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { new InvalidOperationException("boom") });

        notAttempted.Should().NotBeNull();
        applied.Should().NotBeNull();
        mismatch.Should().NotBeNull();
    }

    private static object CreateWithDefaults(Type type)
    {
        var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .First();

        var args = ctor.GetParameters().Select(parameter => CreateDefault(parameter.ParameterType)).ToArray();
        return ctor.Invoke(args);
    }

    private static object? CreateDefault(Type type)
    {
        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type == typeof(CancellationToken))
        {
            return CancellationToken.None;
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
}
