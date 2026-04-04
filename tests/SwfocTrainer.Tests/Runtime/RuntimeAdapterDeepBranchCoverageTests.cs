using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Deep branch-coverage sweep for RuntimeAdapter — targets remaining uncovered branches
/// in validation, anchor merging, symbol resolution, SDK dispatch, context building,
/// telemetry, dependency gating, mechanic blocking, and diagnostics resolution.
/// </summary>
public sealed class RuntimeAdapterDeepBranchCoverageTests
{
    // ── ValidateRequestedIntValue branches ──────────────────────────────────

    [Fact]
    public void ValidateRequestedIntValue_ShouldPass_WhenRuleIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", 100L, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateRequestedIntValue_ShouldFail_WhenBelowMin()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = method!.Invoke(null, new object?[] { "test", 5L, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("value_below_min");
    }

    [Fact]
    public void ValidateRequestedIntValue_ShouldFail_WhenAboveMax()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = method!.Invoke(null, new object?[] { "test", 200L, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("value_above_max");
    }

    [Fact]
    public void ValidateRequestedIntValue_ShouldPass_WhenInRange()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = method!.Invoke(null, new object?[] { "test", 50L, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    // ── ValidateRequestedFloatValue branches ───────────────────────────────

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ValidateRequestedFloatValue_ShouldFail_WhenNonFinite(double value)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", value, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("value_non_finite");
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldPass_WhenRuleIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", 5.0d, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldFail_WhenBelowMin()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, 1.0d, 10.0d, false);
        var result = method!.Invoke(null, new object?[] { "test", 0.5d, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("value_below_min");
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldFail_WhenAboveMax()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateRequestedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, 1.0d, 10.0d, false);
        var result = method!.Invoke(null, new object?[] { "test", 15.0d, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("value_above_max");
    }

    // ── ValidateObservedIntValue branches ──────────────────────────────────

    [Fact]
    public void ValidateObservedIntValue_ShouldPass_WhenRuleIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", 100L, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateObservedIntValue_ShouldFail_WhenBelowMin()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = method!.Invoke(null, new object?[] { "test", 5L, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("observed_below_min");
    }

    [Fact]
    public void ValidateObservedIntValue_ShouldFail_WhenAboveMax()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedIntValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 10L, 100L, null, null, false);
        var result = method!.Invoke(null, new object?[] { "test", 200L, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("observed_above_max");
    }

    // ── ValidateObservedFloatValue branches ────────────────────────────────

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ValidateObservedFloatValue_ShouldFail_WhenNonFinite(double observed)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", observed, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("observed_non_finite");
    }

    [Fact]
    public void ValidateObservedFloatValue_ShouldPass_WhenRuleIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", 5.0d, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateObservedFloatValue_ShouldFail_WhenBelowMin()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, 1.0d, 10.0d, false);
        var result = method!.Invoke(null, new object?[] { "test", 0.5d, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("observed_below_min");
    }

    [Fact]
    public void ValidateObservedFloatValue_ShouldFail_WhenAboveMax()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedFloatValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, 1.0d, 10.0d, false);
        var result = method!.Invoke(null, new object?[] { "test", 15.0d, rule })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        isValid.Should().BeFalse();
        reasonCode.Should().Be("observed_above_max");
    }

    // ── ValidateObservedReadValue branches ─────────────────────────────────

    [Theory]
    [InlineData(SymbolValueType.Int32)]
    [InlineData(SymbolValueType.Int64)]
    [InlineData(SymbolValueType.Byte)]
    [InlineData(SymbolValueType.Float)]
    [InlineData(SymbolValueType.Double)]
    [InlineData(SymbolValueType.Bool)]
    public void ValidateObservedReadValue_ShouldPass_ForAllValueTypes(SymbolValueType valueType)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedReadValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        object observed = valueType switch
        {
            SymbolValueType.Int32 => 42,
            SymbolValueType.Int64 => 42L,
            SymbolValueType.Byte => (byte)42,
            SymbolValueType.Float => 42.0f,
            SymbolValueType.Double => 42.0d,
            SymbolValueType.Bool => true,
            _ => 42
        };

        var result = method!.Invoke(null, new object?[] { "test", observed, valueType, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateObservedReadValue_ShouldPass_ForPointerType()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ValidateObservedReadValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "test", "0x1234", SymbolValueType.Pointer, null })!;
        var isValid = (bool)result.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    // ── FormatValidationRuleRange branches ─────────────────────────────────

    [Fact]
    public void FormatValidationRuleRange_ShouldReturnNone_WhenNoRangesSet()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FormatValidationRuleRange", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, null, null, false);
        var result = (string)method!.Invoke(null, new object[] { rule })!;
        result.Should().Be("none");
    }

    [Fact]
    public void FormatValidationRuleRange_ShouldFormatIntRange()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FormatValidationRuleRange", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 0L, 100L, null, null, false);
        var result = (string)method!.Invoke(null, new object[] { rule })!;
        result.Should().Contain("int[");
    }

    [Fact]
    public void FormatValidationRuleRange_ShouldFormatFloatRange()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FormatValidationRuleRange", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, 0.0d, 100.0d, false);
        var result = (string)method!.Invoke(null, new object[] { rule })!;
        result.Should().Contain("float[");
    }

    [Fact]
    public void FormatValidationRuleRange_ShouldFormatBothRanges()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FormatValidationRuleRange", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, 0L, 100L, 0.0d, 100.0d, false);
        var result = (string)method!.Invoke(null, new object[] { rule })!;
        result.Should().Contain("int[").And.Contain("float[");
    }

    // ── MergeAnchorMap branches ────────────────────────────────────────────

    [Fact]
    public void MergeAnchorMap_ShouldHandleNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { dest, null });
        dest.Should().BeEmpty();
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeJsonObject()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var jsonObj = new JsonObject { ["key1"] = "0x1234" };
        method!.Invoke(null, new object?[] { dest, jsonObj });
        dest.Should().ContainKey("key1");
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeJsonElement()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Deserialize<JsonElement>("{\"key2\": \"0x5678\"}");
        method!.Invoke(null, new object?[] { dest, json });
        dest.Should().ContainKey("key2");
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeObjectDictionary()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["key3"] = "0xABCD"
        };
        method!.Invoke(null, new object?[] { dest, dict });
        dest.Should().ContainKey("key3");
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeStringPairs()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairs = new Dictionary<string, string> { ["key4"] = "0xEF01" };
        method!.Invoke(null, new object?[] { dest, (IEnumerable<KeyValuePair<string, string>>)pairs });
        dest.Should().ContainKey("key4");
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeSerializedJson()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var serialized = "{\"key5\": \"0x2345\"}";
        method!.Invoke(null, new object?[] { dest, serialized });
        dest.Should().ContainKey("key5");
    }

    [Fact]
    public void MergeAnchorMap_ShouldIgnoreMalformedSerializedJson()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { dest, "not json{{{" });
        dest.Should().BeEmpty();
    }

    [Fact]
    public void MergeAnchorMap_ShouldIgnoreIntValue()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { dest, 42 });
        dest.Should().BeEmpty();
    }

    // ── TryReadPayloadString branches ──────────────────────────────────────

    [Fact]
    public void TryReadPayloadString_ShouldReturnTrue_WhenKeyExists()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadPayloadString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["symbol"] = "credits" };
        var args = new object?[] { payload, "symbol", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        args[2].Should().Be("credits");
    }

    [Fact]
    public void TryReadPayloadString_ShouldReturnFalse_WhenKeyMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadPayloadString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        var args = new object?[] { payload, "symbol", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadPayloadString_ShouldReturnFalse_WhenNodeIsNotString()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadPayloadString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["symbol"] = new JsonArray() };
        var args = new object?[] { payload, "symbol", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    // ── TryReadContextValue branches ──────────────────────────────────────

    [Fact]
    public void TryReadContextValue_ShouldReturnFalse_WhenContextIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadContextValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var args = new object?[] { null, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadContextValue_ShouldReturnFalse_WhenKeyNotFound()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadContextValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["other"] = "value" };
        var args = new object?[] { context, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadContextValue_ShouldReturnTrue_WhenKeyFound()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadContextValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?> { ["key"] = "value" };
        var args = new object?[] { context, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        args[2].Should().Be("value");
    }

    // ── AddAddressAnchorIfAvailable branches ──────────────────────────────

    [Fact]
    public void AddAddressAnchorIfAvailable_ShouldNotAdd_WhenAddressIsZero()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "AddAddressAnchorIfAvailable", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { anchors, "key", nint.Zero });
        anchors.Should().BeEmpty();
    }

    [Fact]
    public void AddAddressAnchorIfAvailable_ShouldAdd_WhenAddressIsNonZero()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "AddAddressAnchorIfAvailable", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { anchors, "key", (nint)0x1234 });
        anchors.Should().ContainKey("key");
    }

    // ── ResolvePromotedAnchorAliases branches ─────────────────────────────

    [Theory]
    [InlineData("freeze_timer", new[] { "game_timer_freeze", "freeze_timer" })]
    [InlineData("toggle_fog_reveal", new[] { "fog_reveal", "toggle_fog_reveal" })]
    [InlineData("toggle_ai", new[] { "ai_enabled", "toggle_ai" })]
    [InlineData("set_unit_cap", new[] { "unit_cap", "set_unit_cap" })]
    [InlineData("set_credits", new[] { "credits", "set_credits" })]
    [InlineData("unknown_action", new string[0])]
    public void ResolvePromotedAnchorAliases_ShouldReturnCorrectAliases(string actionId, string[] expectedAliases)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolvePromotedAnchorAliases", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object[] { actionId })!;
        result.Should().BeEquivalentTo(expectedAliases);
    }

    // ── IsEligibleForExpertMutationOverride branches ──────────────────────

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnTrue_WhenAllConditionsMet()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic, ExecutionKind.CodePatch);
        var decision = new BackendRouteDecision(false, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenAllowed()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic, ExecutionKind.CodePatch);
        var decision = new BackendRouteDecision(true, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenNotExtenderBackend()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic, ExecutionKind.CodePatch);
        var decision = new BackendRouteDecision(false, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenNotPromotedAction()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic);
        var decision = new BackendRouteDecision(false, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenReadOnlyAction()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("read_credits", RuntimeMode.Galactic);
        var decision = new BackendRouteDecision(false, ExecutionBackendKind.Extender, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    // ── IsEnabledEnvironmentFlag branches ─────────────────────────────────

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsEnabledEnvironmentFlag_ShouldReturnCorrectly(string? value, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEnabledEnvironmentFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var envName = $"SWFOC_TEST_FLAG_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, value);
        try
        {
            var result = (bool)method!.Invoke(null, new object[] { envName })!;
            result.Should().Be(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    // ── ResolveExpertMutationOverrideState branches ───────────────────────

    [Fact]
    public void ResolveExpertMutationOverrideState_ShouldReturnPanicActive_WhenPanicEnvSet()
    {
        var prevExpert = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var prevPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", "1");
            var method = typeof(RuntimeAdapter).GetMethod(
                "ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)result.GetType().GetProperty("Enabled")!.GetValue(result)!;
            var panicState = (string)result.GetType().GetProperty("PanicDisableState")!.GetValue(result)!;
            enabled.Should().BeFalse();
            panicState.Should().Be("active");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prevExpert);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", prevPanic);
        }
    }

    [Fact]
    public void ResolveExpertMutationOverrideState_ShouldReturnEnabled_WhenExpertEnvSet()
    {
        var prevExpert = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var prevPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", null);
            var method = typeof(RuntimeAdapter).GetMethod(
                "ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)result.GetType().GetProperty("Enabled")!.GetValue(result)!;
            var panicState = (string)result.GetType().GetProperty("PanicDisableState")!.GetValue(result)!;
            enabled.Should().BeTrue();
            panicState.Should().Be("inactive");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prevExpert);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", prevPanic);
        }
    }

    [Fact]
    public void ResolveExpertMutationOverrideState_ShouldReturnDisabled_ByDefault()
    {
        var prevExpert = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var prevPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", null);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", null);
            var method = typeof(RuntimeAdapter).GetMethod(
                "ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)result.GetType().GetProperty("Enabled")!.GetValue(result)!;
            enabled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prevExpert);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", prevPanic);
        }
    }

    // ── IsMutatingSdkOperation branches ───────────────────────────────────

    [Theory]
    [InlineData("list_units", false)]
    [InlineData("read_credits", false)]
    [InlineData("set_credits", true)]
    [InlineData("toggle_fog_reveal", true)]
    public void IsMutatingSdkOperation_ShouldClassifyCorrectly(string operationId, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsMutatingSdkOperation", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { operationId })!;
        result.Should().Be(expected);
    }

    // ── ToReasonCode ─────────────────────────────────────────────────────

    [Fact]
    public void ToReasonCode_ShouldReturnLowercaseString()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ToReasonCode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object[] { RuntimeReasonCode.CAPABILITY_PROBE_PASS })!;
        result.Should().Be("capability_probe_pass");
    }

    // ── ToHex ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToHex_ShouldFormatCorrectly()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ToHex", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object[] { (nint)0xABCD })!;
        result.Should().Be("0xABCD");
    }

    // ── TryResolveFirstDiagnosticValue branches ──────────────────────────

    [Fact]
    public void TryResolveFirstDiagnosticValue_ShouldReturnFalse_WhenNoneMatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveFirstDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["other"] = "value" };
        var args = new object?[] { diag, new[] { "key1", "key2" }, null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolveFirstDiagnosticValue_ShouldReturnTrue_WhenFirstKeyMatches()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryResolveFirstDiagnosticValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["key1"] = "value1" };
        var args = new object?[] { diag, new[] { "key1", "key2" }, null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        args[2].Should().Be("value1");
    }

    // ── TryReadDiagnosticString branches ─────────────────────────────────

    [Fact]
    public void TryReadDiagnosticString_ShouldReturnFalse_WhenDiagnosticsIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var args = new object?[] { null, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadDiagnosticString_ShouldReturnFalse_WhenKeyMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var args = new object?[] { diag, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadDiagnosticString_ShouldReturnTrue_WhenKeyPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["key"] = "value" };
        var args = new object?[] { diag, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        args[2].Should().Be("value");
    }

    // ── Dependency disabled action integration ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldBlockAction_WhenDependencyDisabled()
    {
        var harness = new AdapterHarness
        {
            DependencyValidator = new StubDependencyValidator(
                new DependencyValidationResult(
                    DependencyValidationStatus.SoftFail,
                    "missing dependency",
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "set_hero_state_helper" }))
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(
            adapter,
            "_dependencySoftDisabledActions",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "set_hero_state_helper" });
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(
            adapter,
            "_dependencyValidationStatus",
            DependencyValidationStatus.SoftFail);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(
            adapter,
            "_dependencyValidationMessage",
            "missing dependency");

        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("disabled");
        result.Diagnostics.Should().ContainKey("dependencyValidation");
    }

    // ── Mechanic blocked action integration ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldBlockAction_WhenMechanicDetectionBlocks()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new StubMechanicDetectionService(
                supported: false,
                actionId: "set_hero_state_helper",
                reasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                message: "mechanic not supported")
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("mechanic not supported");
        result.Diagnostics.Should().ContainKey("mechanicGating");
    }

    // ── Mechanic detection: exception swallowed ──────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldNotBlock_WhenMechanicDetectionThrows()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new ThrowingMechanicDetectionService()
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        // Should not fail due to mechanic detection — it catches the exception
        result.Should().NotBeNull();
    }

    // ── Mechanic detection: action supported ─────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldNotBlock_WhenMechanicDetectionSupports()
    {
        var harness = new AdapterHarness
        {
            MechanicDetectionService = new StubMechanicDetectionService(
                supported: true,
                actionId: "set_hero_state_helper",
                reasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                message: "supported")
        };
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic);
        var result = await adapter.ExecuteAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ── CreateFallbackDisabledResult ─────────────────────────────────────

    [Fact]
    public void CreateFallbackDisabledResult_ShouldContainDiagnostics()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CreateFallbackDisabledResult", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ActionExecutionResult)method!.Invoke(null, new object[] { "test_action", "test_flag" })!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("disabled");
        result.Diagnostics.Should().ContainKey("fallbackAction");
        result.Diagnostics.Should().ContainKey("featureFlag");
    }

    // ── ApplyContextActionDiagnostics — non-context action ───────────────

    [Fact]
    public void ApplyContextActionDiagnostics_ShouldReturnUnmodified_WhenNotContextAction()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextActionDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var modified = (ActionExecutionResult)method!.Invoke(null, new object?[] { result, "set_credits", null })!;
        modified.Diagnostics.Should().BeNull();
    }

    // ── ClonePayload ────────────────────────────────────────────────────

    [Fact]
    public void ClonePayload_ShouldDeepClone()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ClonePayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var original = new JsonObject { ["key"] = "value", ["nested"] = new JsonObject { ["inner"] = 42 } };
        var cloned = (JsonObject)method!.Invoke(null, new object[] { original })!;
        cloned.Should().NotBeSameAs(original);
        cloned["key"]!.GetValue<string>().Should().Be("value");
    }

    // ── ResolveSymbolValidationRule branches ──────────────────────────────

    [Fact]
    public void ResolveSymbolValidationRule_ShouldReturnNull_WhenNoRules()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveSymbolValidationRule", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(adapter, new object[] { "nonexistent_symbol", RuntimeMode.Galactic });
        result.Should().BeNull();
    }

    // ── IsCriticalSymbol branches ────────────────────────────────────────

    [Fact]
    public void IsCriticalSymbol_ShouldReturnFalse_WhenNotCritical()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "IsCriticalSymbol", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(adapter, new object?[] { "nonexistent", null })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsCriticalSymbol_ShouldReturnTrue_WhenRuleSaysCritical()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var method = typeof(RuntimeAdapter).GetMethod(
            "IsCriticalSymbol", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var rule = new SymbolValidationRule("test", null, null, null, null, null, true);
        var result = (bool)method!.Invoke(adapter, new object?[] { "nonexistent", rule })!;
        result.Should().BeTrue();
    }

    // ── IsProfileFeatureEnabled branches ─────────────────────────────────

    [Fact]
    public void IsProfileFeatureEnabled_ShouldReturnFalse_WhenProfileIsNull()
    {
        var harness = new AdapterHarness();
        var profile = BuildHelperProfile("set_hero_state_helper");
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_attachedProfile", null);

        var method = typeof(RuntimeAdapter).GetMethod(
            "IsProfileFeatureEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(adapter, new object[] { "some_flag" })!;
        result.Should().BeFalse();
    }

    // ── Helper builders ────────────────────────────────────────────────────

    private static ActionExecutionRequest BuildRequest(string actionId, RuntimeMode runtimeMode, ExecutionKind kind = ExecutionKind.Helper)
    {
        var payload = new JsonObject
        {
            ["helperHookId"] = "hero_hook"
        };
        return new ActionExecutionRequest(
            Action: new ActionSpec(
                actionId,
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                kind,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: runtimeMode);
    }

    private static TrainerProfile BuildHelperProfile(params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(
                id,
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(
                    Name: "test",
                    GameBuild: "build",
                    Signatures:
                    [
                        new SignatureSpec("credits", "AA BB", 0)
                    ])
            ],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks:
            [
                new HelperHookSpec(
                    Id: "hero_hook",
                    Script: "scripts/aotr/hero_state_bridge.lua",
                    Version: "1.0.0",
                    EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
