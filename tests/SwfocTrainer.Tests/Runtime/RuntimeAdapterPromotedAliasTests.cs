using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterPromotedAliasTests
{
    [Fact]
    public void ResolvePromotedAnchorAliases_InstantBuild_ShouldPreferCanonicalInjectionAnchor()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolvePromotedAnchorAliases",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("RuntimeAdapter should expose promoted alias resolution for parity checks.");
        var aliases = (string[]?)method!.Invoke(null, new object?[] { "toggle_instant_build_patch" });

        aliases.Should().NotBeNull();
        aliases.Should().Equal("instant_build_patch_injection", "instant_build_patch", "toggle_instant_build_patch");
    }
}
