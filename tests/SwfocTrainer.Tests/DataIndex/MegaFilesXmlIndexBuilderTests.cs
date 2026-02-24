using FluentAssertions;
using SwfocTrainer.DataIndex.Services;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

public sealed class MegaFilesXmlIndexBuilderTests
{
    [Fact]
    public void Build_ShouldParseMegaFilesInDeclaredLoadOrder()
    {
        const string xml = """
<MegaFiles>
  <MegaFile Name="Config.meg" Enabled="true" />
  <MegaFile Filename="AOTR.meg" Enabled="false" />
  <File Name="Campaign.meg" />
</MegaFiles>
""";

        var builder = new MegaFilesXmlIndexBuilder();

        var index = builder.Build(xml);

        index.Diagnostics.Should().BeEmpty();
        index.Files.Should().HaveCount(3);
        index.Files.Select(x => x.FileName).Should().ContainInOrder("Config.meg", "AOTR.meg", "Campaign.meg");
        index.Files.Select(x => x.LoadOrder).Should().ContainInOrder(0, 1, 2);
        index.Files[0].Enabled.Should().BeTrue();
        index.Files[1].Enabled.Should().BeFalse();
        index.Files[2].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Build_ShouldEmitDiagnosticForUnnamedMegaFileNodes()
    {
        const string xml = """
<MegaFiles>
  <MegaFile Enabled="true" />
  <MegaFile Name="Config.meg" />
</MegaFiles>
""";

        var builder = new MegaFilesXmlIndexBuilder();

        var index = builder.Build(xml);

        index.Files.Should().ContainSingle(x => x.FileName == "Config.meg");
        index.Diagnostics.Should().ContainSingle(x => x.Contains("no filename", StringComparison.OrdinalIgnoreCase));
    }
}
