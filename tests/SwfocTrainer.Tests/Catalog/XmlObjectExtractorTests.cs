using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Catalog.Services;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

public sealed class XmlObjectExtractorTests
{
    [Fact]
    public void ExtractObjectNames_ShouldCollectInterestingAttributes_AndDeduplicate()
    {
        var longValue = new string('A', 97);
        var xml = $$"""
            <Root>
              <Unit Name="AT_AT" />
              <Unit ID="AT_AT" />
              <Obj Id="TIE_FIGHTER" />
              <Obj Object_Name=" STAR_DESTROYER " />
              <Obj Type="SPACE_STATION" />
              <Obj Name="   " />
              <Obj Name="{{longValue}}" />
            </Root>
            """;

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, xml);
            var names = InvokeExtractor(path);

            names.Should().BeEquivalentTo(new[]
            {
                "AT_AT",
                "TIE_FIGHTER",
                "STAR_DESTROYER",
                "SPACE_STATION"
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractObjectNames_ShouldReturnEmpty_ForMissingPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xml");

        var names = InvokeExtractor(path);

        names.Should().BeEmpty();
    }

    [Fact]
    public void ExtractObjectNames_ShouldReturnEmpty_ForMalformedXml()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "<Root><Broken>");

            var names = InvokeExtractor(path);

            names.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IReadOnlyList<string> InvokeExtractor(string xmlPath)
    {
        var type = typeof(CatalogService).Assembly.GetType("SwfocTrainer.Catalog.Parsing.XmlObjectExtractor", throwOnError: true);
        type.Should().NotBeNull();

        var method = type!.GetMethod("ExtractObjectNames", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { xmlPath });
        result.Should().BeAssignableTo<IReadOnlyList<string>>();
        return (IReadOnlyList<string>)result!;
    }
}
