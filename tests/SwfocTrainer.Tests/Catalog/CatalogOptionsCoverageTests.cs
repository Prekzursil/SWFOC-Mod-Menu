using FluentAssertions;
using SwfocTrainer.Catalog.Config;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

public sealed class CatalogOptionsCoverageTests
{
    [Fact]
    public void CatalogOptions_ShouldExposeStableDefaults()
    {
        var options = new CatalogOptions();

        options.CatalogRootPath.Should().NotBeNullOrWhiteSpace();
        options.CatalogRootPath.Should().Contain("profiles");
        options.MaxParsedXmlFiles.Should().Be(4096);
    }
}
