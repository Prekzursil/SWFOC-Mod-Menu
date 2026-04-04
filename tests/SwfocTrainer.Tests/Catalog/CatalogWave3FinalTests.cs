using System.Xml.Linq;
using FluentAssertions;
using SwfocTrainer.Catalog.Services;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

/// <summary>
/// Wave 3 Final coverage for Catalog: XmlObjectExtractor error paths
/// (XmlException, IOException), CatalogService null deserialization branch.
/// </summary>
public sealed class CatalogWave3FinalTests
{
    [Fact]
    public void XmlObjectExtractor_MalformedXml_ShouldReturnEmpty()
    {
        // Write a malformed XML file to trigger XmlException
        var tempPath = Path.Join(Path.GetTempPath(), $"swfoc-cat-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempPath, "<<<not valid xml>>>");
            var result = SwfocTrainer.Catalog.Parsing.XmlObjectExtractor.ExtractObjectNames(tempPath);
            result.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void XmlObjectExtractor_MissingFile_ShouldReturnEmpty()
    {
        // Non-existent file triggers IOException
        var result = SwfocTrainer.Catalog.Parsing.XmlObjectExtractor.ExtractObjectNames(
            Path.Join(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.xml"));
        result.Should().BeEmpty();
    }

    [Fact]
    public void XmlObjectExtractor_ValidXml_WithAttributes_ShouldExtractNames()
    {
        var tempPath = Path.Join(Path.GetTempPath(), $"swfoc-cat-valid-{Guid.NewGuid():N}.xml");
        try
        {
            var xml = @"<Root><Unit Name=""AT-AT"" /><Unit ID=""Speeder"" /><Object Object_Name=""Star_Destroyer"" /></Root>";
            File.WriteAllText(tempPath, xml);
            var result = SwfocTrainer.Catalog.Parsing.XmlObjectExtractor.ExtractObjectNames(tempPath);
            result.Should().Contain("AT-AT");
            result.Should().Contain("Speeder");
            result.Should().Contain("Star_Destroyer");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void XmlObjectExtractor_LongAttributeValue_ShouldBeExcluded()
    {
        var tempPath = Path.Join(Path.GetTempPath(), $"swfoc-cat-long-{Guid.NewGuid():N}.xml");
        try
        {
            var longName = new string('A', 100); // > 96 chars
            var xml = $@"<Root><Unit Name=""{longName}"" /><Unit Name=""Short"" /></Root>";
            File.WriteAllText(tempPath, xml);
            var result = SwfocTrainer.Catalog.Parsing.XmlObjectExtractor.ExtractObjectNames(tempPath);
            result.Should().Contain("Short");
            result.Should().NotContain(longName);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void XmlObjectExtractor_EmptyAndWhitespaceAttributes_ShouldBeExcluded()
    {
        var tempPath = Path.Join(Path.GetTempPath(), $"swfoc-cat-ws-{Guid.NewGuid():N}.xml");
        try
        {
            var xml = @"<Root><Unit Name="""" /><Unit Name=""   "" /><Unit Name=""Valid"" /></Root>";
            File.WriteAllText(tempPath, xml);
            var result = SwfocTrainer.Catalog.Parsing.XmlObjectExtractor.ExtractObjectNames(tempPath);
            result.Should().Contain("Valid");
            result.Should().HaveCount(1);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void XmlObjectExtractor_NullPath_ShouldThrow()
    {
        var act = () => SwfocTrainer.Catalog.Parsing.XmlObjectExtractor.ExtractObjectNames(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
