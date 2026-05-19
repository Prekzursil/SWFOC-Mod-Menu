using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Ux;
using Xunit;

namespace SwfocTrainer.Tests.Ux;

/// <summary>
/// Iteration 53 bonus — tests for FeatureTogglePersistence (JSON
/// save/load of FeatureToggleCoordinator state). Verifies the
/// "resume previous session" workflow: persist-on-shutdown, parse-back
/// across schema drift, and the load-as-synthetic-feedback replay path.
/// </summary>
public sealed class FeatureTogglePersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public FeatureTogglePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "swfoc_trainer_persistence_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private string PathInTemp(string name) => Path.Combine(_tempDir, name);

    // ─── ToJson ────────────────────────────────────────────────

    [Fact]
    public void ToJson_EmptyCoordinator_EmitsSchemaWithEmptyStates()
    {
        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        var json = FeatureTogglePersistence.ToJson(coord);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("states").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task ToJson_AfterToggles_CapturesEachFeatureState()
    {
        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        await coord.ToggleAsync("god_mode", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("god_mode on", "engine call OK", "god_mode")));
        await coord.ToggleAsync("ohk", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("ohk on", "armed", "ohk")));

        var json = FeatureTogglePersistence.ToJson(coord);

        using var doc = JsonDocument.Parse(json);
        var states = doc.RootElement.GetProperty("states");
        states.GetProperty("god_mode").GetProperty("enabled").GetBoolean().Should().BeTrue();
        states.GetProperty("god_mode").GetProperty("lastReason").GetString().Should().Be("engine call OK");
        states.GetProperty("ohk").GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ToJson_NullCoordinator_Throws()
    {
        Action call = () => FeatureTogglePersistence.ToJson(null!);
        call.Should().Throw<ArgumentNullException>();
    }

    // ─── SaveTo ────────────────────────────────────────────────

    [Fact]
    public async Task SaveTo_WritesFileAndCreatesParentDirectory()
    {
        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        await coord.ToggleAsync("free_cam", true,
            action: _ => Task.FromResult(UxFeedback.Success("on", "ok", "free_cam")));

        var nested = PathInTemp("nested/dir/state.json");
        FeatureTogglePersistence.SaveTo(coord, nested);

        File.Exists(nested).Should().BeTrue();
        File.ReadAllText(nested).Should().Contain("free_cam");
    }

    [Fact]
    public async Task SaveTo_OverwritesExistingFile()
    {
        var path = PathInTemp("state.json");
        File.WriteAllText(path, "OLD CONTENTS");

        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        await coord.ToggleAsync("freeze_credits", true,
            action: _ => Task.FromResult(UxFeedback.Success("on", "ok", "freeze_credits")));

        FeatureTogglePersistence.SaveTo(coord, path);

        var contents = File.ReadAllText(path);
        contents.Should().NotContain("OLD CONTENTS");
        contents.Should().Contain("freeze_credits");
    }

    [Fact]
    public void SaveTo_LeavesNoTempArtifactOnSuccess()
    {
        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        var path = PathInTemp("state.json");

        FeatureTogglePersistence.SaveTo(coord, path);

        File.Exists(path + ".tmp").Should().BeFalse(
            "atomic-rename must move the temp file over the target");
    }

    [Fact]
    public void SaveTo_NullArguments_Throw()
    {
        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        ((Action)(() => FeatureTogglePersistence.SaveTo(null!, "x"))).Should()
            .Throw<ArgumentNullException>();
        ((Action)(() => FeatureTogglePersistence.SaveTo(coord, null!))).Should()
            .Throw<ArgumentNullException>();
    }

    // ─── ReadFromJson ──────────────────────────────────────────

    [Fact]
    public void ReadFromJson_EmptyStates_ReturnsEmptyList()
    {
        var json = "{\"schemaVersion\":1,\"states\":{}}";
        FeatureTogglePersistence.ReadFromJson(json).Should().BeEmpty();
    }

    [Fact]
    public void ReadFromJson_MissingStates_ReturnsEmptyList()
    {
        // Older schema where the writer forgot to emit `states` at all.
        var json = "{\"schemaVersion\":1}";
        FeatureTogglePersistence.ReadFromJson(json).Should().BeEmpty();
    }

    [Fact]
    public void ReadFromJson_ParsesFullEntry()
    {
        var json = """
            {
              "schemaVersion": 1,
              "states": {
                "god_mode": {
                  "enabled": true,
                  "lastChangedUtc": "2026-04-24T12:05:00.0000000+00:00",
                  "lastReason": "engine call OK"
                }
              }
            }
            """;
        var entries = FeatureTogglePersistence.ReadFromJson(json);
        entries.Should().HaveCount(1);
        entries[0].FeatureId.Should().Be("god_mode");
        entries[0].Enabled.Should().BeTrue();
        entries[0].LastReason.Should().Be("engine call OK");
        entries[0].LastChanged.Year.Should().Be(2026);
    }

    [Fact]
    public void ReadFromJson_MalformedRoot_Throws()
    {
        Action call = () => FeatureTogglePersistence.ReadFromJson("[]");
        call.Should().Throw<JsonException>();
    }

    [Fact]
    public void ReadFromJson_FutureSchemaExtraKeys_AreSilentlyIgnored()
    {
        var json = """
            {
              "schemaVersion": 99,
              "futureField": "ignored",
              "states": {
                "god_mode": {
                  "enabled": true,
                  "lastReason": "ok",
                  "futureFieldPerEntry": 42
                }
              }
            }
            """;
        var entries = FeatureTogglePersistence.ReadFromJson(json);
        entries.Should().HaveCount(1);
        entries[0].FeatureId.Should().Be("god_mode");
    }

    [Fact]
    public void ReadFromJson_EntryMissingEnabledField_IsSkipped()
    {
        var json = """
            {
              "schemaVersion": 1,
              "states": {
                "broken": { "lastReason": "no enabled bit" },
                "good":   { "enabled": true, "lastReason": "ok" }
              }
            }
            """;
        var entries = FeatureTogglePersistence.ReadFromJson(json);
        entries.Should().ContainSingle(e => e.FeatureId == "good");
        entries.Should().NotContain(e => e.FeatureId == "broken");
    }

    [Fact]
    public void ReadFromJson_EntryWithNonBoolEnabled_IsSkipped()
    {
        var json = """
            {
              "schemaVersion": 1,
              "states": {
                "garbage": { "enabled": "not-a-bool" }
              }
            }
            """;
        FeatureTogglePersistence.ReadFromJson(json).Should().BeEmpty();
    }

    [Fact]
    public void ReadFromJson_BadTimestamp_FallsBackToMinValue()
    {
        var json = """
            {
              "schemaVersion": 1,
              "states": {
                "ts_garbage": {
                  "enabled": true,
                  "lastChangedUtc": "not-a-timestamp"
                }
              }
            }
            """;
        var entries = FeatureTogglePersistence.ReadFromJson(json);
        entries.Should().HaveCount(1);
        entries[0].LastChanged.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public void ReadFromJson_NullArgument_Throws()
    {
        Action call = () => FeatureTogglePersistence.ReadFromJson(null!);
        call.Should().Throw<ArgumentNullException>();
    }

    // ─── ReadFromPath ──────────────────────────────────────────

    [Fact]
    public void ReadFromPath_MissingFile_ReturnsEmptyList()
    {
        var path = PathInTemp("does_not_exist.json");
        FeatureTogglePersistence.ReadFromPath(path).Should().BeEmpty();
    }

    [Fact]
    public async Task ReadFromPath_RoundTripsSavedFile()
    {
        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        await coord.ToggleAsync("god_mode", true,
            action: _ => Task.FromResult(UxFeedback.Success("on", "engine OK", "god_mode")));
        await coord.ToggleAsync("ohk", true,
            action: _ => Task.FromResult(UxFeedback.Success("on", "armed", "ohk")));

        var path = PathInTemp("state.json");
        FeatureTogglePersistence.SaveTo(coord, path);

        var loaded = FeatureTogglePersistence.ReadFromPath(path);
        loaded.Select(e => e.FeatureId).Should().BeEquivalentTo("god_mode", "ohk");
        loaded.All(e => e.Enabled).Should().BeTrue();
    }

    // ─── LoadInto ──────────────────────────────────────────────

    [Fact]
    public async Task LoadInto_MissingFile_ReturnsZeroAndIsNoOp()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);
        var resumed = await FeatureTogglePersistence.LoadInto(coord,
            PathInTemp("missing.json"));

        resumed.Should().Be(0);
        coord.EnabledFeatures().Should().BeEmpty();
        sink.Count.Should().Be(0);
    }

    [Fact]
    public async Task LoadInto_ReplaysOnlyEnabledEntriesAsInfoFeedback()
    {
        var path = PathInTemp("state.json");
        var json = """
            {
              "schemaVersion": 1,
              "states": {
                "god_mode": { "enabled": true,  "lastReason": "was on" },
                "ohk":      { "enabled": false, "lastReason": "was off" },
                "free_cam": { "enabled": true,  "lastReason": "was on" }
              }
            }
            """;
        File.WriteAllText(path, json);

        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);
        var resumed = await FeatureTogglePersistence.LoadInto(coord, path);

        resumed.Should().Be(2, "two entries had enabled=true");
        coord.EnabledFeatures().Should().BeEquivalentTo("god_mode", "free_cam");
        // Each resumed enable emits exactly one Info feedback on the sink.
        sink.BySeverity(UxSeverity.Info).Should().HaveCount(2);
        sink.Items.All(f => f.Title.StartsWith("resumed ")).Should().BeTrue();
    }

    [Fact]
    public async Task LoadInto_NullArguments_Throw()
    {
        var coord = new FeatureToggleCoordinator(NullFeedbackSink.Instance);
        await FluentActions.Invoking(() =>
            FeatureTogglePersistence.LoadInto(null!, "x"))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() =>
            FeatureTogglePersistence.LoadInto(coord, null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    // ─── End-to-end round trip ─────────────────────────────────

    [Fact]
    public async Task EndToEnd_SaveThenLoadInto_ResumesEnabledFeatures()
    {
        // Session A — operator toggles 2 features on, 1 off.
        var sinkA = new RecordingFeedbackSink();
        var coordA = new FeatureToggleCoordinator(sinkA);
        await coordA.ToggleAsync("god_mode", true,
            action: _ => Task.FromResult(UxFeedback.Success("on", "ok", "god_mode")));
        await coordA.ToggleAsync("ohk", true,
            action: _ => Task.FromResult(UxFeedback.Success("on", "ok", "ohk")));
        await coordA.ToggleAsync("freeze_credits", false,
            action: _ => Task.FromResult(UxFeedback.Info("off", "ok", "freeze_credits")));

        var path = PathInTemp("state.json");
        FeatureTogglePersistence.SaveTo(coordA, path);

        // Session B — fresh coordinator, restart simulation.
        var sinkB = new RecordingFeedbackSink();
        var coordB = new FeatureToggleCoordinator(sinkB);
        var resumed = await FeatureTogglePersistence.LoadInto(coordB, path);

        resumed.Should().Be(2);
        coordB.EnabledFeatures().Should().BeEquivalentTo("god_mode", "ohk");
        coordB.IsEnabled("freeze_credits").Should().BeFalse(
            "freeze_credits was off in session A — must NOT come back enabled");
    }
}
