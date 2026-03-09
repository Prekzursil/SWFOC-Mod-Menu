using FluentAssertions;
using SwfocTrainer.Saves.Config;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SaveOptionsCoverageTests
{
    [Fact]
    public void SaveOptions_ShouldExposeStableDefaults()
    {
        var options = new SaveOptions();

        options.SchemaRootPath.Should().NotBeNullOrWhiteSpace();
        options.SchemaRootPath.Should().Contain("profiles");
        options.DefaultSaveRootPath.Should().NotBeNullOrWhiteSpace();
        options.DefaultSaveRootPath.Should().Contain("Petroglyph");
    }
}
