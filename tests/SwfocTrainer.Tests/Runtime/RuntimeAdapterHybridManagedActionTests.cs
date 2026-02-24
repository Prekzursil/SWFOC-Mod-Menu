using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterHybridManagedActionTests
{
    [Fact]
    public void RuntimeAdapter_ShouldNotExposeLegacyHybridExecutionHelpers()
    {
        var resolveHybridMethod = typeof(RuntimeAdapter).GetMethod(
            "ResolveHybridManagedExecutionKind",
            BindingFlags.NonPublic | BindingFlags.Static);
        var shouldExecuteHybridMethod = typeof(RuntimeAdapter).GetMethod(
            "ShouldExecuteHybridManagedAction",
            BindingFlags.NonPublic | BindingFlags.Static);
        var executeHybridMethod = typeof(RuntimeAdapter).GetMethod(
            "ExecuteHybridManagedActionAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        resolveHybridMethod.Should().BeNull("promoted actions are extender-authoritative and should not map to managed execution kinds.");
        shouldExecuteHybridMethod.Should().BeNull("promoted actions should not execute through hybrid routing checks.");
        executeHybridMethod.Should().BeNull("promoted actions should not execute through legacy managed fallback.");
    }
}
