using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class SdkOperationRouterCoverageTests
{
    private static readonly Type RouterType = typeof(SdkOperationRouter);

    [Fact]
    public void FormatAllowedModes_ShouldHandleEmptyAndSortedSets()
    {
        var empty = (string)InvokeStatic("FormatAllowedModes", new HashSet<RuntimeMode>())!;
        empty.Should().Be("any");

        var sorted = (string)InvokeStatic("FormatAllowedModes", new HashSet<RuntimeMode> { RuntimeMode.Galactic, RuntimeMode.AnyTactical })!;
        sorted.Should().Be("AnyTactical,Galactic");
    }

    [Fact]
    public void ReadContextString_ShouldHandleNullAndNonStringValues()
    {
        ((string?)InvokeStatic("ReadContextString", null, "x")).Should().BeNull();

        var context = new Dictionary<string, object?>
        {
            ["name"] = "abc",
            ["num"] = 12
        };

        ((string?)InvokeStatic("ReadContextString", context, "name")).Should().Be("abc");
        ((string?)InvokeStatic("ReadContextString", context, "num")).Should().Be("12");
        ((string?)InvokeStatic("ReadContextString", context, "missing")).Should().BeNull();
    }

    [Fact]
    public void ReadContextInt_ShouldHandleIntLongStringAndInvalid()
    {
        var context = new Dictionary<string, object?>
        {
            ["int"] = 7,
            ["long"] = 8L,
            ["str"] = "9",
            ["bad"] = "x",
            ["tooBig"] = long.MaxValue
        };

        ((int?)InvokeStatic("ReadContextInt", context, "int")).Should().Be(7);
        ((int?)InvokeStatic("ReadContextInt", context, "long")).Should().Be(8);
        ((int?)InvokeStatic("ReadContextInt", context, "str")).Should().Be(9);
        ((int?)InvokeStatic("ReadContextInt", context, "bad")).Should().BeNull();
        ((int?)InvokeStatic("ReadContextInt", context, "tooBig")).Should().BeNull();
        ((int?)InvokeStatic("ReadContextInt", null, "missing")).Should().BeNull();
    }

    [Fact]
    public void ExtractResolvedAnchors_ShouldHandleSupportedRepresentations()
    {
        var fromEnumerable = (IReadOnlySet<string>)InvokeStatic(
            "ExtractResolvedAnchors",
            new Dictionary<string, object?>
            {
                ["resolvedAnchors"] = new[] { "a", "", "b" }
            })!;
        fromEnumerable.Should().BeEquivalentTo("a", "b");

        var fromJsonArray = (IReadOnlySet<string>)InvokeStatic(
            "ExtractResolvedAnchors",
            new Dictionary<string, object?>
            {
                ["resolvedAnchors"] = new JsonArray(JsonValue.Create("x"), JsonValue.Create(" "), JsonValue.Create("y"))
            })!;
        fromJsonArray.Should().BeEquivalentTo("x", "y");

        var fromSerialized = (IReadOnlySet<string>)InvokeStatic(
            "ExtractResolvedAnchors",
            new Dictionary<string, object?>
            {
                ["resolvedAnchors"] = "[\"k1\",\"k2\",\"\"]"
            })!;
        fromSerialized.Should().BeEquivalentTo("k1", "k2");

        var fromInvalidSerialized = (IReadOnlySet<string>)InvokeStatic(
            "ExtractResolvedAnchors",
            new Dictionary<string, object?>
            {
                ["resolvedAnchors"] = "not-json"
            })!;
        fromInvalidSerialized.Should().BeEmpty();

        var fromUnsupported = (IReadOnlySet<string>)InvokeStatic(
            "ExtractResolvedAnchors",
            new Dictionary<string, object?>
            {
                ["resolvedAnchors"] = 123
            })!;
        fromUnsupported.Should().BeEmpty();
    }

    [Fact]
    public void MergeContext_ShouldPreserveOriginalAndAddCapabilityFields()
    {
        var capability = new CapabilityResolutionResult(
            ProfileId: "base_swfoc",
            OperationId: "spawn",
            State: SdkCapabilityStatus.Available,
            ReasonCode: CapabilityReasonCode.AllRequiredAnchorsPresent,
            Confidence: 0.9d,
            FingerprintId: "fp-1",
            MissingAnchors: Array.Empty<string>(),
            MatchedAnchors: new[] { "a" },
            Metadata: new CapabilityResolutionMetadata(
                SourceReasonCode: "source_ok",
                SourceState: "verified",
                DeclaredAvailable: true));

        var variant = new ProfileVariantResolution("requested", "resolved", "reason", 0.7d);

        var merged = (IReadOnlyDictionary<string, object?>)InvokeStatic(
            "MergeContext",
            new Dictionary<string, object?>
            {
                ["existing"] = "value"
            },
            capability,
            variant)!;

        merged["existing"].Should().Be("value");
        merged["resolvedVariant"].Should().Be("resolved");
        merged["variantReasonCode"].Should().Be("reason");
        merged["fingerprintId"].Should().Be("fp-1");
        merged["capabilityState"].Should().Be("Available");
        merged["capabilityReasonCode"].Should().Be("AllRequiredAnchorsPresent");
        merged["capabilityMapReasonCode"].Should().Be("source_ok");
        merged["capabilityMapState"].Should().Be("verified");
        merged["capabilityDeclaredAvailable"].Should().Be(true);
    }

    [Fact]
    public void CreateModeMismatchResult_ShouldReturnNullWhenModeAllowed_AndFailureOtherwise()
    {
        var requestAllowed = new SdkOperationRequest(
            OperationId: "spawn",
            Payload: new JsonObject(),
            IsMutation: true,
            RuntimeMode: RuntimeMode.Galactic,
            ProfileId: "base");

        var requestBlocked = requestAllowed with { RuntimeMode = RuntimeMode.Unknown };

        var definition = SdkOperationDefinition.Mutation("spawn", RuntimeMode.Galactic);

        InvokeStatic("CreateModeMismatchResult", requestAllowed, definition).Should().BeNull();

        var blocked = InvokeStatic("CreateModeMismatchResult", requestBlocked, definition);
        blocked.Should().NotBeNull();
        ReadProperty<CapabilityReasonCode>(blocked!, "ReasonCode").Should().Be(CapabilityReasonCode.ModeMismatch);
    }

    [Fact]
    public void CreateUnknownOperationResult_AndFeatureGateDisabledResult_ShouldBeUnavailable()
    {
        var unknown = InvokeStatic("CreateUnknownOperationResult", "mystery")!;
        ReadProperty<bool>(unknown, "Succeeded").Should().BeFalse();
        ReadProperty<CapabilityReasonCode>(unknown, "ReasonCode").Should().Be(CapabilityReasonCode.UnknownSdkOperation);

        var gate = InvokeStatic("CreateFeatureGateDisabledResult")!;
        ReadProperty<bool>(gate, "Succeeded").Should().BeFalse();
        ReadProperty<CapabilityReasonCode>(gate, "ReasonCode").Should().Be(CapabilityReasonCode.FeatureFlagDisabled);
    }

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = RouterType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected method {methodName}");
        return method!.Invoke(null, args);
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return (T)property!.GetValue(instance)!;
    }
}

