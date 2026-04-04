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

    [Fact]
    public void GetEnabledFilesInLoadOrder_ShouldReturnOnlyEnabledRowsSortedByLoadOrder()
    {
        var index = new SwfocTrainer.DataIndex.Models.MegaFilesIndex(
            Files: new[]
            {
                new SwfocTrainer.DataIndex.Models.MegaFileEntry("B.meg", 2, true, new Dictionary<string, string>()),
                new SwfocTrainer.DataIndex.Models.MegaFileEntry("A.meg", 1, true, new Dictionary<string, string>()),
                new SwfocTrainer.DataIndex.Models.MegaFileEntry("Disabled.meg", 0, false, new Dictionary<string, string>())
            },
            Diagnostics: Array.Empty<string>());

        var enabled = index.GetEnabledFilesInLoadOrder();

        enabled.Select(x => x.FileName).Should().ContainInOrder("A.meg", "B.meg");
    }

    [Fact]
    public void Build_ShouldReturnEmpty_WhenContentIsWhitespace()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build("   ");
        index.Should().Be(SwfocTrainer.DataIndex.Models.MegaFilesIndex.Empty);
    }

    [Fact]
    public void Build_ShouldThrow_WhenContentIsNull()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var act = () => builder.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ShouldReturnDiagnostic_WhenXmlIsInvalid()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build("<bad xml><<<");
        index.Files.Should().BeEmpty();
        index.Diagnostics.Should().ContainSingle(x => x.Contains("Invalid MegaFiles XML"));
    }

    [Fact]
    public void Build_ShouldIgnoreNonMegaFileElements()
    {
        const string xml = """
<MegaFiles>
  <Other Name="NotAMeg.meg" />
  <MegaFile Name="Config.meg" />
</MegaFiles>
""";
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build(xml);
        index.Files.Should().ContainSingle(x => x.FileName == "Config.meg");
    }

    [Fact]
    public void Build_ShouldReadPathAttribute()
    {
        const string xml = """
<MegaFiles>
  <MegaFile Path="PathBased.meg" />
</MegaFiles>
""";
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build(xml);
        index.Files.Should().ContainSingle(x => x.FileName == "PathBased.meg");
    }

    [Fact]
    public void Build_ShouldReadFileAttribute()
    {
        const string xml = """
<MegaFiles>
  <MegaFile File="FileBased.meg" />
</MegaFiles>
""";
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build(xml);
        index.Files.Should().ContainSingle(x => x.FileName == "FileBased.meg");
    }

    [Fact]
    public void Build_ShouldDisableEntry_WhenIsEnabledIsFalse()
    {
        const string xml = """
<MegaFiles>
  <MegaFile Name="Disabled.meg" IsEnabled="false" />
</MegaFiles>
""";
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build(xml);
        index.Files.Should().ContainSingle(x => x.FileName == "Disabled.meg" && !x.Enabled);
    }

    [Fact]
    public void Build_ShouldPreserveAllAttributes()
    {
        const string xml = """
<MegaFiles>
  <MegaFile Name="Config.meg" Custom="value" />
</MegaFiles>
""";
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build(xml);
        index.Files[0].Attributes.Should().ContainKey("Custom");
        index.Files[0].Attributes["Custom"].Should().Be("value");
    }

    [Fact]
    public void Build_ShouldDefaultToEnabled_WhenNoEnabledAttribute()
    {
        const string xml = """
<MegaFiles>
  <MegaFile Name="Config.meg" />
</MegaFiles>
""";
        var builder = new MegaFilesXmlIndexBuilder();
        var index = builder.Build(xml);
        index.Files[0].Enabled.Should().BeTrue();
    }
}
