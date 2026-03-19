#pragma warning disable CA1014
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class NamedPipeExtenderBackendContextHelpersCoverageTests
{
    [Fact]
    public void ParseCapabilities_ShouldAddNativeFallbacks_WhenDiagnosticsMissing()
    {
        var features = new[] { "feature_a", "feature_b" };

        var parsed = NamedPipeExtenderBackendContextHelpers.ParseCapabilities(
            diagnostics: null,
            nativeAuthoritativeFeatureIds: features);

        parsed.Should().ContainKey("feature_a");
        parsed.Should().ContainKey("feature_b");
        parsed["feature_a"].Available.Should().BeFalse();
        parsed["feature_a"].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
    }

    [Fact]
    public void ParseCapabilities_ShouldParseStatesAndReasonCodes_AndFillMissingFeatures()
    {
        var capabilityJson = JsonDocument.Parse(
            """
            {
              "set_credits": { "available": true, "state": "Verified", "reasonCode": "CAPABILITY_PROBE_PASS" },
              "set_unit_cap": { "available": false, "state": "Experimental", "reasonCode": "CAPABILITY_FEATURE_EXPERIMENTAL" },
              "weird_feature": { "available": true, "state": "unknown", "reasonCode": "NOT_A_REASON_CODE" },
              "ignored_scalar": 15
            }
            """);

        var parsed = NamedPipeExtenderBackendContextHelpers.ParseCapabilities(
            diagnostics: new Dictionary<string, object?>
            {
                ["capabilities"] = capabilityJson.RootElement
            },
            nativeAuthoritativeFeatureIds: new[] { "set_credits", "toggle_ai" });

        parsed["set_credits"].Available.Should().BeTrue();
        parsed["set_credits"].Confidence.Should().Be(CapabilityConfidenceState.Verified);
        parsed["set_credits"].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        parsed["set_unit_cap"].Available.Should().BeFalse();
        parsed["set_unit_cap"].Confidence.Should().Be(CapabilityConfidenceState.Experimental);

        parsed["weird_feature"].Confidence.Should().Be(CapabilityConfidenceState.Unknown);
        parsed["weird_feature"].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_UNKNOWN);

        parsed.Should().ContainKey("toggle_ai");
        parsed["toggle_ai"].Available.Should().BeFalse();
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(8L, 8)]
    [InlineData("9", 9)]
    [InlineData("not-int", 0)]
    public void ReadContextInt_ShouldHandlePrimitiveVariants(object rawValue, int expected)
    {
        var context = new Dictionary<string, object?>
        {
            ["value"] = rawValue
        };

        var parsed = NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "value");

        parsed.Should().Be(expected);
    }

    [Fact]
    public void ReadContextInt_ShouldReturnZero_ForMissingAndOutOfRangeLong()
    {
        var context = new Dictionary<string, object?>
        {
            ["value"] = long.MaxValue
        };

        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "value").Should().Be(0);
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "missing").Should().Be(0);
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(null, "missing").Should().Be(0);
    }

    [Fact]
    public void ReadContextString_ShouldReturnStringValue_OrEmpty()
    {
        var context = new Dictionary<string, object?>
        {
            ["str"] = "alpha",
            ["num"] = 44
        };

        NamedPipeExtenderBackendContextHelpers.ReadContextString(context, "str").Should().Be("alpha");
        NamedPipeExtenderBackendContextHelpers.ReadContextString(context, "num").Should().Be("44");
        NamedPipeExtenderBackendContextHelpers.ReadContextString(context, "missing").Should().BeEmpty();
        NamedPipeExtenderBackendContextHelpers.ReadContextString(null, "missing").Should().BeEmpty();
    }

    [Fact]
    public void ReadContextAnchors_ShouldMergeAllSupportedRepresentations()
    {
        var jsonObjectAnchors = new JsonObject
        {
            ["json_obj"] = "0x1",
            ["ignored_null"] = null
        };

        var jsonElementAnchors = JsonDocument.Parse("{\"json_element\":\"0x2\"}").RootElement;

        var objectDictAnchors = new Dictionary<string, object?>
        {
            ["dict_obj"] = "0x3",
            ["dict_null"] = null
        };

        var stringPairsAnchors = new List<KeyValuePair<string, string>>
        {
            new("pair_a", "0x4"),
            new("pair_b", " "),
        };

        var serializedAnchors = "{\"serialized\":\"0x5\",\"blank\":\"\"}";

        var context = new Dictionary<string, object?>
        {
            ["resolvedAnchors"] = jsonObjectAnchors,
            ["anchors"] = jsonElementAnchors
        };

        var merged = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context);
        merged["json_obj"]!.ToString().Should().Be("0x1");
        merged.ContainsKey("ignored_null").Should().BeFalse();

        var context2 = new Dictionary<string, object?> { ["resolvedAnchors"] = objectDictAnchors };
        var merged2 = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context2);
        merged2["dict_obj"]!.ToString().Should().Be("0x3");
        merged2.ContainsKey("dict_null").Should().BeFalse();

        var context3 = new Dictionary<string, object?> { ["resolvedAnchors"] = stringPairsAnchors };
        var merged3 = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context3);
        merged3["pair_a"]!.ToString().Should().Be("0x4");
        merged3.ContainsKey("pair_b").Should().BeFalse();

        var context4 = new Dictionary<string, object?> { ["resolvedAnchors"] = serializedAnchors };
        var merged4 = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context4);
        merged4["serialized"]!.ToString().Should().Be("0x5");
        merged4.ContainsKey("blank").Should().BeFalse();
    }

    [Fact]
    public void ReadContextAnchors_ShouldFallbackToLegacyAnchorsAndIgnoreBadSerializedJson()
    {
        var context = new Dictionary<string, object?>
        {
            ["resolvedAnchors"] = "not-json",
            ["anchors"] = new Dictionary<string, object?>
            {
                ["legacy"] = "0xABC"
            }
        };

        var merged = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context);

        merged["legacy"]!.ToString().Should().Be("0xABC");
    }

    [Fact]
    public void ParseCapabilities_ShouldIgnoreNonObjectCapabilitiesNode()
    {
        var doc = JsonDocument.Parse("[]");

        var parsed = NamedPipeExtenderBackendContextHelpers.ParseCapabilities(
            diagnostics: new Dictionary<string, object?>
            {
                ["capabilities"] = doc.RootElement
            },
            nativeAuthoritativeFeatureIds: new[] { "must_exist" });

        parsed.Should().ContainKey("must_exist");
        parsed["must_exist"].Available.Should().BeFalse();
    }
}

#pragma warning restore CA1014

