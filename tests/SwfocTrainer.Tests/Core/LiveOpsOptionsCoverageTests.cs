using FluentAssertions;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class LiveOpsOptionsCoverageTests
{
    [Fact]
    public void LiveOpsOptions_ShouldExposeStableDefaults()
    {
        var options = new LiveOpsOptions();

        options.PresetRootPath.Should().NotBeNullOrWhiteSpace();
        options.PresetRootPath.Should().Contain("profiles");
        options.PresetRootPath.Should().Contain("presets");
    }
}
