using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-05-07 (iter 309, Thread D arc post-finale): pin tests for the new
/// <see cref="V2Settings.IconsRoot"/> property + JSON round-trip + default
/// behavior. The property is consumed by MainViewModelV2 to construct the
/// iter-308 UnitIconResolver pointing at the operator's extracted-DDS root.
/// </summary>
public sealed class Iter309IconsRootSettingsTests
{
    [Fact]
    public void IconsRoot_DefaultsToNull()
    {
        var settings = new V2Settings();
        settings.IconsRoot.Should().BeNull(
            because: "the operator must explicitly set the icons root or use the SWFOC_EXTRACTED_DDS_ROOT env var; null = no icons (graceful)");
    }

    [Fact]
    public void IconsRoot_RoundTripsThroughJson()
    {
        var original = new V2Settings { IconsRoot = @"C:\Games\SWFOC\extracted" };
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<V2Settings>(json)!;

        deserialized.IconsRoot.Should().Be(@"C:\Games\SWFOC\extracted",
            because: "iter-309 added the [JsonPropertyName(\"iconsRoot\")] attribute; missing or wrong attribute would lose the value across save/load");
    }

    [Fact]
    public void IconsRoot_NullValue_RoundTripsAsNull()
    {
        var original = new V2Settings { IconsRoot = null };
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<V2Settings>(json)!;

        deserialized.IconsRoot.Should().BeNull(
            because: "a settings file with no IconsRoot key (or explicit null) must deserialize cleanly to null, not throw");
    }

    [Fact]
    public void JsonPropertyName_IsLowerCamelCaseIconsRoot()
    {
        var settings = new V2Settings { IconsRoot = "/tmp/icons" };
        var json = JsonSerializer.Serialize(settings);
        json.Should().Contain("\"iconsRoot\":",
            because: "convention matches sibling properties (gamePath, bridgePipeName, logPath)");
        json.Should().NotContain("\"IconsRoot\":",
            because: "PascalCase property name would break operator JSON edits and downstream consumers expecting camelCase");
    }

    [Fact]
    public void OldSettingsFile_WithoutIconsRoot_DeserializesGracefully()
    {
        // Simulate a v2_settings.json that pre-dates iter-309 (no iconsRoot key).
        // Operators upgrading should not see crashes — IconsRoot defaults to null.
        var legacyJson = """
        {
            "gamePath": "C:\\Games\\SWFOC",
            "bridgePipeName": "swfoc_bridge",
            "logPath": "C:\\Games\\SWFOC\\swfoc_bridge.log",
            "autoConnect": true,
            "showAdvanced": false,
            "profileId": "base_swfoc",
            "theme": "system"
        }
        """;

        var settings = JsonSerializer.Deserialize<V2Settings>(legacyJson)!;
        settings.IconsRoot.Should().BeNull(
            because: "backward-compat: old settings files (pre-iter-309) must deserialize cleanly with IconsRoot defaulting to null");
        settings.GamePath.Should().Be(@"C:\Games\SWFOC",
            because: "non-iter-309 properties must keep round-tripping after the new property is added");
    }
}
