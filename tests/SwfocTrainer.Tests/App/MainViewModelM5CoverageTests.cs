using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelM5CoverageTests
{
    [Theory]
    [InlineData("spawn_tactical_entity", "ForceZeroTactical", "EphemeralBattleOnly", "reinforcement_zone")]
    [InlineData("spawn_galactic_entity", "Normal", "PersistentGalactic", null)]
    public void ApplyActionSpecificPayloadDefaults_ShouldSetSpawnPolicies(
        string actionId,
        string expectedPopulationPolicy,
        string expectedPersistencePolicy,
        string? expectedPlacementMode)
    {
        var payload = new JsonObject();

        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(actionId, payload);

        payload["populationPolicy"]!.ToString().Should().Be(expectedPopulationPolicy);
        payload["persistencePolicy"]!.ToString().Should().Be(expectedPersistencePolicy);
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
        if (expectedPlacementMode is not null)
        {
            payload["placementMode"]!.ToString().Should().Be(expectedPlacementMode);
        }
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldSetBuildingAndPlanetPolicies()
    {
        var buildingPayload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("place_planet_building", buildingPayload);

        buildingPayload["placementMode"]!.ToString().Should().Be("safe_rules");
        buildingPayload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
        buildingPayload["forceOverride"]!.GetValue<bool>().Should().BeFalse();

        var flipPayload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("flip_planet_owner", flipPayload);

        flipPayload["flipMode"]!.ToString().Should().Be("convert_everything");
        flipPayload["planetFlipMode"]!.ToString().Should().Be("convert_everything");
        flipPayload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
        flipPayload["forceOverride"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldSetHeroPolicies()
    {
        var editPayload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("edit_hero_state", editPayload);

        editPayload["desiredState"]!.ToString().Should().Be("alive");
        editPayload["allowDuplicate"]!.GetValue<bool>().Should().BeFalse();

        var variantPayload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("create_hero_variant", variantPayload);

        variantPayload["variantGenerationMode"]!.ToString().Should().Be("patch_mod_overlay");
        variantPayload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildEntityRoster_ShouldParseExtendedEntryAndInferStates()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog"] =
            [
                "Unit|STORMTROOPER|base_swfoc|1125571106|Textures/UI/storm.dds|dep_a;dep_b",
                "Hero|DARTH_VADER"
            ]
        };

        var rows = MainViewModelRosterHelpers.BuildEntityRoster(catalog, "base_swfoc", "1125571106");

        rows.Should().HaveCount(2);

        var native = rows.Single(x => x.EntityId == "STORMTROOPER");
        native.VisualState.Should().Be(RosterEntityVisualState.Resolved);
        native.CompatibilityState.Should().Be(RosterEntityCompatibilityState.Native);
        native.DependencySummary.Should().Be("dep_a;dep_b");

        var fallback = rows.Single(x => x.EntityId == "DARTH_VADER");
        fallback.VisualState.Should().Be(RosterEntityVisualState.Missing);
        fallback.SourceProfileId.Should().Be("base_swfoc");
        fallback.SourceWorkshopId.Should().Be("1125571106");
    }

    [Fact]
    public void BuildEntityRoster_ShouldMarkForeignWorkshopAsRequiresTransplant()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog"] = ["Unit|AT_AT|foreign_profile|9999999999|Textures/UI/atat.dds"]
        };

        var rows = MainViewModelRosterHelpers.BuildEntityRoster(catalog, "base_swfoc", "1125571106");

        rows.Should().ContainSingle();
        rows[0].CompatibilityState.Should().Be(RosterEntityCompatibilityState.RequiresTransplant);
        rows[0].TransplantReportId.Should().Contain("9999999999");
    }

    [Fact]
    public void BuildEntityRoster_ShouldReturnEmpty_ForNullOrInvalidCatalogInputs()
    {
        MainViewModelRosterHelpers.BuildEntityRoster(null, "base_swfoc", "1125571106")
            .Should()
            .BeEmpty();

        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog"] = ["", "Unit|", "not-json"]
        };

        MainViewModelRosterHelpers.BuildEntityRoster(catalog, "base_swfoc", "1125571106")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void BuildEntityRoster_ShouldRespectDeclaredStates_AndNormalizeScalarMetadata()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog_typed"] =
            [
                """
                {
                  "entityId": "EMPIRE_STORMTROOPER_SQUAD",
                  "displayName": "Stormtrooper Squad",
                  "kind": "Unit",
                  "sourceProfileId": "base_swfoc",
                  "sourceWorkshopId": "9999999999",
                  "affiliations": "EMPIRE; PENTASTAR",
                  "populationValue": 2.5,
                  "buildCostCredits": true,
                  "visualState": "Resolved",
                  "compatibilityState": "Blocked",
                  "visualRef": "",
                  "dependencyRefs": "dep_a, dep_b",
                  "sourceLabel": "typed override"
                }
                """
            ]
        };

        var row = MainViewModelRosterHelpers.BuildEntityRoster(catalog, "base_swfoc", "1125571106")
            .Should()
            .ContainSingle()
            .Subject;

        row.SourceLabel.Should().Be("typed override");
        row.AffiliationSummary.Should().Be("EMPIRE, PENTASTAR");
        row.PopulationCostText.Should().Be("2.5");
        row.BuildCostText.Should().Be("True");
        row.VisualState.Should().Be(RosterEntityVisualState.Resolved);
        row.VisualSummary.Should().Be("resolved");
        row.CompatibilityState.Should().Be(RosterEntityCompatibilityState.Blocked);
        row.CompatibilitySummary.Should().Be("Blocked (9999999999)");
        row.DependencySummary.Should().Be("dep_a; dep_b");
        row.TransplantReportId.Should().BeEmpty();
    }

    [Fact]
    public void BuildEntityRoster_ShouldPreferTypedProjection_WhenAvailable_AndFallbackToLegacyRows()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog_typed"] =
            [
                """
                {
                  "entityId": "EMPIRE_STORMTROOPER_SQUAD",
                  "displayName": "Stormtrooper Squad",
                  "displayNameKey": "TEXT_STORMTROOPER",
                  "kind": "Unit",
                  "sourceProfileId": "base_swfoc",
                  "sourceWorkshopId": "1125571106",
                  "defaultFaction": "EMPIRE",
                  "affiliations": ["EMPIRE", "PENTASTAR"],
                  "populationValue": 2,
                  "buildCostCredits": 200,
                  "visualRef": "i_stormtrooper.tga",
                  "visualState": "Resolved",
                  "compatibilityState": "Native",
                  "dependencyRefs": ["EMPIRE_BARRACKS", "STORMTROOPER_COMPANY"]
                }
                """
            ],
            ["entity_catalog"] =
            [
                "Unit|EMPIRE_STORMTROOPER_SQUAD|base_swfoc|1125571106|legacy.dds|legacy_dep",
                "Hero|DARTH_VADER|base_swfoc|1125571106||"
            ]
        };

        var rows = MainViewModelRosterHelpers.BuildEntityRoster(catalog, "base_swfoc", "1125571106");

        rows.Should().HaveCount(2);

        var typed = rows.Single(x => x.EntityId == "EMPIRE_STORMTROOPER_SQUAD");
        typed.DisplayName.Should().Be("Stormtrooper Squad");
        typed.DisplayNameKey.Should().Be("TEXT_STORMTROOPER");
        typed.AffiliationSummary.Should().Be("EMPIRE, PENTASTAR");
        typed.PopulationCostText.Should().Be("2");
        typed.BuildCostText.Should().Be("200");
        typed.DependencySummary.Should().Be("EMPIRE_BARRACKS; STORMTROOPER_COMPANY");
        typed.SourceLabel.Should().Contain("base_swfoc");
        typed.SourceLabel.Should().Contain("1125571106");

        var fallback = rows.Single(x => x.EntityId == "DARTH_VADER");
        fallback.DisplayName.Should().Be("DARTH_VADER");
        fallback.AffiliationSummary.Should().Be("HeroOwner");
        fallback.PopulationCostText.Should().Be("n/a");
        fallback.BuildCostText.Should().Be("n/a");
    }

    [Fact]
    public void BuildEntityRoster_ShouldFallbackToLegacyParsing_WhenTypedJsonIsMalformed()
    {
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog"] =
            [
                "{\"entityId\":\"BROKEN_JSON\"",
                "Hero|DARTH_VADER|base_swfoc|1125571106||dep_one,dep_two"
            ]
        };

        var row = MainViewModelRosterHelpers.BuildEntityRoster(catalog, "base_swfoc", "1125571106")
            .Should()
            .ContainSingle()
            .Subject;

        row.EntityId.Should().Be("DARTH_VADER");
        row.EntityKind.Should().Be("Hero");
        row.DefaultFaction.Should().Be("HeroOwner");
        row.DependencySummary.Should().Be("dep_one,dep_two");
        row.VisualState.Should().Be(RosterEntityVisualState.Missing);
        row.CompatibilityState.Should().Be(RosterEntityCompatibilityState.Native);
    }
}
