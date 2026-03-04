using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterAdditionalCoverageTests
{
    private static readonly Type RuntimeAdapterType = typeof(RuntimeAdapter);

    [Theory]
    [InlineData(ExecutionKind.Helper, ExecutionBackendKind.Helper)]
    [InlineData(ExecutionKind.Save, ExecutionBackendKind.Save)]
    [InlineData(ExecutionKind.Sdk, ExecutionBackendKind.Memory)]
    public void ResolveLegacyOverrideBackend_ShouldMapKinds(ExecutionKind kind, ExecutionBackendKind expected)
    {
        var actual = (ExecutionBackendKind)InvokeStatic("ResolveLegacyOverrideBackend", kind)!;
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("read_credits", false)]
    [InlineData("list_units", false)]
    [InlineData("get_status", false)]
    [InlineData("set_credits", true)]
    public void IsMutatingActionId_ShouldApplyPrefixRules(string? actionId, bool expected)
    {
        var actual = (bool)InvokeStatic("IsMutatingActionId", actionId)!;
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("list_entities", false)]
    [InlineData("read_value", false)]
    [InlineData("spawn_entity", true)]
    public void IsMutatingSdkOperation_ShouldFallbackByPrefix(string operationId, bool expected)
    {
        var actual = (bool)InvokeStatic("IsMutatingSdkOperation", operationId)!;
        actual.Should().Be(expected);
    }

    [Fact]
    public void TryReadPayloadString_ShouldHandleMissingWhitespaceAndInvalidNode()
    {
        var payload = new JsonObject
        {
            ["good"] = "value",
            ["blank"] = "  ",
            ["invalid"] = new JsonObject()
        };

        TryInvokeOutString("TryReadPayloadString", payload, "good", out var valueGood).Should().BeTrue();
        valueGood.Should().Be("value");

        TryInvokeOutString("TryReadPayloadString", payload, "blank", out var valueBlank).Should().BeFalse();
        valueBlank.Should().Be("  ");

        TryInvokeOutString("TryReadPayloadString", payload, "missing", out var valueMissing).Should().BeFalse();
        valueMissing.Should().BeNull();

        TryInvokeOutString("TryReadPayloadString", payload, "invalid", out var valueInvalid).Should().BeFalse();
        valueInvalid.Should().BeNull();
    }

    [Fact]
    public void TryReadContextValue_ShouldHandleNullMissingAndPresent()
    {
        var context = new Dictionary<string, object?>
        {
            ["present"] = 42
        };

        TryInvokeOutObject("TryReadContextValue", null, "present", out var valueFromNull).Should().BeFalse();
        valueFromNull.Should().BeNull();

        TryInvokeOutObject("TryReadContextValue", context, "missing", out var missing).Should().BeFalse();
        missing.Should().BeNull();

        TryInvokeOutObject("TryReadContextValue", context, "present", out var present).Should().BeTrue();
        present.Should().Be(42);
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeSupportedRepresentations_AndIgnoreInvalid()
    {
        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        InvokeStatic("MergeAnchorMap", destination, new JsonObject
        {
            ["json_obj"] = "0x1",
            ["empty"] = ""
        });

        InvokeStatic("MergeAnchorMap", destination, JsonDocument.Parse("{\"json_element\":\"0x2\"}").RootElement);

        InvokeStatic("MergeAnchorMap", destination, new Dictionary<string, object?>
        {
            ["dict_obj"] = "0x3",
            ["dict_null"] = null
        });

        InvokeStatic("MergeAnchorMap", destination, new List<KeyValuePair<string, string>>
        {
            new("pair", "0x4"),
            new("pair_blank", " ")
        });

        InvokeStatic("MergeAnchorMap", destination, "{\"serialized\":\"0x5\",\"blank\":\"\"}");
        InvokeStatic("MergeAnchorMap", destination, "not-json");
        InvokeStatic("MergeAnchorMap", destination, null);

        destination["json_obj"].Should().Be("0x1");
        destination["json_element"].Should().Be("0x2");
        destination["dict_obj"].Should().Be("0x3");
        destination["pair"].Should().Be("0x4");
        destination["serialized"].Should().Be("0x5");
        destination.Should().NotContainKey("empty");
        destination.Should().NotContainKey("dict_null");
        destination.Should().NotContainKey("pair_blank");
        destination.Should().NotContainKey("blank");
    }

    [Fact]
    public void ResolvePromotedAnchorAliases_ShouldMapKnownActions()
    {
        ((string[])InvokeStatic("ResolvePromotedAnchorAliases", "freeze_timer")!)
            .Should().Equal("game_timer_freeze", "freeze_timer");

        ((string[])InvokeStatic("ResolvePromotedAnchorAliases", "toggle_fog_reveal_patch_fallback")!)
            .Should().ContainInOrder("fog_reveal", "toggle_fog_reveal_patch_fallback");

        ((string[])InvokeStatic("ResolvePromotedAnchorAliases", "unknown_action")!)
            .Should().BeEmpty();
    }

    [Fact]
    public void AddAddressAnchorIfAvailable_ShouldOnlyAddWhenNonZero()
    {
        var anchors = new Dictionary<string, string>();

        InvokeStatic("AddAddressAnchorIfAvailable", anchors, "zero", (nint)0);
        InvokeStatic("AddAddressAnchorIfAvailable", anchors, "nonzero", (nint)0x1234);

        anchors.Should().NotContainKey("zero");
        anchors["nonzero"].Should().Be("0x1234");
    }

    [Fact]
    public void AddAnchorIfNotEmpty_ShouldOnlyAddNonBlank()
    {
        var anchors = new Dictionary<string, string>();

        InvokeStatic("AddAnchorIfNotEmpty", anchors, "blank", " ");
        InvokeStatic("AddAnchorIfNotEmpty", anchors, "value", "0xABC");

        anchors.Should().NotContainKey("blank");
        anchors["value"].Should().Be("0xABC");
    }

    [Fact]
    public void ValidateRequestedIntValue_ShouldRespectRuleBounds()
    {
        var rule = new SymbolValidationRule("credits", IntMin: 1, IntMax: 10);

        var below = InvokeStatic("ValidateRequestedIntValue", "credits", 0L, rule)!;
        var above = InvokeStatic("ValidateRequestedIntValue", "credits", 11L, rule)!;
        var pass = InvokeStatic("ValidateRequestedIntValue", "credits", 5L, rule)!;
        var noRule = InvokeStatic("ValidateRequestedIntValue", "credits", 999L, null)!;

        ReadProperty<bool>(below, "IsValid").Should().BeFalse();
        ReadProperty<string>(below, "ReasonCode").Should().Be("value_below_min");

        ReadProperty<bool>(above, "IsValid").Should().BeFalse();
        ReadProperty<string>(above, "ReasonCode").Should().Be("value_above_max");

        ReadProperty<bool>(pass, "IsValid").Should().BeTrue();
        ReadProperty<bool>(noRule, "IsValid").Should().BeTrue();
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldRejectNonFinite_AndRespectBounds()
    {
        var rule = new SymbolValidationRule("speed", FloatMin: 1.0, FloatMax: 5.0);

        var nonFiniteNan = InvokeStatic("ValidateRequestedFloatValue", "speed", double.NaN, rule)!;
        var nonFiniteInf = InvokeStatic("ValidateRequestedFloatValue", "speed", double.PositiveInfinity, rule)!;
        var below = InvokeStatic("ValidateRequestedFloatValue", "speed", 0.5d, rule)!;
        var above = InvokeStatic("ValidateRequestedFloatValue", "speed", 6.0d, rule)!;
        var pass = InvokeStatic("ValidateRequestedFloatValue", "speed", 3.0d, rule)!;

        ReadProperty<string>(nonFiniteNan, "ReasonCode").Should().Be("value_non_finite");
        ReadProperty<string>(nonFiniteInf, "ReasonCode").Should().Be("value_non_finite");
        ReadProperty<string>(below, "ReasonCode").Should().Be("value_below_min");
        ReadProperty<string>(above, "ReasonCode").Should().Be("value_above_max");
        ReadProperty<bool>(pass, "IsValid").Should().BeTrue();
    }

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = RuntimeAdapterType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected private static method '{methodName}'");
        return method!.Invoke(null, args);
    }

    private static bool TryInvokeOutString(string methodName, JsonObject payload, string key, out string? value)
    {
        var method = RuntimeAdapterType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var args = new object?[] { payload, key, null };
        var result = (bool)method!.Invoke(null, args)!;
        value = (string?)args[2];
        return result;
    }

    private static bool TryInvokeOutObject(
        string methodName,
        IReadOnlyDictionary<string, object?>? context,
        string key,
        out object? value)
    {
        var method = RuntimeAdapterType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var args = new object?[] { context, key, null };
        var result = (bool)method!.Invoke(null, args)!;
        value = args[2];
        return result;
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return (T)property!.GetValue(instance)!;
    }
}
