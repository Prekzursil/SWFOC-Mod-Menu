using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class NamedPipeExtenderBackendContextHelpersTests
{
    [Fact]
    public void ParseCapabilities_ShouldReturnNativeFallbackEntries_WhenDiagnosticsAreMissing()
    {
        var capabilities = NamedPipeExtenderBackendContextHelpers.ParseCapabilities(
            diagnostics: null,
            nativeAuthoritativeFeatureIds: new[] { "set_credits", "toggle_ai" });

        capabilities.Should().ContainKey("set_credits");
        capabilities["set_credits"].Available.Should().BeFalse();
        capabilities["set_credits"].Confidence.Should().Be(CapabilityConfidenceState.Unknown);
        capabilities["set_credits"].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
        capabilities.Should().ContainKey("toggle_ai");
    }

    [Fact]
    public void ParseCapabilities_ShouldSkipMalformedNodes_AndFallbackUnknownReasonCodes()
    {
        using var payload = JsonDocument.Parse(
            """
            {
              "capabilities": {
                "set_credits": {
                  "available": true,
                  "state": "Verified",
                  "reasonCode": "CAPABILITY_PROBE_PASS"
                },
                "toggle_ai": {
                  "available": false,
                  "state": "Unavailable",
                  "reasonCode": "NOT_A_REASON"
                },
                "set_unit_cap": {
                  "available": "true"
                },
                "invalid_node": "not-an-object"
              }
            }
            """);
        var diagnostics = new Dictionary<string, object?>
        {
            ["capabilities"] = payload.RootElement.GetProperty("capabilities")
        };

        var capabilities = NamedPipeExtenderBackendContextHelpers.ParseCapabilities(
            diagnostics,
            new[] { "set_credits", "toggle_ai", "spawn_unit_helper" });

        capabilities.Should().ContainKey("set_credits");
        capabilities["set_credits"].Available.Should().BeTrue();
        capabilities["set_credits"].Confidence.Should().Be(CapabilityConfidenceState.Verified);
        capabilities["set_credits"].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        capabilities.Should().ContainKey("toggle_ai");
        capabilities["toggle_ai"].Available.Should().BeFalse();
        capabilities["toggle_ai"].Confidence.Should().Be(CapabilityConfidenceState.Unknown);
        capabilities["toggle_ai"].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_UNKNOWN);

        capabilities.Should().ContainKey("set_unit_cap");
        capabilities["set_unit_cap"].Available.Should().BeFalse();
        capabilities["set_unit_cap"].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_UNKNOWN);

        capabilities.Should().NotContainKey("invalid_node");
        capabilities.Should().ContainKey("spawn_unit_helper");
        capabilities["spawn_unit_helper"].Available.Should().BeFalse();
    }

    [Fact]
    public void ReadContextInt_ShouldHandleNumericConversionsAndInvalidValues()
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["directInt"] = 17,
            ["inRangeLong"] = (long)18,
            ["outOfRangeLong"] = (long)int.MaxValue + 1,
            ["numericString"] = "19",
            ["invalidString"] = "abc"
        };

        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "directInt").Should().Be(17);
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "inRangeLong").Should().Be(18);
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "outOfRangeLong").Should().Be(0);
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "numericString").Should().Be(19);
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "invalidString").Should().Be(0);
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(context, "missing").Should().Be(0);
    }

    [Fact]
    public void ReadContextString_ShouldReturnStringifiedValues_AndEmptyForMissingEntries()
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = "hello",
            ["number"] = 42,
            ["nullValue"] = null
        };

        NamedPipeExtenderBackendContextHelpers.ReadContextString(context, "text").Should().Be("hello");
        NamedPipeExtenderBackendContextHelpers.ReadContextString(context, "number").Should().Be("42");
        NamedPipeExtenderBackendContextHelpers.ReadContextString(context, "nullValue").Should().BeEmpty();
        NamedPipeExtenderBackendContextHelpers.ReadContextString(context, "missing").Should().BeEmpty();
    }

    [Fact]
    public void ReadContextAnchors_ShouldUseResolvedAnchors_WhenNonEmpty()
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedAnchors"] = new JsonObject
            {
                ["freeze_timer"] = "0xA1",
                ["null_ignored"] = null
            },
            ["anchors"] = new Dictionary<string, object?>
            {
                ["legacy_anchor"] = "0xLEGACY"
            }
        };

        var anchors = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context);

        anchors.ContainsKey("freeze_timer").Should().BeTrue();
        anchors["freeze_timer"]!.GetValue<string>().Should().Be("0xA1");
        anchors.ContainsKey("null_ignored").Should().BeFalse();
        anchors.ContainsKey("legacy_anchor").Should().BeFalse();
    }

    [Fact]
    public void ReadContextAnchors_ShouldFallbackToLegacyAnchors_WhenResolvedAnchorsAreEmpty()
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedAnchors"] = new JsonObject(),
            ["anchors"] = new Dictionary<string, object?>
            {
                ["set_credits"] = "0xB2",
                ["null_ignored"] = null
            }
        };

        var anchors = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context);

        anchors.ContainsKey("set_credits").Should().BeTrue();
        anchors["set_credits"]!.GetValue<string>().Should().Be("0xB2");
        anchors.ContainsKey("null_ignored").Should().BeFalse();
    }

    [Fact]
    public void ReadContextAnchors_ShouldMergeJsonElementLegacyAnchors()
    {
        using var anchorDoc = JsonDocument.Parse("""{"toggle_ai":"0xC3","set_unit_cap":"0xD4"}""");
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["anchors"] = anchorDoc.RootElement
        };

        var anchors = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context);

        anchors["toggle_ai"]!.GetValue<string>().Should().Be("0xC3");
        anchors["set_unit_cap"]!.GetValue<string>().Should().Be("0xD4");
    }

    [Fact]
    public void ReadContextAnchors_ShouldMergeStringPairLegacyAnchors_AndIgnoreWhitespaceValues()
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["anchors"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["freeze_timer"] = "0xE5",
                ["blank_ignored"] = " "
            }
        };

        var anchors = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(context);

        anchors.ContainsKey("freeze_timer").Should().BeTrue();
        anchors["freeze_timer"]!.GetValue<string>().Should().Be("0xE5");
        anchors.ContainsKey("blank_ignored").Should().BeFalse();
    }

    [Fact]
    public void ReadContextAnchors_ShouldMergeSerializedLegacyAnchors_AndIgnoreInvalidJson()
    {
        var validContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["anchors"] = """{"credits":"0xF6","blank":" "}"""
        };
        var invalidContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["anchors"] = "{not-valid-json}"
        };

        var validAnchors = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(validContext);
        var invalidAnchors = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(invalidContext);

        validAnchors.ContainsKey("credits").Should().BeTrue();
        validAnchors["credits"]!.GetValue<string>().Should().Be("0xF6");
        validAnchors.ContainsKey("blank").Should().BeFalse();
        invalidAnchors.Should().BeEmpty();
    }
}
