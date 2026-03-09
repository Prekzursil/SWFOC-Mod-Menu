using FluentAssertions;
using SwfocTrainer.Helper.Config;
using Xunit;

namespace SwfocTrainer.Tests.Helper;

public sealed class HelperModOptionsCoverageTests
{
    [Fact]
    public void HelperModOptions_ShouldExposeExpectedDefaultPaths()
    {
        var options = new HelperModOptions();

        options.SourceRoot.Should().Contain(Path.Combine("profiles", "helper"));
        options.InstallRoot.Should().Contain("SwfocTrainer");
        options.InstallRoot.Should().Contain("helper_mod");
    }
}
