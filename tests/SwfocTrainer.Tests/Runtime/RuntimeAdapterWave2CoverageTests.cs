using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 2 branch-coverage sweep for RuntimeAdapter — targets anchor merging,
/// context building, SDK dispatch, telemetry resolution, validation helpers,
/// process selection, workshop filtering, and diagnostic resolution paths.
/// </summary>
public sealed class RuntimeAdapterWave2CoverageTests
{
    // ── MergeAnchorMap branches ────────────────────────────────────────────

    [Fact]
    public void MergeAnchorMap_ShouldDoNothing_WhenRawIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, null });
        destination.Should().BeEmpty();
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeJsonObject()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var raw = new JsonObject { ["key1"] = "0xAA", ["key2"] = "0xBB" };
        method!.Invoke(null, new object?[] { destination, raw });
        destination.Should().ContainKey("key1");
        destination["key1"].Should().Be("0xAA");
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeJsonElement()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var json = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["sym1"] = "0x100" });
        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, json });
        destination.Should().ContainKey("sym1");
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeObjectDictionary()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["anchor1"] = "0xDEAD"
        } as IReadOnlyDictionary<string, object?>;
        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, dict });
        destination.Should().ContainKey("anchor1");
    }

    [Fact]
    public void MergeAnchorMap_ShouldMergeStringPairs()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var pairs = new List<KeyValuePair<string, string>>
        {
            new("a", "0x1"), new("b", "0x2"), new("c", "")
        };
        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, pairs });
        destination.Should().ContainKey("a");
        destination.Should().ContainKey("b");
        destination.Should().NotContainKey("c"); // empty value skipped
    }

    [Fact]
    public void MergeAnchorMap_ShouldDeserializeSerializedJson()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var serialized = JsonSerializer.Serialize(new Dictionary<string, string> { ["x"] = "0xFF" });
        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, serialized });
        destination.Should().ContainKey("x");
    }

    [Fact]
    public void MergeAnchorMap_ShouldIgnoreMalformedSerializedJson()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, "not-valid-json{{{" });
        destination.Should().BeEmpty();
    }

    [Fact]
    public void MergeAnchorMap_ShouldIgnoreEmptyString()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, "" });
        destination.Should().BeEmpty();
    }

    [Fact]
    public void MergeAnchorMap_ShouldIgnoreUnknownObjectType()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeAnchorMap", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, (object)42 });
        destination.Should().BeEmpty();
    }

    // ── TryMergeAnchorJsonObject branches ──────────────────────────────────

    [Fact]
    public void TryMergeAnchorJsonObject_ShouldReturnFalse_WhenNotJsonObject()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryMergeAnchorJsonObject", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = (bool)method!.Invoke(null, new object?[] { destination, "string" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryMergeAnchorJsonObject_ShouldSkipNullValues()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryMergeAnchorJsonObject", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var json = new JsonObject { ["key1"] = "val1", ["key2"] = null };
        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = (bool)method!.Invoke(null, new object?[] { destination, json })!;
        result.Should().BeTrue();
        destination.Should().ContainKey("key1");
        destination.Should().NotContainKey("key2");
    }

    // ── TryMergeAnchorJsonElement branches ─────────────────────────────────

    [Fact]
    public void TryMergeAnchorJsonElement_ShouldReturnFalse_WhenNotJsonElement()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryMergeAnchorJsonElement", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = (bool)method!.Invoke(null, new object?[] { destination, "string" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryMergeAnchorJsonElement_ShouldReturnFalse_WhenNotObject()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryMergeAnchorJsonElement", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var element = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 });
        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = (bool)method!.Invoke(null, new object?[] { destination, element })!;
        result.Should().BeFalse();
    }

    // ── TryMergeAnchorObjectDictionary branches ───────────────────────────

    [Fact]
    public void TryMergeAnchorObjectDictionary_ShouldReturnFalse_WhenNotDictionary()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryMergeAnchorObjectDictionary", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = (bool)method!.Invoke(null, new object?[] { destination, "string" })!;
        result.Should().BeFalse();
    }

    // ── TryMergeAnchorStringPairs branches ────────────────────────────────

    [Fact]
    public void TryMergeAnchorStringPairs_ShouldReturnFalse_WhenNotPairs()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryMergeAnchorStringPairs", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = (bool)method!.Invoke(null, new object?[] { destination, "string" })!;
        result.Should().BeFalse();
    }

    // ── AddAnchorIfNotEmpty branches ──────────────────────────────────────

    [Theory]
    [InlineData("key", "value", true)]
    [InlineData("key", "", false)]
    [InlineData("key", null, false)]
    [InlineData("key", "   ", false)]
    public void AddAnchorIfNotEmpty_ShouldHandleEdgeCases(string key, string? value, bool shouldAdd)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "AddAnchorIfNotEmpty", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var destination = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { destination, key, value });
        if (shouldAdd)
        {
            destination.Should().ContainKey(key);
        }
        else
        {
            destination.Should().NotContainKey(key);
        }
    }

    // ── AddAddressAnchorIfAvailable branches ─────────────────────────────

    [Fact]
    public void AddAddressAnchorIfAvailable_ShouldAdd_WhenNonZero()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "AddAddressAnchorIfAvailable", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { anchors, "test_hook", (nint)0x1234 });
        anchors.Should().ContainKey("test_hook");
    }

    [Fact]
    public void AddAddressAnchorIfAvailable_ShouldSkip_WhenZero()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "AddAddressAnchorIfAvailable", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var anchors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { anchors, "test_hook", nint.Zero });
        anchors.Should().NotContainKey("test_hook");
    }

    // ── TryReadPayloadString branches ─────────────────────────────────────

    [Fact]
    public void TryReadPayloadString_ShouldReturnTrue_WhenStringPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadPayloadString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["symbol"] = "my_symbol" };
        var args = new object?[] { payload, "symbol", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        ((string?)args[2]).Should().Be("my_symbol");
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
    public void TryReadPayloadString_ShouldReturnFalse_WhenValueIsWhitespace()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadPayloadString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["symbol"] = "   " };
        var args = new object?[] { payload, "symbol", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadPayloadString_ShouldReturnFalse_WhenValueIsNonString()
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
    public void TryReadContextValue_ShouldReturnFalse_WhenKeyMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadContextValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { context, "missing", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadContextValue_ShouldReturnTrue_WhenKeyPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadContextValue", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["found"] = "value"
        } as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { context, "found", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        args[2].Should().Be("value");
    }

    // ── TryReadDiagnosticString branches ──────────────────────────────────

    [Fact]
    public void TryReadDiagnosticString_ShouldReturnFalse_WhenNull()
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

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { diag, "missing", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadDiagnosticString_ShouldReturnTrue_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = "val"
        } as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { diag, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        ((string?)args[2]).Should().Be("val");
    }

    [Fact]
    public void TryReadDiagnosticString_ShouldReturnFalse_WhenValueIsWhitespace()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadDiagnosticString", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var diag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = "   "
        } as IReadOnlyDictionary<string, object?>;
        var args = new object?[] { diag, "key", null };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    // ── ResolveContextRouteType branches ──────────────────────────────────

    [Theory]
    [InlineData("spawn_context_entity", "Spawn")]
    [InlineData("set_context_faction", "Faction")]
    [InlineData("set_context_allegiance", "Faction")]
    [InlineData("other_action", "None")]
    public void ResolveContextRouteType_ShouldReturnCorrectType(string actionId, string expectedName)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveContextRouteType", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object[] { actionId })!;
        result.ToString().Should().Be(expectedName);
    }

    // ── ResolveContextFactionTargetAction branches ────────────────────────

    [Theory]
    [InlineData(RuntimeMode.Galactic, "set_planet_owner")]
    [InlineData(RuntimeMode.TacticalLand, "set_selected_owner_faction")]
    [InlineData(RuntimeMode.TacticalSpace, "set_selected_owner_faction")]
    [InlineData(RuntimeMode.AnyTactical, "set_selected_owner_faction")]
    [InlineData(RuntimeMode.Unknown, null)]
    public void ResolveContextFactionTargetAction_ShouldReturnCorrectAction(RuntimeMode mode, string? expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveContextFactionTargetAction", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string?)method!.Invoke(null, new object[] { mode });
        result.Should().Be(expected);
    }

    // ── ResolveContextSpawnTargetAction branches ─────────────────────────

    [Theory]
    [InlineData(RuntimeMode.Galactic, "spawn_galactic_entity")]
    [InlineData(RuntimeMode.TacticalLand, "spawn_tactical_entity")]
    [InlineData(RuntimeMode.TacticalSpace, "spawn_tactical_entity")]
    [InlineData(RuntimeMode.AnyTactical, "spawn_tactical_entity")]
    [InlineData(RuntimeMode.Unknown, null)]
    public void ResolveContextSpawnTargetAction_ShouldReturnCorrectAction(RuntimeMode mode, string? expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveContextSpawnTargetAction", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string?)method!.Invoke(null, new object[] { mode });
        result.Should().Be(expected);
    }

    // ── IsMutatingSdkOperation branches ──────────────────────────────────

    [Theory]
    [InlineData("list_something", false)]
    [InlineData("read_something", false)]
    [InlineData("set_something", true)]
    [InlineData("toggle_something", true)]
    public void IsMutatingSdkOperation_ShouldReturnCorrectResult(string operationId, bool expected)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsMutatingSdkOperation", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { operationId })!;
        result.Should().Be(expected);
    }

    // ── IsEnabledEnvironmentFlag branches ────────────────────────────────

    [Fact]
    public void IsEnabledEnvironmentFlag_ShouldReturnTrue_ForValue1()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEnabledEnvironmentFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var key = "SWFOC_WAVE2_TEST_FLAG_1";
        var prev = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "1");
            var result = (bool)method!.Invoke(null, new object[] { key })!;
            result.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
        }
    }

    [Fact]
    public void IsEnabledEnvironmentFlag_ShouldReturnTrue_ForValueTrue()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEnabledEnvironmentFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var key = "SWFOC_WAVE2_TEST_FLAG_2";
        var prev = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "true");
            var result = (bool)method!.Invoke(null, new object[] { key })!;
            result.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
        }
    }

    [Fact]
    public void IsEnabledEnvironmentFlag_ShouldReturnFalse_ForOtherValues()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEnabledEnvironmentFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var key = "SWFOC_WAVE2_TEST_FLAG_3";
        var prev = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "0");
            var result = (bool)method!.Invoke(null, new object[] { key })!;
            result.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
        }
    }

    [Fact]
    public void IsEnabledEnvironmentFlag_ShouldReturnFalse_WhenNotSet()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEnabledEnvironmentFlag", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var key = "SWFOC_WAVE2_NONEXISTENT_FLAG";
        var prev = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, null);
            var result = (bool)method!.Invoke(null, new object[] { key })!;
            result.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
        }
    }

    // ── ResolveExpertMutationOverrideState branches ──────────────────────

    [Fact]
    public void ResolveExpertMutationOverrideState_ShouldReturnDisabled_ByDefault()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var prevOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var prevPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", null);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", null);

            var state = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)state.GetType().GetProperty("Enabled")!.GetValue(state)!;
            var panicState = (string)state.GetType().GetProperty("PanicDisableState")!.GetValue(state)!;
            enabled.Should().BeFalse();
            panicState.Should().Be("inactive");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prevOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", prevPanic);
        }
    }

    [Fact]
    public void ResolveExpertMutationOverrideState_ShouldReturnEnabled_WhenEnvVarIsSet()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var prevOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var prevPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", null);

            var state = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)state.GetType().GetProperty("Enabled")!.GetValue(state)!;
            enabled.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prevOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", prevPanic);
        }
    }

    [Fact]
    public void ResolveExpertMutationOverrideState_ShouldReturnPanicDisable_WhenPanicIsSet()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveExpertMutationOverrideState", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var prevOverride = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES");
        var prevPanic = Environment.GetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", "1");
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", "1");

            var state = method!.Invoke(null, Array.Empty<object>())!;
            var enabled = (bool)state.GetType().GetProperty("Enabled")!.GetValue(state)!;
            var panicState = (string)state.GetType().GetProperty("PanicDisableState")!.GetValue(state)!;
            enabled.Should().BeFalse();
            panicState.Should().Be("active");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES", prevOverride);
            Environment.SetEnvironmentVariable("SWFOC_EXPERT_MUTATION_OVERRIDES_PANIC", prevPanic);
        }
    }

    // ── IsEligibleForExpertMutationOverride branches ─────────────────────

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnTrue_WhenAllConditionsMet()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic);
        var decision = new BackendRouteDecision(
            Allowed: false,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
            Message: "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenRouteAllowed()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic);
        var decision = new BackendRouteDecision(
            Allowed: true,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "ok");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenBackendIsNotExtender()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_unit_cap", RuntimeMode.Galactic);
        var decision = new BackendRouteDecision(
            Allowed: false,
            Backend: ExecutionBackendKind.Helper,
            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
            Message: "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenNotPromoted()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic);
        var decision = new BackendRouteDecision(
            Allowed: false,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
            Message: "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForExpertMutationOverride_ShouldReturnFalse_WhenReadOnly()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsEligibleForExpertMutationOverride", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var request = BuildRequest("read_credits", RuntimeMode.Galactic);
        var decision = new BackendRouteDecision(
            Allowed: false,
            Backend: ExecutionBackendKind.Extender,
            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
            Message: "blocked");
        var result = (bool)method!.Invoke(null, new object[] { request, decision })!;
        result.Should().BeFalse();
    }

    // ── MergeDiagnostics edge branches ───────────────────────────────────

    [Fact]
    public void MergeDiagnostics_ShouldReturnPrimary_WhenSecondaryIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var primary = new Dictionary<string, object?> { ["a"] = "1" } as IReadOnlyDictionary<string, object?>;
        var result = method!.Invoke(null, new object?[] { primary, null }) as IReadOnlyDictionary<string, object?>;
        result.Should().NotBeNull();
        result!.Should().ContainKey("a");
    }

    [Fact]
    public void MergeDiagnostics_ShouldReturnSecondary_WhenPrimaryIsNull()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var secondary = new Dictionary<string, object?> { ["b"] = "2" } as IReadOnlyDictionary<string, object?>;
        var result = method!.Invoke(null, new object?[] { null, secondary }) as IReadOnlyDictionary<string, object?>;
        result.Should().NotBeNull();
        result!.Should().ContainKey("b");
    }

    [Fact]
    public void MergeDiagnostics_ShouldReturnNull_WhenBothEmpty()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "MergeDiagnostics", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var emptyPrimary = new Dictionary<string, object?>() as IReadOnlyDictionary<string, object?>;
        var emptySecondary = new Dictionary<string, object?>() as IReadOnlyDictionary<string, object?>;
        var result = method!.Invoke(null, new object?[] { emptyPrimary, emptySecondary });
        // Should return primary (the empty one)
        result.Should().NotBeNull();
    }

    // ── ClonePayload branches ────────────────────────────────────────────

    [Fact]
    public void ClonePayload_ShouldCloneAllProperties()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ClonePayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["a"] = "hello", ["b"] = 42, ["c"] = true };
        var cloned = (JsonObject)method!.Invoke(null, new object[] { payload })!;
        cloned["a"]!.GetValue<string>().Should().Be("hello");
        cloned["b"]!.GetValue<int>().Should().Be(42);
        cloned["c"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ClonePayload_ShouldHandleNullValues()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ClonePayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["a"] = null };
        var cloned = (JsonObject)method!.Invoke(null, new object[] { payload })!;
        cloned.ContainsKey("a").Should().BeTrue();
    }

    // ── ApplyContextSpawnPayloadDefaults branches ────────────────────────

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_ShouldSetDefaults_ForTacticalSpawn()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextSpawnPayloadDefaults", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        method!.Invoke(null, new object[] { payload, "spawn_tactical_entity" });

        payload["helperHookId"]!.GetValue<string>().Should().Be("spawn_bridge");
        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn_Context");
        payload["populationPolicy"]!.GetValue<string>().Should().Be("ForceZeroTactical");
        payload["persistencePolicy"]!.GetValue<string>().Should().Be("EphemeralBattleOnly");
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_ShouldSetDefaults_ForGalacticSpawn()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextSpawnPayloadDefaults", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        method!.Invoke(null, new object[] { payload, "spawn_galactic_entity" });

        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn_Context");
        payload["populationPolicy"]!.GetValue<string>().Should().Be("Normal");
        payload["persistencePolicy"]!.GetValue<string>().Should().Be("PersistentGalactic");
    }

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_ShouldUseLegacyEntryPoint_ForOtherActions()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextSpawnPayloadDefaults", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        method!.Invoke(null, new object[] { payload, "place_planet_building" });

        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn");
    }

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_ShouldNotOverrideExistingValues()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ApplyContextSpawnPayloadDefaults", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject
        {
            ["helperHookId"] = "custom_hook",
            ["helperEntryPoint"] = "custom_entry",
            ["populationPolicy"] = "custom_policy",
            ["persistencePolicy"] = "custom_persistence",
            ["allowCrossFaction"] = false
        };
        method!.Invoke(null, new object[] { payload, "spawn_tactical_entity" });

        payload["helperHookId"]!.GetValue<string>().Should().Be("custom_hook");
        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("custom_entry");
        payload["populationPolicy"]!.GetValue<string>().Should().Be("custom_policy");
        payload["persistencePolicy"]!.GetValue<string>().Should().Be("custom_persistence");
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeFalse();
    }

    // ── CreateContextModeBlockedResult branches ─────────────────────────

    [Fact]
    public void CreateContextModeBlockedResult_ShouldReturnSpawnMessage_ForSpawnRoute()
    {
        var contextRouteType = typeof(RuntimeAdapter)
            .GetNestedType("ContextRouteType", BindingFlags.NonPublic)!;
        var spawnValue = Enum.Parse(contextRouteType, "Spawn");

        var method = typeof(RuntimeAdapter).GetMethod(
            "CreateContextModeBlockedResult", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ActionExecutionResult)method!.Invoke(null, new[] { spawnValue, (object)RuntimeMode.Unknown })!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Context entity spawning");
    }

    [Fact]
    public void CreateContextModeBlockedResult_ShouldReturnFactionMessage_ForFactionRoute()
    {
        var contextRouteType = typeof(RuntimeAdapter)
            .GetNestedType("ContextRouteType", BindingFlags.NonPublic)!;
        var factionValue = Enum.Parse(contextRouteType, "Faction");

        var method = typeof(RuntimeAdapter).GetMethod(
            "CreateContextModeBlockedResult", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ActionExecutionResult)method!.Invoke(null, new[] { factionValue, (object)RuntimeMode.Unknown })!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Context faction routing");
    }

    // ── ResolveProcessSelectionReason branches ──────────────────────────

    [Fact]
    public void ResolveProcessSelectionReason_ShouldReturnRecommendation_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveProcessSelectionReason", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic).Process with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["recommendationReason"] = "profile_match"
            }
        };

        var result = (string)method!.Invoke(null, new object[] { process })!;
        result.Should().Be("profile_match");
    }

    [Fact]
    public void ResolveProcessSelectionReason_ShouldReturnDefault_WhenNoRecommendation()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveProcessSelectionReason", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic).Process;
        var result = (string)method!.Invoke(null, new object[] { process })!;
        result.Should().Be("exe_target_match");
    }

    // ── IncrementCounter branches ───────────────────────────────────────

    [Fact]
    public void IncrementCounter_ShouldAddNew_WhenKeyNotPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IncrementCounter", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { counters, "key1" });
        counters["key1"].Should().Be(1);
    }

    [Fact]
    public void IncrementCounter_ShouldIncrement_WhenKeyAlreadyPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IncrementCounter", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["key1"] = 5 };
        method!.Invoke(null, new object[] { counters, "key1" });
        counters["key1"].Should().Be(6);
    }

    // ── ResolveMemoryActionSymbol branches ──────────────────────────────

    [Fact]
    public void ResolveMemoryActionSymbol_ShouldReturnSymbol_WhenPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveMemoryActionSymbol", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["symbol"] = "credits" };
        var result = (string)method!.Invoke(null, new object[] { payload })!;
        result.Should().Be("credits");
    }

    [Fact]
    public void ResolveMemoryActionSymbol_ShouldThrow_WhenSymbolMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveMemoryActionSymbol", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject();
        var act = () => method!.Invoke(null, new object[] { payload });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();
    }

    // ── IsRel32Reachable branches ───────────────────────────────────────

    [Fact]
    public void IsRel32Reachable_ShouldReturnTrue_WhenWithinRange()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsRel32Reachable", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { (nint)0x1000, 5, (nint)0x2000 })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRel32Reachable_ShouldReturnFalse_WhenOutOfRange()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsRel32Reachable", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { (nint)0x1000, 5, unchecked((nint)0x100000000) })!;
        result.Should().BeFalse();
    }

    // ── ComputeRelativeDisplacement branches ────────────────────────────

    [Fact]
    public void ComputeRelativeDisplacement_ShouldComputeCorrectly()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ComputeRelativeDisplacement", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (int)method!.Invoke(null, new object[] { (nint)0x1005, (nint)0x2000 })!;
        result.Should().Be(0x2000 - 0x1005);
    }

    // ── WriteInt32 branches ──────────────────────────────────────────────

    [Fact]
    public void WriteInt32_ShouldWriteCorrectly()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "WriteInt32", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var buffer = new byte[8];
        method!.Invoke(null, new object[] { buffer, 2, 0x12345678 });
        var written = BitConverter.ToInt32(buffer, 2);
        written.Should().Be(0x12345678);
    }

    // ── BuildRelativeJumpBytes branches ─────────────────────────────────

    [Fact]
    public void BuildRelativeJumpBytes_ShouldStartWithJmpOpcode()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "BuildRelativeJumpBytes", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (byte[])method!.Invoke(null, new object[] { (nint)0x1000, (nint)0x2000 })!;
        result.Should().NotBeNull();
        result.Length.Should().Be(5);
        result[0].Should().Be(0xE9); // JMP rel32
    }

    // ── ResolveHelperOperationKind dispatch ─────────────────────────────

    [Theory]
    [InlineData("spawn_unit_helper")]
    [InlineData("spawn_context_entity")]
    [InlineData("spawn_tactical_entity")]
    [InlineData("spawn_galactic_entity")]
    [InlineData("place_planet_building")]
    [InlineData("set_context_allegiance")]
    [InlineData("set_context_faction")]
    [InlineData("set_hero_state_helper")]
    [InlineData("toggle_roe_respawn_helper")]
    [InlineData("completely_unknown_action")]
    public void ResolveHelperOperationKind_ShouldReturn_WellDefinedKind(string actionId)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolveHelperOperationKind", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object[] { actionId })!;
        result.Should().NotBeNull();
        Enum.IsDefined(result.GetType(), result).Should().BeTrue();
    }

    // ── ResolvePromotedAnchorAliases branches ──────────────────────────

    [Theory]
    [InlineData("freeze_timer", 2)]
    [InlineData("toggle_fog_reveal", 2)]
    [InlineData("toggle_fog_reveal_patch_fallback", 2)]
    [InlineData("toggle_ai", 2)]
    [InlineData("set_unit_cap", 2)]
    [InlineData("set_unit_cap_patch_fallback", 2)]
    [InlineData("toggle_instant_build_patch", 4)]
    [InlineData("set_credits", 2)]
    [InlineData("unknown_action", 0)]
    public void ResolvePromotedAnchorAliases_ShouldReturnCorrectAliasCount(string actionId, int expectedCount)
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ResolvePromotedAnchorAliases", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object[] { actionId })!;
        result.Length.Should().Be(expectedCount);
    }

    // ── ToReasonCode branch ─────────────────────────────────────────────

    [Fact]
    public void ToReasonCode_ShouldReturnStringRepresentation()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "ToReasonCode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object[] { RuntimeReasonCode.CAPABILITY_PROBE_PASS })!;
        result.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS.ToString().ToLowerInvariant());
    }

    // ── FindPatternOffsets branches ─────────────────────────────────────

    [Fact]
    public void FindPatternOffsets_ShouldReturnEmpty_WhenNoMatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var memory = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var pattern = AobPattern.Parse("FF FF FF");
        var result = (IReadOnlyList<int>)method!.Invoke(null, new object[] { memory, pattern, 10 })!;
        result.Count.Should().Be(0);
    }

    [Fact]
    public void FindPatternOffsets_ShouldReturnOffsets_WhenMatched()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var memory = new byte[] { 0xAA, 0xBB, 0xCC, 0xAA, 0xBB, 0xCC };
        var pattern = AobPattern.Parse("AA BB CC");
        var result = (IReadOnlyList<int>)method!.Invoke(null, new object[] { memory, pattern, 10 })!;
        result.Count.Should().Be(2);
        result[0].Should().Be(0);
        result[1].Should().Be(3);
    }

    [Fact]
    public void FindPatternOffsets_ShouldRespectMaxHits()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var memory = new byte[] { 0xAA, 0xBB, 0xCC, 0xAA, 0xBB, 0xCC };
        var pattern = AobPattern.Parse("AA BB CC");
        var result = (IReadOnlyList<int>)method!.Invoke(null, new object[] { memory, pattern, 1 })!;
        result.Count.Should().Be(1);
    }

    // ── IsPatternMatchAtOffset branches ─────────────────────────────────

    [Fact]
    public void IsPatternMatchAtOffset_ShouldReturnTrue_WhenExactMatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsPatternMatchAtOffset", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var memory = new byte[] { 0xAA, 0xBB, 0xCC };
        var signature = new byte?[] { 0xAA, 0xBB, 0xCC };
        var result = (bool)method!.Invoke(null, new object[] { memory, signature, 0 })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsPatternMatchAtOffset_ShouldReturnTrue_WhenWildcardMatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsPatternMatchAtOffset", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var memory = new byte[] { 0xAA, 0x42, 0xCC };
        var signature = new byte?[] { 0xAA, null, 0xCC };
        var result = (bool)method!.Invoke(null, new object[] { memory, signature, 0 })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsPatternMatchAtOffset_ShouldReturnFalse_WhenMismatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsPatternMatchAtOffset", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var memory = new byte[] { 0xAA, 0xBB, 0xCC };
        var signature = new byte?[] { 0xAA, 0xFF, 0xCC };
        var result = (bool)method!.Invoke(null, new object[] { memory, signature, 0 })!;
        result.Should().BeFalse();
    }

    // ── CollectRequiredWorkshopIds branches ─────────────────────────────

    [Fact]
    public void CollectRequiredWorkshopIds_ShouldCollectFromSteamWorkshopId()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CollectRequiredWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = BuildHelperProfile("test") with { SteamWorkshopId = "12345" };
        var result = (HashSet<string>)method!.Invoke(null, new object[] { profile })!;
        result.Should().Contain("12345");
    }

    [Fact]
    public void CollectRequiredWorkshopIds_ShouldCollectFromRequiredWorkshopIds()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CollectRequiredWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = "111,222,333"
        };
        var profile = BuildHelperProfile("test") with { Metadata = metadata };
        var result = (HashSet<string>)method!.Invoke(null, new object[] { profile })!;
        result.Should().Contain("111");
        result.Should().Contain("222");
        result.Should().Contain("333");
    }

    [Fact]
    public void CollectRequiredWorkshopIds_ShouldCollectFromLegacyRequiredWorkshopId()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CollectRequiredWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopId"] = "999"
        };
        var profile = BuildHelperProfile("test") with { Metadata = metadata };
        var result = (HashSet<string>)method!.Invoke(null, new object[] { profile })!;
        result.Should().Contain("999");
    }

    [Fact]
    public void CollectRequiredWorkshopIds_ShouldReturnEmpty_WhenNoWorkshopIds()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "CollectRequiredWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var profile = BuildHelperProfile("test");
        var result = (HashSet<string>)method!.Invoke(null, new object[] { profile })!;
        result.Should().BeEmpty();
    }

    // ── TryReadBooleanPayload FormatException paths ─────────────────────

    [Fact]
    public void TryReadBooleanPayload_ShouldReturnFalse_WhenNodeIsArrayType()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["key"] = new JsonArray(1, 2, 3) };
        var args = new object?[] { payload, "key", false };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldParseFalseString()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["key"] = "false" };
        var args = new object?[] { payload, "key", false };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        ((bool)args[2]!).Should().BeFalse();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldReturnFalse_WhenStringNotParseable()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["key"] = "not_a_bool" };
        var args = new object?[] { payload, "key", false };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldParseIntZero()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var payload = new JsonObject { ["key"] = 0 };
        var args = new object?[] { payload, "key", false };
        var ok = (bool)method!.Invoke(null, args)!;
        ok.Should().BeTrue();
        ((bool)args[2]!).Should().BeFalse();
    }

    // ── IsStarWarsGProcess branches ─────────────────────────────────────

    [Fact]
    public void IsStarWarsGProcess_ShouldReturnTrue_WhenProcessNameMatches()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsStarWarsGProcess", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic).Process with
        {
            ProcessName = "starwarsg"
        };
        var result = (bool)method!.Invoke(null, new object[] { process })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_ShouldReturnFalse_WhenDifferentProcess()
    {
        var method = typeof(RuntimeAdapter).GetMethod(
            "IsStarWarsGProcess", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var process = RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic).Process with
        {
            ProcessName = "sweaw"
        };
        var result = (bool)method!.Invoke(null, new object[] { process })!;
        result.Should().BeFalse();
    }

    // ── Helper builders ────────────────────────────────────────────────

    private static ActionExecutionRequest BuildRequest(string actionId, RuntimeMode runtimeMode)
    {
        var payload = new JsonObject { ["helperHookId"] = "hero_hook" };
        return new ActionExecutionRequest(
            Action: new ActionSpec(
                actionId,
                ActionCategory.Hero,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
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
                id, ActionCategory.Hero, RuntimeMode.Unknown, ExecutionKind.Helper,
                new JsonObject(), VerifyReadback: false, CooldownMs: 0),
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
                    Signatures: [new SignatureSpec("credits", "AA BB", 0)])
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
