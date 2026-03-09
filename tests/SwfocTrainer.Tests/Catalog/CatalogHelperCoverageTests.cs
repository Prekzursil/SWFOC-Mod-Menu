using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Catalog;

public sealed class CatalogHelperCoverageTests
{
    [Fact]
    public void TryCreateRecord_ShouldResolveVisualMetadata_AndFilterVisualDependencyEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-helper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var sourceDirectory = Path.Combine(root, "Data", "XML");
            Directory.CreateDirectory(sourceDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "Units.xml");
            File.WriteAllText(sourcePath, "<Root />");

            var iconPath = Path.Combine(root, "Art", "Textures", "UI", "i_trooper.tga");
            Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
            File.WriteAllText(iconPath, "icon");

            var element = XElement.Parse(
                """
                <LandUnit Name="EMPIRE_TROOPER">
                  <Text_ID>TEXT_TROOPER</Text_ID>
                  <Encyclopedia_Text>TEXT_TROOPER_DESC</Encyclopedia_Text>
                  <Affiliation>EMPIRE|PIRATE</Affiliation>
                  <Population_Value>3</Population_Value>
                  <Build_Cost_Credits>450</Build_Cost_Credits>
                  <Icon_Name>i_trooper.tga</Icon_Name>
                  <Required_Prerequisites>TECH_1;TECH_2</Required_Prerequisites>
                  <Model_Name>i_trooper.tga</Model_Name>
                  <Tactical_Override_Model>MODEL_ALT</Tactical_Override_Model>
                </LandUnit>
                """);

            var (created, record) = InvokeTryCreateRecord("profile_runtime", sourcePath, element);

            created.Should().BeTrue();
            record.EntityId.Should().Be("EMPIRE_TROOPER");
            record.Kind.Should().Be(CatalogEntityKind.Unit);
            record.DisplayNameKey.Should().Be("TEXT_TROOPER");
            record.EncyclopediaTextKey.Should().Be("TEXT_TROOPER_DESC");
            record.Affiliations.Should().Equal("EMPIRE", "PIRATE");
            record.PopulationValue.Should().Be(3);
            record.BuildCostCredits.Should().Be(450);
            record.VisualRef.Should().Be(iconPath);
            record.VisualState.Should().Be(CatalogEntityVisualState.Resolved);
            record.CompatibilityState.Should().Be(CatalogEntityCompatibilityState.Unknown);
            record.DependencyRefs.Should().BeEquivalentTo(new[] { "MODEL_ALT", "TECH_1", "TECH_2" });
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("elementName", "LandUnit"));
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("displayNameKey", "TEXT_TROOPER"));
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("encyclopediaTextKey", "TEXT_TROOPER_DESC"));
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("visualRef", "i_trooper.tga"));
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("resolvedVisualRef", iconPath));
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("visualState", CatalogEntityVisualState.Resolved.ToString()));
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("populationValue", "3"));
            record.Metadata.Should().Contain(new KeyValuePair<string, string>("buildCostCredits", "450"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TryCreateRecord_ShouldFallbackFactionAffiliation_AndReadAttributeMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-faction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var sourcePath = Path.Combine(root, "Factions.xml");
            File.WriteAllText(sourcePath, "<Root />");

            var element = XElement.Parse("<Faction Name=\"REBEL\" Text_ID=\"TEXT_REBEL\" Encyclopedia_Text=\"TEXT_REBEL_DESC\" />");

            var (created, record) = InvokeTryCreateRecord("profile_runtime", sourcePath, element);

            created.Should().BeTrue();
            record.Kind.Should().Be(CatalogEntityKind.Faction);
            record.DisplayNameKey.Should().Be("TEXT_REBEL");
            record.EncyclopediaTextKey.Should().Be("TEXT_REBEL_DESC");
            record.Affiliations.Should().ContainSingle().Which.Should().Be("REBEL");
            record.DefaultAffiliation.Should().Be("REBEL");
            record.VisualRef.Should().BeNull();
            record.VisualState.Should().Be(CatalogEntityVisualState.Unknown);
            record.CompatibilityState.Should().Be(CatalogEntityCompatibilityState.Unknown);
            record.Metadata.Keys.Should().NotContain(new[] { "visualRef", "resolvedVisualRef", "visualState" });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TryCreateRecord_ShouldReturnFalse_WhenNoSupportedIdentifierExists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-catalog-no-id-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var sourcePath = Path.Combine(root, "Objects.xml");
            File.WriteAllText(sourcePath, "<Root />");

            var element = XElement.Parse("<LandUnit Alias=\"EMPIRE_TROOPER\" />");

            var (created, record) = InvokeTryCreateRecord("profile_runtime", sourcePath, element);

            created.Should().BeFalse();
            record.EntityId.Should().BeNullOrEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static (bool Created, EntityCatalogRecord Record) InvokeTryCreateRecord(string profileId, string sourcePath, XElement element)
    {
        var method = typeof(CatalogService).GetMethod("TryCreateRecord", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var args = new object?[] { profileId, sourcePath, element, null };
        var created = (bool)method!.Invoke(null, args)!;
        var record = args[3] is EntityCatalogRecord typed ? typed : default;
        return (created, record);
    }
}
