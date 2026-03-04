#pragma warning disable CA1014
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterHelperPolicyCoverageTests
{
    [Fact]
    public void ApplyHelperActionPolicies_ShouldBlockTacticalSpawn_WhenPlacementMissing()
    {
        var request = BuildRequest("spawn_tactical_entity", new JsonObject());

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");
        var rewritten = ReadProperty<ActionExecutionRequest>(resolution, "Request");

        blocked.Should().NotBeNull();
        blocked!.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.SPAWN_PLACEMENT_INVALID.ToString());
        rewritten.Payload.Should().NotContainKey("populationPolicy");
    }

    [Fact]
    public void ApplyHelperActionPolicies_ShouldAcceptTacticalSpawn_WithEntryMarker()
    {
        var request = BuildRequest("spawn_tactical_entity", new JsonObject
        {
            ["entityId"] = "u1",
            ["entryMarker"] = "marker_a"
        });

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");
        var rewritten = ReadProperty<ActionExecutionRequest>(resolution, "Request");

        blocked.Should().BeNull();
        rewritten.Payload["populationPolicy"]!.GetValue<string>().Should().Be("ForceZeroTactical");
        rewritten.Payload["persistencePolicy"]!.GetValue<string>().Should().Be("EphemeralBattleOnly");
        rewritten.Payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ApplyHelperActionPolicies_ShouldSetGalacticSpawnDefaults()
    {
        var request = BuildRequest("spawn_galactic_entity", new JsonObject());

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var rewritten = ReadProperty<ActionExecutionRequest>(resolution, "Request");

        rewritten.Payload["populationPolicy"]!.GetValue<string>().Should().Be("Normal");
        rewritten.Payload["persistencePolicy"]!.GetValue<string>().Should().Be("PersistentGalactic");
    }

    [Fact]
    public void ApplyHelperActionPolicies_ShouldBlockPlanetBuilding_WhenUnsafeWithoutOverride()
    {
        var request = BuildRequest("place_planet_building", new JsonObject
        {
            ["entityId"] = "b1",
            ["planetId"] = "p1",
            ["placementMode"] = "anywhere"
        });

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");

        blocked.Should().NotBeNull();
        blocked!.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.BUILDING_SLOT_INVALID.ToString());
    }

    [Fact]
    public void ApplyHelperActionPolicies_ShouldAnnotateBuildingForceOverride()
    {
        var request = BuildRequest("place_planet_building", new JsonObject
        {
            ["entityId"] = "b1",
            ["planetId"] = "p1",
            ["placementMode"] = "anywhere",
            ["forceOverride"] = true
        });

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");
        var diagnostics = ReadProperty<IReadOnlyDictionary<string, object?>>(resolution, "Diagnostics");

        blocked.Should().BeNull();
        diagnostics.Should().ContainKey("policyReasonCodes");
        diagnostics["policyReasonCodes"].Should().BeOfType<string[]>().Which
            .Should().Contain(RuntimeReasonCode.BUILDING_FORCE_OVERRIDE_APPLIED.ToString());
    }

    [Fact]
    public void ApplyHelperActionPolicies_ShouldBlockTransferFleet_WhenSourceAndTargetMatch()
    {
        var request = BuildRequest("transfer_fleet_safe", new JsonObject
        {
            ["entityId"] = "fleet_1",
            ["sourceFaction"] = "Empire",
            ["targetFaction"] = "Empire",
            ["safePlanetId"] = "p1"
        });

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");

        blocked.Should().NotBeNull();
        blocked!.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.SAFETY_MUTATION_BLOCKED.ToString());
    }

    [Fact]
    public void ApplyHelperActionPolicies_ShouldBlockPlanetFlip_WhenModeInvalid()
    {
        var request = BuildRequest("flip_planet_owner", new JsonObject
        {
            ["entityId"] = "planet_1",
            ["targetFaction"] = "Rebel",
            ["flipMode"] = "unsafe"
        });

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");

        blocked.Should().NotBeNull();
        blocked!.Diagnostics!["reasonCode"]!.ToString().Should().Be(RuntimeReasonCode.SAFETY_MUTATION_BLOCKED.ToString());
    }

    [Fact]
    public void ApplyHelperActionPolicies_ShouldCopyLegacyPlanetFlipMode()
    {
        var request = BuildRequest("flip_planet_owner", new JsonObject
        {
            ["entityId"] = "planet_1",
            ["targetFaction"] = "Rebel",
            ["planetFlipMode"] = "empty_and_retreat"
        });

        var resolution = InvokeStatic("ApplyHelperActionPolicies", request)!;
        var rewritten = ReadProperty<ActionExecutionRequest>(resolution, "Request");
        var blocked = ReadProperty<ActionExecutionResult?>(resolution, "BlockedResult");

        blocked.Should().BeNull();
        rewritten.Payload["flipMode"]!.GetValue<string>().Should().Be("empty_and_retreat");
        rewritten.Payload["planetFlipMode"]!.GetValue<string>().Should().Be("empty_and_retreat");
    }

    [Fact]
    public void ResolveHelperOperationPolicy_ShouldPreferExplicitPayloadPolicy()
    {
        var request = BuildRequest("spawn_tactical_entity", new JsonObject
        {
            ["operationPolicy"] = "custom"
        });

        var policy = (string?)InvokeStatic("ResolveHelperOperationPolicy", request);

        policy.Should().Be("custom");
    }

    [Fact]
    public void ResolveMutationIntent_ShouldReturnUnknown_ForUnknownAction()
    {
        var intent = (string?)InvokeStatic("ResolveMutationIntent", "mystery_action");

        intent.Should().Be("unknown");
    }

    [Theory]
    [InlineData("set_context_faction", true)]
    [InlineData("set_context_allegiance", true)]
    [InlineData("set_credits", false)]
    public void ShouldDefaultCrossFaction_ShouldMapKnownActions(string actionId, bool expected)
    {
        var actual = (bool)InvokeStatic("ShouldDefaultCrossFaction", actionId)!;
        actual.Should().Be(expected);
    }

    [Fact]
    public void HasAnyPayloadValue_ShouldIgnoreWhitespaceAndObjectNodes()
    {
        var payload = new JsonObject
        {
            ["blank"] = "   ",
            ["obj"] = new JsonObject(),
            ["ok"] = "value"
        };

        ((bool)InvokeStatic("HasAnyPayloadValue", payload, new[] { "blank", "obj" })!).Should().BeFalse();
        ((bool)InvokeStatic("HasAnyPayloadValue", payload, new[] { "blank", "ok" })!).Should().BeTrue();
    }

    private static ActionExecutionRequest BuildRequest(string actionId, JsonObject payload)
    {
        return new ActionExecutionRequest(
            Action: new ActionSpec(
                actionId,
                ActionCategory.Global,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: RuntimeMode.Galactic,
            Context: null);
    }

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = typeof(RuntimeAdapter).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected private static method '{methodName}'");
        return method!.Invoke(null, args);
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return (T)property!.GetValue(instance)!;
    }
}

