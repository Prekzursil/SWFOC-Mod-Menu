using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 10 coverage tests targeting uncovered branches in RuntimeAdapter,
/// NamedPipeExtenderBackend, ProcessMemoryScanner, and SignatureResolver.
/// Focuses on pure-computation methods, record factories, static helpers,
/// validation logic, and guard clauses -- no Win32 API calls.
/// </summary>
public sealed class RuntimeWave10CoverageTests
{
    private static readonly Type RuntimeAdapterType = typeof(RuntimeAdapter);

    // ---- Helper: invoke private static method by name ----
    private static object? InvokeStatic(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        method.Should().NotBeNull($"Expected to find method '{methodName}' on {type.Name}");
        return method!.Invoke(null, args);
    }

    private static ActionSpec CreateActionSpec(string id)
    {
        return new ActionSpec(id, ActionCategory.Global, RuntimeMode.Unknown,
            ExecutionKind.Memory, new JsonObject(), false, 0);
    }

    private static TrainerProfile CreateMinimalProfile(
        string id,
        string? steamWorkshopId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new TrainerProfile(
            id, id, null, ExeTarget.Swfoc, steamWorkshopId,
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), string.Empty, Array.Empty<HelperHookSpec>(),
            metadata);
    }

    // ================================================================
    // 1. NormalizePatternText
    // ================================================================

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("f3 0f 2c", "F3 0F 2C")]
    [InlineData("? ?? AB cd", "?? ?? AB CD")]
    [InlineData("  f3  0f  ", "F3 0F")]
    public void NormalizePatternText_VariousInputs(string? input, string expected)
    {
        var result = InvokeStatic(RuntimeAdapterType, "NormalizePatternText", input!);
        result.Should().Be(expected);
    }

    // ================================================================
    // 2. BuildPatternSnippet
    // ================================================================

    [Fact]
    public void BuildPatternSnippet_EmptyModule_ReturnsEmpty()
    {
        var result = InvokeStatic(RuntimeAdapterType, "BuildPatternSnippet",
            Array.Empty<byte>(), 0, 4);
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void BuildPatternSnippet_ValidModule_ReturnsHexSnippet()
    {
        var module = new byte[32];
        for (var i = 0; i < module.Length; i++) module[i] = (byte)i;
        var result = (string)InvokeStatic(RuntimeAdapterType, "BuildPatternSnippet",
            module, 10, 4)!;
        result.Should().NotBeNullOrEmpty();
        result.Should().NotContain("-");
    }

    [Fact]
    public void BuildPatternSnippet_HitAtStart_ClampsToZero()
    {
        var module = new byte[20];
        for (var i = 0; i < module.Length; i++) module[i] = (byte)(0xA0 + i);
        var result = (string)InvokeStatic(RuntimeAdapterType, "BuildPatternSnippet",
            module, 2, 4)!;
        result.Should().NotBeNullOrEmpty();
    }

    // ================================================================
    // 3. SanitizeArtifactToken
    // ================================================================

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData("___", "unknown")]
    [InlineData("hello world!", "hello_world")]
    [InlineData("  test_123  ", "test_123")]
    [InlineData("abc", "abc")]
    public void SanitizeArtifactToken_VariousInputs(string? input, string expected)
    {
        var result = InvokeStatic(RuntimeAdapterType, "SanitizeArtifactToken", input!);
        result.Should().Be(expected);
    }

    // ================================================================
    // 4. ClampConfidence
    // ================================================================

    [Theory]
    [InlineData(double.NaN, 0d)]
    [InlineData(-0.5d, 0d)]
    [InlineData(0d, 0d)]
    [InlineData(0.5d, 0.5d)]
    [InlineData(1d, 1d)]
    [InlineData(1.5d, 1d)]
    public void ClampConfidence_VariousInputs(double input, double expected)
    {
        var result = InvokeStatic(RuntimeAdapterType, "ClampConfidence", input);
        result.Should().Be(expected);
    }

    // ================================================================
    // 5. ComputeSelectionScore
    // ================================================================

    [Fact]
    public void ComputeSelectionScore_ReturnsExpectedWeights()
    {
        var result = (double)InvokeStatic(RuntimeAdapterType, "ComputeSelectionScore",
            2, true, 1, true, 5_000_000)!;
        result.Should().BeApproximately(2415d, 0.01d);
    }

    [Fact]
    public void ComputeSelectionScore_AllZero_ReturnsZero()
    {
        var result = (double)InvokeStatic(RuntimeAdapterType, "ComputeSelectionScore",
            0, false, 0, false, 0)!;
        result.Should().Be(0d);
    }

    // ================================================================
    // 6. ResolveProcessSelectionReason
    // ================================================================

    [Fact]
    public void ResolveProcessSelectionReason_WithRecommendation_ReturnsReason()
    {
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["recommendationReason"] = "workshop_fingerprint" });
        var result = InvokeStatic(RuntimeAdapterType, "ResolveProcessSelectionReason", process);
        result.Should().Be("workshop_fingerprint");
    }

    [Fact]
    public void ResolveProcessSelectionReason_NoMetadata_ReturnsDefault()
    {
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        var result = InvokeStatic(RuntimeAdapterType, "ResolveProcessSelectionReason", process);
        result.Should().Be("exe_target_match");
    }

    [Fact]
    public void ResolveProcessSelectionReason_EmptyRecommendation_ReturnsDefault()
    {
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["recommendationReason"] = "  " });
        var result = InvokeStatic(RuntimeAdapterType, "ResolveProcessSelectionReason", process);
        result.Should().Be("exe_target_match");
    }

    // ================================================================
    // 7. IsStarWarsGProcess
    // ================================================================

    [Theory]
    [InlineData("StarWarsG", "c:\\game\\foo.exe", true)]
    [InlineData("StarWarsG.exe", "c:\\game\\foo.exe", true)]
    [InlineData("swfoc", "c:\\game\\StarWarsG.exe", true)]
    [InlineData("swfoc", "c:\\game\\swfoc.exe", false)]
    public void IsStarWarsGProcess_ByNameOrPath(string processName, string path, bool expected)
    {
        var process = new ProcessMetadata(1, processName, path, null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        var result = (bool)InvokeStatic(RuntimeAdapterType, "IsStarWarsGProcess", process)!;
        result.Should().Be(expected);
    }

    [Fact]
    public void IsStarWarsGProcess_MetadataTrue_ReturnsTrue()
    {
        var process = new ProcessMetadata(1, "other", "c:\\other.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var result = (bool)InvokeStatic(RuntimeAdapterType, "IsStarWarsGProcess", process)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsStarWarsGProcess_MetadataFalse_ReturnsFalse()
    {
        var process = new ProcessMetadata(1, "other", "c:\\other.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["isStarWarsG"] = "false" });
        var result = (bool)InvokeStatic(RuntimeAdapterType, "IsStarWarsGProcess", process)!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 8. ProcessContainsWorkshopId
    // ================================================================

    [Fact]
    public void ProcessContainsWorkshopId_InCommandLine_ReturnsTrue()
    {
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", "--modid=12345", ExeTarget.Swfoc, RuntimeMode.Unknown);
        var result = (bool)InvokeStatic(RuntimeAdapterType, "ProcessContainsWorkshopId", process, "12345")!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessContainsWorkshopId_InMetadataSteamModIds_ReturnsTrue()
    {
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string> { ["steamModIdsDetected"] = "111,222,333" });
        var result = (bool)InvokeStatic(RuntimeAdapterType, "ProcessContainsWorkshopId", process, "222")!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessContainsWorkshopId_InLaunchContext_ReturnsTrue()
    {
        var launchContext = new LaunchContext(LaunchKind.Workshop, false, new[] { "456" }, null, null, null, null);
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, LaunchContext: launchContext);
        var result = (bool)InvokeStatic(RuntimeAdapterType, "ProcessContainsWorkshopId", process, "456")!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessContainsWorkshopId_NoMatch_ReturnsFalse()
    {
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        var result = (bool)InvokeStatic(RuntimeAdapterType, "ProcessContainsWorkshopId", process, "999")!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 9. CollectRequiredWorkshopIds
    // ================================================================

    [Fact]
    public void CollectRequiredWorkshopIds_NoWorkshopData_ReturnsEmpty()
    {
        var profile = CreateMinimalProfile("test");
        var result = (HashSet<string>)InvokeStatic(RuntimeAdapterType, "CollectRequiredWorkshopIds", profile)!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void CollectRequiredWorkshopIds_AllSources_MergesIds()
    {
        var profile = CreateMinimalProfile("test", steamWorkshopId: "111",
            metadata: new Dictionary<string, string>
            {
                ["requiredWorkshopIds"] = "222,333",
                ["requiredWorkshopId"] = "444"
            });
        var result = (HashSet<string>)InvokeStatic(RuntimeAdapterType, "CollectRequiredWorkshopIds", profile)!;
        result.Should().HaveCount(4);
        result.Should().Contain("111").And.Contain("222").And.Contain("333").And.Contain("444");
    }

    // ================================================================
    // 10. TryResolveTelemetryModeFromContext
    // ================================================================

    [Theory]
    [InlineData("Galactic", RuntimeMode.Galactic)]
    [InlineData("TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("Land", RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", RuntimeMode.TacticalSpace)]
    [InlineData("Space", RuntimeMode.TacticalSpace)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical)]
    public void TryResolveTelemetryModeFromContext_ValidModes(string modeString, RuntimeMode expectedMode)
    {
        var context = new Dictionary<string, object?> { ["telemetryRuntimeMode"] = modeString };
        var method = RuntimeAdapterType.GetMethod("TryResolveTelemetryModeFromContext",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { context, RuntimeMode.Unknown };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(expectedMode);
    }

    [Fact]
    public void TryResolveTelemetryModeFromContext_NullContext_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("TryResolveTelemetryModeFromContext",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { null, RuntimeMode.Unknown };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryResolveTelemetryModeFromContext_UnrecognizedMode_ReturnsFalse()
    {
        var context = new Dictionary<string, object?> { ["telemetryRuntimeMode"] = "SomeOther" };
        var method = RuntimeAdapterType.GetMethod("TryResolveTelemetryModeFromContext",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { context, RuntimeMode.Unknown };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryResolveTelemetryModeFromContext_WhitespaceValue_ReturnsFalse()
    {
        var context = new Dictionary<string, object?> { ["telemetryRuntimeMode"] = "   " };
        var method = RuntimeAdapterType.GetMethod("TryResolveTelemetryModeFromContext",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { context, RuntimeMode.Unknown };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryResolveTelemetryModeFromContext_NonStringValue_UsesToString()
    {
        var context = new Dictionary<string, object?> { ["telemetryRuntimeMode"] = 42 };
        var method = RuntimeAdapterType.GetMethod("TryResolveTelemetryModeFromContext",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { context, RuntimeMode.Unknown };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 11. ResolveManualOverrideMode
    // ================================================================

    [Theory]
    [InlineData("Galactic", RuntimeMode.Galactic)]
    [InlineData("AnyTactical", RuntimeMode.AnyTactical)]
    [InlineData("TacticalLand", RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", RuntimeMode.TacticalSpace)]
    public void ResolveManualOverrideMode_ValidModes(string modeValue, RuntimeMode expected)
    {
        var context = new Dictionary<string, object?> { ["runtimeModeOverride"] = modeValue };
        var result = InvokeStatic(RuntimeAdapterType, "ResolveManualOverrideMode", context) as RuntimeMode?;
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveManualOverrideMode_NullContext_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveManualOverrideMode",
            new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveManualOverrideMode_Auto_ReturnsNull()
    {
        var context = new Dictionary<string, object?> { ["runtimeModeOverride"] = "Auto" };
        var result = InvokeStatic(RuntimeAdapterType, "ResolveManualOverrideMode", context) as RuntimeMode?;
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveManualOverrideMode_Whitespace_ReturnsNull()
    {
        var context = new Dictionary<string, object?> { ["runtimeModeOverride"] = "  " };
        var result = InvokeStatic(RuntimeAdapterType, "ResolveManualOverrideMode", context) as RuntimeMode?;
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveManualOverrideMode_Unrecognized_ReturnsNull()
    {
        var context = new Dictionary<string, object?> { ["runtimeModeOverride"] = "Skirmish" };
        var result = InvokeStatic(RuntimeAdapterType, "ResolveManualOverrideMode", context) as RuntimeMode?;
        result.Should().BeNull();
    }

    // ================================================================
    // 12. ResolveContextRouteType
    // ================================================================

    [Fact]
    public void ResolveContextRouteType_SpawnContextEntity_ReturnsSpawn()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextRouteType", "spawn_context_entity");
        result!.ToString().Should().Be("Spawn");
    }

    [Fact]
    public void ResolveContextRouteType_SetContextFaction_ReturnsFaction()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextRouteType", "set_context_faction");
        result!.ToString().Should().Be("Faction");
    }

    [Fact]
    public void ResolveContextRouteType_SetContextAllegiance_ReturnsFaction()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextRouteType", "set_context_allegiance");
        result!.ToString().Should().Be("Faction");
    }

    [Fact]
    public void ResolveContextRouteType_UnknownAction_ReturnsNone()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextRouteType", "unknown_action");
        result!.ToString().Should().Be("None");
    }

    // ================================================================
    // 13. ResolveContextFactionTargetAction / ResolveContextSpawnTargetAction
    // ================================================================

    [Theory]
    [InlineData(RuntimeMode.Galactic, "set_planet_owner")]
    [InlineData(RuntimeMode.AnyTactical, "set_selected_owner_faction")]
    [InlineData(RuntimeMode.TacticalLand, "set_selected_owner_faction")]
    [InlineData(RuntimeMode.TacticalSpace, "set_selected_owner_faction")]
    public void ResolveContextFactionTargetAction_KnownModes(RuntimeMode mode, string expected)
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextFactionTargetAction", mode);
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveContextFactionTargetAction_UnknownMode_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextFactionTargetAction", RuntimeMode.Unknown);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(RuntimeMode.Galactic, "spawn_galactic_entity")]
    [InlineData(RuntimeMode.AnyTactical, "spawn_tactical_entity")]
    [InlineData(RuntimeMode.TacticalLand, "spawn_tactical_entity")]
    [InlineData(RuntimeMode.TacticalSpace, "spawn_tactical_entity")]
    public void ResolveContextSpawnTargetAction_KnownModes(RuntimeMode mode, string expected)
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextSpawnTargetAction", mode);
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveContextSpawnTargetAction_UnknownMode_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ResolveContextSpawnTargetAction", RuntimeMode.Unknown);
        result.Should().BeNull();
    }

    // ================================================================
    // 14. CreateContextModeBlockedResult
    // ================================================================

    [Fact]
    public void CreateContextModeBlockedResult_Spawn_ContainsSpawnMessage()
    {
        var contextRouteType = RuntimeAdapterType.GetNestedType("ContextRouteType", BindingFlags.NonPublic)!;
        var spawnValue = Enum.Parse(contextRouteType, "Spawn");
        var method = RuntimeAdapterType.GetMethod("CreateContextModeBlockedResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ActionExecutionResult)method!.Invoke(null, new object[] { spawnValue, RuntimeMode.Unknown })!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("spawning");
    }

    [Fact]
    public void CreateContextModeBlockedResult_Faction_ContainsFactionMessage()
    {
        var contextRouteType = RuntimeAdapterType.GetNestedType("ContextRouteType", BindingFlags.NonPublic)!;
        var factionValue = Enum.Parse(contextRouteType, "Faction");
        var method = RuntimeAdapterType.GetMethod("CreateContextModeBlockedResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ActionExecutionResult)method!.Invoke(null, new object[] { factionValue, RuntimeMode.Unknown })!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("faction");
    }

    // ================================================================
    // 15. CreateContextMissingActionResult
    // ================================================================

    [Fact]
    public void CreateContextMissingActionResult_ContainsProfileAndAction()
    {
        var result = (ActionExecutionResult)InvokeStatic(RuntimeAdapterType,
            "CreateContextMissingActionResult", "my_profile", "spawn_tactical_entity")!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("my_profile");
        result.Message.Should().Contain("spawn_tactical_entity");
    }

    // ================================================================
    // 16. ClonePayload
    // ================================================================

    [Fact]
    public void ClonePayload_DeepClone()
    {
        var original = new JsonObject { ["key1"] = "value1", ["key2"] = 42 };
        var result = (JsonObject)InvokeStatic(RuntimeAdapterType, "ClonePayload", original)!;
        result.Should().NotBeSameAs(original);
        result["key1"]!.GetValue<string>().Should().Be("value1");
        result["key2"]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void ClonePayload_EmptyPayload()
    {
        var original = new JsonObject();
        var result = (JsonObject)InvokeStatic(RuntimeAdapterType, "ClonePayload", original)!;
        result.Count.Should().Be(0);
    }

    // ================================================================
    // 17. ApplyContextSpawnPayloadDefaults
    // ================================================================

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_TacticalEntity_SetsExpectedDefaults()
    {
        var payload = new JsonObject();
        InvokeStatic(RuntimeAdapterType, "ApplyContextSpawnPayloadDefaults",
            payload, "spawn_tactical_entity");
        payload["helperHookId"]!.GetValue<string>().Should().Be("spawn_bridge");
        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn_Context");
        payload["populationPolicy"]!.GetValue<string>().Should().Be("ForceZeroTactical");
        payload["persistencePolicy"]!.GetValue<string>().Should().Be("EphemeralBattleOnly");
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_GalacticEntity_SetsNormalPolicy()
    {
        var payload = new JsonObject();
        InvokeStatic(RuntimeAdapterType, "ApplyContextSpawnPayloadDefaults",
            payload, "spawn_galactic_entity");
        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn_Context");
        payload["populationPolicy"]!.GetValue<string>().Should().Be("Normal");
        payload["persistencePolicy"]!.GetValue<string>().Should().Be("PersistentGalactic");
    }

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_LegacyAction_SetsLegacyEntryPoint()
    {
        var payload = new JsonObject();
        InvokeStatic(RuntimeAdapterType, "ApplyContextSpawnPayloadDefaults",
            payload, "some_other_action");
        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn");
    }

    [Fact]
    public void ApplyContextSpawnPayloadDefaults_PresetValues_NotOverwritten()
    {
        var payload = new JsonObject
        {
            ["helperHookId"] = "custom_hook",
            ["helperEntryPoint"] = "custom_entry",
            ["populationPolicy"] = "CustomPolicy",
            ["persistencePolicy"] = "CustomPersist",
            ["allowCrossFaction"] = false
        };
        InvokeStatic(RuntimeAdapterType, "ApplyContextSpawnPayloadDefaults",
            payload, "spawn_tactical_entity");
        payload["helperHookId"]!.GetValue<string>().Should().Be("custom_hook");
        payload["helperEntryPoint"]!.GetValue<string>().Should().Be("custom_entry");
    }

    // ================================================================
    // 18. MergeDiagnostics
    // ================================================================

    [Fact]
    public void MergeDiagnostics_BothNull_ReturnsNull()
    {
        var method = RuntimeAdapterType.GetMethod("MergeDiagnostics",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null, null });
        result.Should().BeNull();
    }

    [Fact]
    public void MergeDiagnostics_BothEmpty_ReturnsPrimary()
    {
        var primary = new Dictionary<string, object?>();
        var secondary = new Dictionary<string, object?>();
        var method = RuntimeAdapterType.GetMethod("MergeDiagnostics",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { primary, secondary });
        result.Should().BeSameAs(primary);
    }

    [Fact]
    public void MergeDiagnostics_SecondaryOverwrites()
    {
        var primary = new Dictionary<string, object?> { ["a"] = "1" };
        var secondary = new Dictionary<string, object?> { ["a"] = "2", ["b"] = "3" };
        var method = RuntimeAdapterType.GetMethod("MergeDiagnostics",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (IReadOnlyDictionary<string, object?>)method!.Invoke(null,
            new object?[] { primary, secondary })!;
        result["a"].Should().Be("2");
        result["b"].Should().Be("3");
    }

    // ================================================================
    // 19. IncrementCounter
    // ================================================================

    [Fact]
    public void IncrementCounter_NewKey_SetsToOne()
    {
        var counters = new Dictionary<string, int>();
        InvokeStatic(RuntimeAdapterType, "IncrementCounter", counters, "test");
        counters["test"].Should().Be(1);
    }

    [Fact]
    public void IncrementCounter_ExistingKey_Increments()
    {
        var counters = new Dictionary<string, int> { ["test"] = 5 };
        InvokeStatic(RuntimeAdapterType, "IncrementCounter", counters, "test");
        counters["test"].Should().Be(6);
    }

    // ================================================================
    // 20. ToHex
    // ================================================================

    [Fact]
    public void ToHex_Zero_Returns0x0()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ToHex", nint.Zero);
        result.Should().Be("0x0");
    }

    [Fact]
    public void ToHex_NonZero_ReturnsHex()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ToHex", (nint)0x1234ABCD);
        result!.ToString().Should().Contain("1234ABCD");
    }

    // ================================================================
    // 21. TryReadBooleanPayload
    // ================================================================

    [Fact]
    public void TryReadBooleanPayload_BoolTrue_ReturnsTrue()
    {
        var payload = new JsonObject { ["enabled"] = true };
        var method = RuntimeAdapterType.GetMethod("TryReadBooleanPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { payload, "enabled", false };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().Be(true);
    }

    [Fact]
    public void TryReadBooleanPayload_IntOne_ReturnsTrue()
    {
        var payload = new JsonObject { ["enabled"] = 1 };
        var method = RuntimeAdapterType.GetMethod("TryReadBooleanPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { payload, "enabled", false };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().Be(true);
    }

    [Fact]
    public void TryReadBooleanPayload_IntZero_ReturnsTrueWithFalseValue()
    {
        var payload = new JsonObject { ["enabled"] = 0 };
        var method = RuntimeAdapterType.GetMethod("TryReadBooleanPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { payload, "enabled", false };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().Be(false);
    }

    [Fact]
    public void TryReadBooleanPayload_StringTrue_ReturnsTrue()
    {
        var payload = new JsonObject { ["enabled"] = "true" };
        var method = RuntimeAdapterType.GetMethod("TryReadBooleanPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { payload, "enabled", false };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().Be(true);
    }

    [Fact]
    public void TryReadBooleanPayload_StringNonParseable_ReturnsFalse()
    {
        var payload = new JsonObject { ["enabled"] = "not_a_bool" };
        var method = RuntimeAdapterType.GetMethod("TryReadBooleanPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { payload, "enabled", false };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadBooleanPayload_MissingKey_ReturnsFalse()
    {
        var payload = new JsonObject();
        var method = RuntimeAdapterType.GetMethod("TryReadBooleanPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { payload, "missing", false };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadBooleanPayload_ArrayNode_ReturnsFalse()
    {
        var payload = new JsonObject { ["enabled"] = new JsonArray(1, 2) };
        var method = RuntimeAdapterType.GetMethod("TryReadBooleanPayload",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object[] { payload, "enabled", false };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 22. TryReadPayloadString
    // ================================================================

    [Fact]
    public void TryReadPayloadString_ValidString_ReturnsTrue()
    {
        var payload = new JsonObject { ["name"] = "hello" };
        var method = RuntimeAdapterType.GetMethod("TryReadPayloadString",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { payload, "name", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().Be("hello");
    }

    [Fact]
    public void TryReadPayloadString_MissingKey_ReturnsFalse()
    {
        var payload = new JsonObject();
        var method = RuntimeAdapterType.GetMethod("TryReadPayloadString",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { payload, "missing", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadPayloadString_NonStringNode_ReturnsFalse()
    {
        var payload = new JsonObject { ["name"] = new JsonArray() };
        var method = RuntimeAdapterType.GetMethod("TryReadPayloadString",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { payload, "name", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 23. TryReadContextValue
    // ================================================================

    [Fact]
    public void TryReadContextValue_NullContext_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("TryReadContextValue",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { null, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadContextValue_FoundKey_ReturnsTrue()
    {
        var context = new Dictionary<string, object?> { ["key"] = "value" };
        var method = RuntimeAdapterType.GetMethod("TryReadContextValue",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { context, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().Be("value");
    }

    // ================================================================
    // 24. IsPromotedExtenderAction
    // ================================================================

    [Theory]
    [InlineData("freeze_timer", true)]
    [InlineData("toggle_fog_reveal", true)]
    [InlineData("toggle_ai", true)]
    [InlineData("set_unit_cap", true)]
    [InlineData("toggle_instant_build_patch", true)]
    [InlineData("set_credits", false)]
    [InlineData("", false)]
    [InlineData("unknown", false)]
    public void IsPromotedExtenderAction_VariousActions(string actionId, bool expected)
    {
        var result = (bool)InvokeStatic(RuntimeAdapterType, "IsPromotedExtenderAction", actionId)!;
        result.Should().Be(expected);
    }

    // ================================================================
    // 25. ResolvePromotedAnchorAliases
    // ================================================================

    [Theory]
    [InlineData("freeze_timer", new[] { "game_timer_freeze", "freeze_timer" })]
    [InlineData("toggle_fog_reveal", new[] { "fog_reveal", "toggle_fog_reveal" })]
    [InlineData("toggle_ai", new[] { "ai_enabled", "toggle_ai" })]
    [InlineData("set_credits", new[] { "credits", "set_credits" })]
    [InlineData("unknown_action", new string[0])]
    public void ResolvePromotedAnchorAliases_VariousActions(string actionId, string[] expected)
    {
        var result = (string[])InvokeStatic(RuntimeAdapterType, "ResolvePromotedAnchorAliases", actionId)!;
        result.Should().BeEquivalentTo(expected);
    }

    // ================================================================
    // 26. IsCreditsWrite
    // ================================================================

    [Fact]
    public void IsCreditsWrite_ByActionId_ReturnsTrue()
    {
        var action = CreateActionSpec("set_credits");
        var request = new ActionExecutionRequest(action, new JsonObject(), "profile", RuntimeMode.Unknown);
        var result = (bool)InvokeStatic(RuntimeAdapterType, "IsCreditsWrite", request, "something")!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCreditsWrite_BySymbol_ReturnsTrue()
    {
        var action = CreateActionSpec("other");
        var request = new ActionExecutionRequest(action, new JsonObject(), "profile", RuntimeMode.Unknown);
        var result = (bool)InvokeStatic(RuntimeAdapterType, "IsCreditsWrite", request, "credits")!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCreditsWrite_Neither_ReturnsFalse()
    {
        var action = CreateActionSpec("other");
        var request = new ActionExecutionRequest(action, new JsonObject(), "profile", RuntimeMode.Unknown);
        var result = (bool)InvokeStatic(RuntimeAdapterType, "IsCreditsWrite", request, "not_credits")!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 27. TryParseCreditsCvttss2siInstruction
    // ================================================================

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_ValidPattern_ReturnsTrue()
    {
        var module = new byte[] { 0xF3, 0x0F, 0x2C, 0x50, 0x70 };
        var method = RuntimeAdapterType.GetMethod("TryParseCreditsCvttss2siInstruction",
            BindingFlags.Static | BindingFlags.NonPublic);
        var instruction = Activator.CreateInstance(
            RuntimeAdapterType.GetNestedType("CreditsCvttss2siInstruction", BindingFlags.NonPublic)!,
            (byte)0, (byte)0, Array.Empty<byte>());
        var args = new object?[] { module, 0, instruction };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_OutOfBounds_ReturnsFalse()
    {
        var module = new byte[] { 0xF3, 0x0F };
        var method = RuntimeAdapterType.GetMethod("TryParseCreditsCvttss2siInstruction",
            BindingFlags.Static | BindingFlags.NonPublic);
        var instruction = Activator.CreateInstance(
            RuntimeAdapterType.GetNestedType("CreditsCvttss2siInstruction", BindingFlags.NonPublic)!,
            (byte)0, (byte)0, Array.Empty<byte>());
        var args = new object?[] { module, 0, instruction };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_WrongOpcode_ReturnsFalse()
    {
        var module = new byte[] { 0xFF, 0x0F, 0x2C, 0x50, 0x70 };
        var method = RuntimeAdapterType.GetMethod("TryParseCreditsCvttss2siInstruction",
            BindingFlags.Static | BindingFlags.NonPublic);
        var instruction = Activator.CreateInstance(
            RuntimeAdapterType.GetNestedType("CreditsCvttss2siInstruction", BindingFlags.NonPublic)!,
            (byte)0, (byte)0, Array.Empty<byte>());
        var args = new object?[] { module, 0, instruction };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_WrongModRM_ReturnsFalse()
    {
        var module = new byte[] { 0xF3, 0x0F, 0x2C, 0x10, 0x70 };
        var method = RuntimeAdapterType.GetMethod("TryParseCreditsCvttss2siInstruction",
            BindingFlags.Static | BindingFlags.NonPublic);
        var instruction = Activator.CreateInstance(
            RuntimeAdapterType.GetNestedType("CreditsCvttss2siInstruction", BindingFlags.NonPublic)!,
            (byte)0, (byte)0, Array.Empty<byte>());
        var args = new object?[] { module, 0, instruction };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_NegativeOffset_ReturnsFalse()
    {
        var module = new byte[] { 0xF3, 0x0F, 0x2C, 0x50, 0x70 };
        var method = RuntimeAdapterType.GetMethod("TryParseCreditsCvttss2siInstruction",
            BindingFlags.Static | BindingFlags.NonPublic);
        var instruction = Activator.CreateInstance(
            RuntimeAdapterType.GetNestedType("CreditsCvttss2siInstruction", BindingFlags.NonPublic)!,
            (byte)0, (byte)0, Array.Empty<byte>());
        var args = new object?[] { module, -1, instruction };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 28. FindPatternOffsets
    // ================================================================

    [Fact]
    public void FindPatternOffsets_MaxHitsZero_ReturnsEmpty()
    {
        var method = RuntimeAdapterType.GetMethod("FindPatternOffsets",
            BindingFlags.Static | BindingFlags.NonPublic);
        var pattern = AobPattern.Parse("AB CD");
        var result = (IReadOnlyList<int>)method!.Invoke(null, new object[] { new byte[16], pattern, 0 })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindPatternOffsets_FindsMultipleHits()
    {
        var method = RuntimeAdapterType.GetMethod("FindPatternOffsets",
            BindingFlags.Static | BindingFlags.NonPublic);
        var memory = new byte[] { 0xAB, 0xCD, 0x00, 0xAB, 0xCD, 0x00 };
        var pattern = AobPattern.Parse("AB CD");
        var result = (IReadOnlyList<int>)method!.Invoke(null, new object[] { memory, pattern, 10 })!;
        result.Should().HaveCount(2);
        result[0].Should().Be(0);
        result[1].Should().Be(3);
    }

    [Fact]
    public void FindPatternOffsets_RespectsMaxHits()
    {
        var method = RuntimeAdapterType.GetMethod("FindPatternOffsets",
            BindingFlags.Static | BindingFlags.NonPublic);
        var memory = new byte[] { 0xAB, 0xCD, 0x00, 0xAB, 0xCD, 0x00 };
        var pattern = AobPattern.Parse("AB CD");
        var result = (IReadOnlyList<int>)method!.Invoke(null, new object[] { memory, pattern, 1 })!;
        result.Should().HaveCount(1);
    }

    // ================================================================
    // 29. HasNearbyStoreToCreditsRva
    // ================================================================

    [Fact]
    public void HasNearbyStoreToCreditsRva_MatchingStore_ReturnsTrue()
    {
        var method = RuntimeAdapterType.GetMethod("HasNearbyStoreToCreditsRva",
            BindingFlags.Static | BindingFlags.NonPublic);
        var module = new byte[64];
        module[10] = 0x89;
        module[11] = 0x05;
        var targetRva = 10 + 6 + 100;
        BitConverter.GetBytes(100).CopyTo(module, 12);
        var result = (bool)method!.Invoke(null, new object[] { module, 8, 16, (long)targetRva })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void HasNearbyStoreToCreditsRva_NoMatch_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("HasNearbyStoreToCreditsRva",
            BindingFlags.Static | BindingFlags.NonPublic);
        var module = new byte[64];
        var result = (bool)method!.Invoke(null, new object[] { module, 0, 32, 9999L })!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 30. LooksLikeImmediateStoreFromConvertedRegister
    // ================================================================

    [Fact]
    public void LooksLikeImmediateStore_ValidStore_ReturnsTrue()
    {
        var method = RuntimeAdapterType.GetMethod("LooksLikeImmediateStoreFromConvertedRegister",
            BindingFlags.Static | BindingFlags.NonPublic);
        var module = new byte[] { 0x89, 0x50, 0x00 };
        var result = (bool)method!.Invoke(null, new object[] { module, 0, (byte)2 })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void LooksLikeImmediateStore_WrongOpcode_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("LooksLikeImmediateStoreFromConvertedRegister",
            BindingFlags.Static | BindingFlags.NonPublic);
        var module = new byte[] { 0x8B, 0x50, 0x00 };
        var result = (bool)method!.Invoke(null, new object[] { module, 0, (byte)2 })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeImmediateStore_RegisterMismatch_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("LooksLikeImmediateStoreFromConvertedRegister",
            BindingFlags.Static | BindingFlags.NonPublic);
        var module = new byte[] { 0x89, 0x58, 0x00 };
        var result = (bool)method!.Invoke(null, new object[] { module, 0, (byte)2 })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeImmediateStore_ModRegMode_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("LooksLikeImmediateStoreFromConvertedRegister",
            BindingFlags.Static | BindingFlags.NonPublic);
        var module = new byte[] { 0x89, 0xD0, 0x00 };
        var result = (bool)method!.Invoke(null, new object[] { module, 0, (byte)2 })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeImmediateStore_OffsetAtEnd_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("LooksLikeImmediateStoreFromConvertedRegister",
            BindingFlags.Static | BindingFlags.NonPublic);
        var module = new byte[] { 0x89 };
        var result = (bool)method!.Invoke(null, new object[] { module, 0, (byte)0 })!;
        result.Should().BeFalse();
    }

    // ================================================================
    // 31. Record type factory methods
    // ================================================================

    [Fact]
    public void UnitCapHookResolution_Ok_Succeeds()
    {
        var type = RuntimeAdapterType.GetNestedType("UnitCapHookResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { (nint)0x1000 });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(ok)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void UnitCapHookResolution_Fail_DoesNotSucceed()
    {
        var type = RuntimeAdapterType.GetNestedType("UnitCapHookResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { "error message" });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(fail)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void InstantBuildHookResolution_Ok_Succeeds()
    {
        var type = RuntimeAdapterType.GetNestedType("InstantBuildHookResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { (nint)0x2000, new byte[] { 0x90 } });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(ok)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void InstantBuildHookResolution_Fail_DoesNotSucceed()
    {
        var type = RuntimeAdapterType.GetNestedType("InstantBuildHookResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { "error" });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(fail)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void FogPatchFallbackResolution_Ok_Succeeds()
    {
        var type = RuntimeAdapterType.GetNestedType("FogPatchFallbackResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { (nint)0x3000, (byte)0x74, (byte)0xEB, "pattern" });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(ok)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void FogPatchFallbackResolution_Fail_DoesNotSucceed()
    {
        var type = RuntimeAdapterType.GetNestedType("FogPatchFallbackResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { RuntimeReasonCode.FALLBACK_DISABLED, "not found" });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(fail)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void CreditsHookResolution_Ok_Succeeds()
    {
        var type = RuntimeAdapterType.GetNestedType("CreditsHookResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { (nint)0x4000, (byte)0x70, (byte)0x02, new byte[] { 0xF3, 0x0F, 0x2C, 0x50, 0x70 } });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(ok)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void CreditsHookResolution_Fail_DoesNotSucceed()
    {
        var type = RuntimeAdapterType.GetNestedType("CreditsHookResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { "pattern not found" });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(fail)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void CreditsHookPatchResult_Ok_Succeeds()
    {
        var type = RuntimeAdapterType.GetNestedType("CreditsHookPatchResult", BindingFlags.NonPublic)!;
        var diagnostics = new Dictionary<string, object?> { ["test"] = "value" };
        var ok = type.GetMethod("Ok", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { "good", diagnostics });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(ok)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void CreditsHookPatchResult_Fail_DoesNotSucceed()
    {
        var type = RuntimeAdapterType.GetNestedType("CreditsHookPatchResult", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { "error" });
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(fail)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void WriteAttemptResult_SuccessWithoutObservation()
    {
        var type = RuntimeAdapterType.GetNestedTypes(BindingFlags.NonPublic)
            .First(t => t.Name.StartsWith("WriteAttemptResult"));
        var closedType = type.MakeGenericType(typeof(int));
        var instance = closedType.GetMethod("SuccessWithoutObservation", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, null);
        var success = (bool)closedType.GetProperty("Success")!.GetValue(instance)!;
        success.Should().BeTrue();
        var hasObserved = (bool)closedType.GetProperty("HasObservedValue")!.GetValue(instance)!;
        hasObserved.Should().BeFalse();
    }

    [Fact]
    public void WriteAttemptResult_SuccessWithObservation()
    {
        var type = RuntimeAdapterType.GetNestedTypes(BindingFlags.NonPublic)
            .First(t => t.Name.StartsWith("WriteAttemptResult"));
        var closedType = type.MakeGenericType(typeof(int));
        var instance = closedType.GetMethod("SuccessWithObservation", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, new object[] { 42 });
        var success = (bool)closedType.GetProperty("Success")!.GetValue(instance)!;
        success.Should().BeTrue();
        var observed = (int)closedType.GetProperty("ObservedValue")!.GetValue(instance)!;
        observed.Should().Be(42);
    }

    [Fact]
    public void ContextFactionResolution_None_HasNullFields()
    {
        var type = RuntimeAdapterType.GetNestedType("ContextFactionResolution", BindingFlags.NonPublic)!;
        var none = type.GetProperty("None", BindingFlags.Static | BindingFlags.Public)!.GetValue(null);
        type.GetProperty("RedirectedRequest")!.GetValue(none).Should().BeNull();
        type.GetProperty("BlockedResult")!.GetValue(none).Should().BeNull();
    }

    [Fact]
    public void CodePatchActionContext_CanInstantiate()
    {
        var type = RuntimeAdapterType.GetNestedType("CodePatchActionContext", BindingFlags.NonPublic)!;
        var symbolInfo = new SymbolInfo("test", (nint)0x100, SymbolValueType.Int32, AddressSource.Signature);
        var instance = Activator.CreateInstance(type,
            "symbol", true, new byte[] { 0x90 }, new byte[] { 0xCC }, symbolInfo, (nint)0x1000);
        instance.Should().NotBeNull();
        type.GetProperty("Symbol")!.GetValue(instance).Should().Be("symbol");
    }

    [Fact]
    public void CreditsWritePulseState_CanInstantiate()
    {
        var type = RuntimeAdapterType.GetNestedType("CreditsWritePulseState", BindingFlags.NonPublic)!;
        var instance = Activator.CreateInstance(type, true, (nint)0x5000);
        instance.Should().NotBeNull();
        type.GetProperty("HookTickObserved")!.GetValue(instance).Should().Be(true);
    }

    [Fact]
    public void CreditsHookInstallContext_CanInstantiate()
    {
        var type = RuntimeAdapterType.GetNestedType("CreditsHookInstallContext", BindingFlags.NonPublic)!;
        var instance = Activator.CreateInstance(type,
            (nint)0x100, (nint)0x200, new byte[] { 0x01 }, new byte[] { 0x02 });
        instance.Should().NotBeNull();
    }

    [Fact]
    public void CreditsHookTickObservation_CanInstantiate()
    {
        var type = RuntimeAdapterType.GetNestedType("CreditsHookTickObservation", BindingFlags.NonPublic)!;
        var instance = Activator.CreateInstance(type, true, 5);
        instance.Should().NotBeNull();
        type.GetProperty("Observed")!.GetValue(instance).Should().Be(true);
        type.GetProperty("HitCount")!.GetValue(instance).Should().Be(5);
    }

    // ================================================================
    // 32. TryGetFileSize / TryGetLastWriteUtc / ComputeFileSha256
    // ================================================================

    [Fact]
    public void TryGetFileSize_NullPath_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "TryGetFileSize", new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void TryGetFileSize_NonExistent_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "TryGetFileSize", @"C:\nonexistent_abc_xyz_123.dll");
        result.Should().BeNull();
    }

    [Fact]
    public void TryGetFileSize_ValidFile_ReturnsSize()
    {
        var path = typeof(RuntimeWave10CoverageTests).Assembly.Location;
        var result = InvokeStatic(RuntimeAdapterType, "TryGetFileSize", path);
        ((long?)result).Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryGetLastWriteUtc_NullPath_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "TryGetLastWriteUtc", new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void TryGetLastWriteUtc_NonExistent_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "TryGetLastWriteUtc", @"C:\nonexistent_abc_xyz_123.dll");
        result.Should().BeNull();
    }

    [Fact]
    public void TryGetLastWriteUtc_ValidFile_ReturnsDate()
    {
        var path = typeof(RuntimeWave10CoverageTests).Assembly.Location;
        var result = InvokeStatic(RuntimeAdapterType, "TryGetLastWriteUtc", path) as DateTimeOffset?;
        result.Should().NotBeNull();
    }

    [Fact]
    public void ComputeFileSha256_NullPath_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ComputeFileSha256", new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void ComputeFileSha256_NonExistent_ReturnsNull()
    {
        var result = InvokeStatic(RuntimeAdapterType, "ComputeFileSha256", @"C:\nonexistent_abc_xyz_123.dll");
        result.Should().BeNull();
    }

    [Fact]
    public void ComputeFileSha256_ValidFile_ReturnsHexHash()
    {
        var path = typeof(RuntimeWave10CoverageTests).Assembly.Location;
        var result = InvokeStatic(RuntimeAdapterType, "ComputeFileSha256", path) as string;
        result!.Should().HaveLength(64);
    }

    // ================================================================
    // 33. ComputeRelativeDisplacement / WriteInt32
    // ================================================================

    [Fact]
    public void ComputeRelativeDisplacement_ForwardJump()
    {
        var method = RuntimeAdapterType.GetMethod("ComputeRelativeDisplacement",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (int)method!.Invoke(null, new object[] { (nint)100, (nint)200 })!;
        result.Should().Be(100);
    }

    [Fact]
    public void ComputeRelativeDisplacement_BackwardJump()
    {
        var method = RuntimeAdapterType.GetMethod("ComputeRelativeDisplacement",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (int)method!.Invoke(null, new object[] { (nint)200, (nint)100 })!;
        result.Should().Be(-100);
    }

    [Fact]
    public void WriteInt32_WritesToCorrectOffset()
    {
        var method = RuntimeAdapterType.GetMethod("WriteInt32",
            BindingFlags.Static | BindingFlags.NonPublic);
        var buffer = new byte[8];
        method!.Invoke(null, new object[] { buffer, 2, 0x12345678 });
        BitConverter.ToInt32(buffer, 2).Should().Be(0x12345678);
    }

    // ================================================================
    // 34. NamedPipeExtenderBackend -- ShouldSeedProbeDefaults
    // ================================================================

    [Theory]
    [InlineData("base_swfoc", true)]
    [InlineData("base_sweaw", true)]
    [InlineData("aotr_something", true)]
    [InlineData("roe_v2", true)]
    [InlineData("custom_profile", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void ShouldSeedProbeDefaults_VariousProfiles(string profileId, bool expected)
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod("ShouldSeedProbeDefaults",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object[] { profileId })!;
        result.Should().Be(expected);
    }

    // ================================================================
    // 35. NamedPipeExtenderBackend -- static result factories
    // ================================================================

    [Fact]
    public void NamedPipeExtenderBackend_ParseResponse_NullLine_ReturnsNoResponse()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod("ParseResponse",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ExtenderResult)method!.Invoke(null, new object?[] { "cmd-123", null })!;
        result.Succeeded.Should().BeFalse();
        result.HookState.Should().Be("no_response");
    }

    [Fact]
    public void NamedPipeExtenderBackend_ParseResponse_EmptyLine_ReturnsNoResponse()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod("ParseResponse",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ExtenderResult)method!.Invoke(null, new object?[] { "cmd-123", "" })!;
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void NamedPipeExtenderBackend_CreateTimeoutResult()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod("CreateTimeoutResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ExtenderResult)method!.Invoke(null, new object[] { "cmd-456" })!;
        result.Succeeded.Should().BeFalse();
        result.HookState.Should().Be("timeout");
    }

    [Fact]
    public void NamedPipeExtenderBackend_CreateUnreachableResult()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod("CreateUnreachableResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ExtenderResult)method!.Invoke(null, new object[] { "cmd-789", "pipe broken" })!;
        result.Succeeded.Should().BeFalse();
        result.HookState.Should().Be("unreachable");
        result.Message.Should().Contain("pipe broken");
    }

    [Fact]
    public void NamedPipeExtenderBackend_CreateInvalidResponseResult()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod("CreateInvalidResponseResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ExtenderResult)method!.Invoke(null, new object[] { "cmd-aaa" })!;
        result.Succeeded.Should().BeFalse();
        result.HookState.Should().Be("invalid_response");
    }

    // ================================================================
    // 36. NamedPipeExtenderBackend -- constructors
    // ================================================================

    [Fact]
    public void NamedPipeExtenderBackend_DefaultCtor_DoesNotThrow()
    {
        var backend = new NamedPipeExtenderBackend();
        backend.BackendKind.Should().Be(ExecutionBackendKind.Extender);
    }

    [Fact]
    public void NamedPipeExtenderBackend_NullPipeName_UsesDefault()
    {
        var backend = new NamedPipeExtenderBackend(null, false);
        backend.BackendKind.Should().Be(ExecutionBackendKind.Extender);
    }

    [Fact]
    public void NamedPipeExtenderBackend_WhitespacePipeName_UsesDefault()
    {
        var backend = new NamedPipeExtenderBackend("   ", false);
        backend.BackendKind.Should().Be(ExecutionBackendKind.Extender);
    }

    // ================================================================
    // 37. NamedPipeExtenderBackend -- null guards
    // ================================================================

    [Fact]
    public async Task NamedPipeExtenderBackend_ProbeCapabilitiesAsync_NullProfileId_Throws()
    {
        var backend = new NamedPipeExtenderBackend(null, false);
        var process = new ProcessMetadata(1, "test", "c:\\test.exe", null, ExeTarget.Swfoc, RuntimeMode.Unknown);
        await Assert.ThrowsAsync<ArgumentNullException>(() => backend.ProbeCapabilitiesAsync(null!, process));
    }

    [Fact]
    public async Task NamedPipeExtenderBackend_ProbeCapabilitiesAsync_NullProcess_Throws()
    {
        var backend = new NamedPipeExtenderBackend(null, false);
        await Assert.ThrowsAsync<ArgumentNullException>(() => backend.ProbeCapabilitiesAsync("profile", null!));
    }

    [Fact]
    public async Task NamedPipeExtenderBackend_ExecuteAsync_NullCommand_Throws()
    {
        var backend = new NamedPipeExtenderBackend(null, false);
        var report = CapabilityReport.Unknown("test", RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE);
        await Assert.ThrowsAsync<ArgumentNullException>(() => backend.ExecuteAsync(null!, report));
    }

    [Fact]
    public async Task NamedPipeExtenderBackend_ExecuteAsync_NullReport_Throws()
    {
        var backend = new NamedPipeExtenderBackend(null, false);
        var action = CreateActionSpec("test");
        var request = new ActionExecutionRequest(action, new JsonObject(), "profile", RuntimeMode.Unknown);
        await Assert.ThrowsAsync<ArgumentNullException>(() => backend.ExecuteAsync(request, null!));
    }

    // ================================================================
    // 38. NamedPipeExtenderBackendContextHelpers
    // ================================================================

    [Fact]
    public void ReadContextInt_NullContext_ReturnsZero()
    {
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(null, "key").Should().Be(0);
    }

    [Fact]
    public void ReadContextInt_IntValue_ReturnsIt()
    {
        var ctx = new Dictionary<string, object?> { ["pid"] = 42 };
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(ctx, "pid").Should().Be(42);
    }

    [Fact]
    public void ReadContextInt_LongValue_CastsToInt()
    {
        var ctx = new Dictionary<string, object?> { ["pid"] = 100L };
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(ctx, "pid").Should().Be(100);
    }

    [Fact]
    public void ReadContextInt_LongOutOfRange_FallsToStringParse()
    {
        var ctx = new Dictionary<string, object?> { ["pid"] = long.MaxValue };
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(ctx, "pid").Should().Be(0);
    }

    [Fact]
    public void ReadContextInt_StringValue_Parses()
    {
        var ctx = new Dictionary<string, object?> { ["pid"] = "77" };
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(ctx, "pid").Should().Be(77);
    }

    [Fact]
    public void ReadContextInt_NullValue_ReturnsZero()
    {
        var ctx = new Dictionary<string, object?> { ["pid"] = null };
        NamedPipeExtenderBackendContextHelpers.ReadContextInt(ctx, "pid").Should().Be(0);
    }

    [Fact]
    public void ReadContextString_NullContext_ReturnsEmpty()
    {
        NamedPipeExtenderBackendContextHelpers.ReadContextString(null, "key").Should().Be(string.Empty);
    }

    [Fact]
    public void ReadContextString_StringValue_ReturnsIt()
    {
        var ctx = new Dictionary<string, object?> { ["name"] = "hello" };
        NamedPipeExtenderBackendContextHelpers.ReadContextString(ctx, "name").Should().Be("hello");
    }

    [Fact]
    public void ReadContextString_NonStringValue_UsesToString()
    {
        var ctx = new Dictionary<string, object?> { ["num"] = 42 };
        NamedPipeExtenderBackendContextHelpers.ReadContextString(ctx, "num").Should().Be("42");
    }

    [Fact]
    public void ReadContextAnchors_NullContext_ReturnsEmptyJsonObject()
    {
        var result = NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(null);
        result.Count.Should().Be(0);
    }

    // ================================================================
    // 39. ProcessMemoryScanner -- guard clauses
    // ================================================================

    [Fact]
    public void ProcessMemoryScanner_ScanInt32_MaxResultsZero_ReturnsEmpty()
    {
        ProcessMemoryScanner.ScanInt32(0, 42, false, 0, CancellationToken.None).Should().BeEmpty();
    }

    [Fact]
    public void ProcessMemoryScanner_ScanInt32_MaxResultsNegative_ReturnsEmpty()
    {
        ProcessMemoryScanner.ScanInt32(0, 42, false, -1, CancellationToken.None).Should().BeEmpty();
    }

    [Fact]
    public void ProcessMemoryScanner_ScanFloatApprox_MaxResultsZero_ReturnsEmpty()
    {
        ProcessMemoryScanner.ScanFloatApprox(0, 1.0f, 0.1f, false, 0, CancellationToken.None).Should().BeEmpty();
    }

    [Fact]
    public void ProcessMemoryScanner_ScanFloatApprox_MaxResultsNegative_ReturnsEmpty()
    {
        ProcessMemoryScanner.ScanFloatApprox(0, 1.0f, 0.1f, false, -1, CancellationToken.None).Should().BeEmpty();
    }

    [Fact]
    public void ProcessMemoryScanner_ScanFloatApprox_RequestOverload_MaxResultsZero_ReturnsEmpty()
    {
        var request = new ProcessMemoryScanner.FloatApproxScanRequest(0, 1.0f, 0.1f, false, 0);
        ProcessMemoryScanner.ScanFloatApprox(request, CancellationToken.None).Should().BeEmpty();
    }

    // ================================================================
    // 40. RuntimeAdapter -- constructor null guards and basic state
    // ================================================================

    [Fact]
    public void RuntimeAdapter_NullProcessLocator_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeAdapter(
            null!, new StubProfileRepository(), new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance));
    }

    [Fact]
    public void RuntimeAdapter_NullProfileRepository_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeAdapter(
            new StubProcessLocator(), null!, new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance));
    }

    [Fact]
    public void RuntimeAdapter_NullSignatureResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(), null!,
            NullLogger<RuntimeAdapter>.Instance));
    }

    [Fact]
    public void RuntimeAdapter_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(),
            new StubSignatureResolver(), null!));
    }

    [Fact]
    public void RuntimeAdapter_ValidConstruction_IsNotAttached()
    {
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(),
            new StubSignatureResolver(), NullLogger<RuntimeAdapter>.Instance);
        adapter.IsAttached.Should().BeFalse();
        adapter.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task RuntimeAdapter_AttachAsync_NullProfileId_Throws()
    {
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(),
            new StubSignatureResolver(), NullLogger<RuntimeAdapter>.Instance);
        await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.AttachAsync(null!));
    }

    [Fact]
    public async Task RuntimeAdapter_ReadAsync_NullSymbol_Throws()
    {
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(),
            new StubSignatureResolver(), NullLogger<RuntimeAdapter>.Instance);
        await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.ReadAsync<int>(null!));
    }

    [Fact]
    public void RuntimeAdapter_EnsureAttached_WhenNotAttached_Throws()
    {
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(),
            new StubSignatureResolver(), NullLogger<RuntimeAdapter>.Instance);
        var method = RuntimeAdapterType.GetMethod("EnsureAttached",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Throws<TargetInvocationException>(() => method!.Invoke(adapter, null));
    }

    [Fact]
    public void RuntimeAdapter_ResolveSymbol_WhenNotAttached_Throws()
    {
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(),
            new StubSignatureResolver(), NullLogger<RuntimeAdapter>.Instance);
        var method = RuntimeAdapterType.GetMethod("ResolveSymbol",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Throws<TargetInvocationException>(() => method!.Invoke(adapter, new object[] { "credits" }));
    }

    [Fact]
    public async Task RuntimeAdapter_DetachAsync_WhenNotAttached_DoesNotThrow()
    {
        var adapter = new RuntimeAdapter(
            new StubProcessLocator(), new StubProfileRepository(),
            new StubSignatureResolver(), NullLogger<RuntimeAdapter>.Instance);
        await adapter.DetachAsync();
        adapter.IsAttached.Should().BeFalse();
    }

    // ================================================================
    // 41. SignatureResolver -- SelectBestGhidraPackPath null guards
    // ================================================================

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_NullRoot_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SignatureResolver.SelectBestGhidraPackPath(null!, "fp-id"));
    }

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_NullFingerprintId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SignatureResolver.SelectBestGhidraPackPath("root", null!));
    }

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_NonExistentDir_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath(
            @"C:\nonexistent_ghidra_packs_abc123", "some_fingerprint");
        result.Should().BeNull();
    }

    // ================================================================
    // 42. BuildCreditsHookPatternNotFoundResult
    // ================================================================

    [Fact]
    public void BuildCreditsHookPatternNotFoundResult_WithCreditsRva_IncludesRva()
    {
        var method = RuntimeAdapterType.GetMethod("BuildCreditsHookPatternNotFoundResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { 5, 2, 1, 0x1234L });
        var msg = result!.GetType().GetProperty("Message")!.GetValue(result) as string;
        msg!.Should().Contain("0x1234");
    }

    [Fact]
    public void BuildCreditsHookPatternNotFoundResult_ZeroCreditsRva_SaysUnavailable()
    {
        var method = RuntimeAdapterType.GetMethod("BuildCreditsHookPatternNotFoundResult",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { 0, 0, 0, 0L });
        var msg = result!.GetType().GetProperty("Message")!.GetValue(result) as string;
        msg!.Should().Contain("unavailable");
    }

    // ================================================================
    // 43. ValidateCreditsHookCaveInputs
    // ================================================================

    [Fact]
    public void ValidateCreditsHookCaveInputs_WrongLength_Throws()
    {
        var method = RuntimeAdapterType.GetMethod("ValidateCreditsHookCaveInputs",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, new object[] { new byte[3], (byte)2 }));
    }

    [Fact]
    public void ValidateCreditsHookCaveInputs_RegisterOutOfRange_Throws()
    {
        var method = RuntimeAdapterType.GetMethod("ValidateCreditsHookCaveInputs",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, new object[] { new byte[5], (byte)8 }));
    }

    [Fact]
    public void ValidateCreditsHookCaveInputs_ValidInputs_DoesNotThrow()
    {
        var method = RuntimeAdapterType.GetMethod("ValidateCreditsHookCaveInputs",
            BindingFlags.Static | BindingFlags.NonPublic);
        method!.Invoke(null, new object[] { new byte[5], (byte)2 });
    }

    // ================================================================
    // 44. Diagnostic resolution helpers
    // ================================================================

    [Fact]
    public void TryReadDiagnosticString_NullDiagnostics_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("TryReadDiagnosticString",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { null, "key", null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryResolveFirstDiagnosticValue_MatchOnSecondKey_ReturnsTrue()
    {
        var method = RuntimeAdapterType.GetMethod("TryResolveFirstDiagnosticValue",
            BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["b"] = "found" };
        var keys = new List<string> { "a", "b" } as IReadOnlyList<string>;
        var args = new object?[] { diag, keys, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().Be("found");
    }

    [Fact]
    public void ResolveHybridExecutionFlag_NullDiagnostics_ReturnsFalse()
    {
        var method = RuntimeAdapterType.GetMethod("ResolveHybridExecutionFlag",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { null })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveHybridExecutionFlag_TrueString_ReturnsTrue()
    {
        var method = RuntimeAdapterType.GetMethod("ResolveHybridExecutionFlag",
            BindingFlags.Static | BindingFlags.NonPublic);
        var diag = new Dictionary<string, object?> { ["hybridExecution"] = "true" };
        var result = (bool)method!.Invoke(null, new object?[] { diag })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveBackendDiagnosticValue_NoDiag_ReturnsFallback()
    {
        var method = RuntimeAdapterType.GetMethod("ResolveBackendDiagnosticValue",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (string)method!.Invoke(null, new object?[] { null, ExecutionBackendKind.Memory })!;
        result.Should().Be("Memory");
    }

    // ================================================================
    // Stubs
    // ================================================================

    private sealed class StubProcessLocator : IProcessLocator
    {
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProcessMetadata>>(Array.Empty<ProcessMetadata>());

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
            => Task.FromResult<ProcessMetadata?>(null);
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateMinimalProfile(profileId));

        public Task<IReadOnlyList<TrainerProfile>> GetAllProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TrainerProfile>>(Array.Empty<TrainerProfile>());

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
            => Task.FromResult(CreateMinimalProfile(profileId));

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class StubSignatureResolver : ISignatureResolver
    {
        public Task<SymbolMap> ResolveAsync(
            ProfileBuild profileBuild,
            IReadOnlyList<SignatureSet> signatureSets,
            IReadOnlyDictionary<string, long> fallbackOffsets,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SymbolMap(new Dictionary<string, SymbolInfo>()));
    }
}
