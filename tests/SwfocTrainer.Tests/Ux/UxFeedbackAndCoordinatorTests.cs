using FluentAssertions;
using SwfocTrainer.Core.Ux;
using Xunit;

namespace SwfocTrainer.Tests.Ux;

/// <summary>
/// Task #155 — tests for UxFeedback + FeatureToggleCoordinator. The
/// coordinator's "cleanup-on-disable" guarantee is the most important
/// invariant: every feature that's been toggled on MUST have its
/// disable callback run when the editor detaches, even if some
/// callbacks throw.
/// </summary>
public sealed class UxFeedbackAndCoordinatorTests
{
    // ─── UxFeedback factory helpers ────────────────────────────

    [Fact]
    public void UxFeedback_Info_BuildsCorrectShape()
    {
        var fb = UxFeedback.Info("title", "msg", featureId: "god_mode");
        fb.Severity.Should().Be(UxSeverity.Info);
        fb.Title.Should().Be("title");
        fb.Message.Should().Be("msg");
        fb.FeatureId.Should().Be("god_mode");
        fb.OccurredAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(UxSeverity.Info)]
    [InlineData(UxSeverity.Success)]
    [InlineData(UxSeverity.Warning)]
    [InlineData(UxSeverity.Error)]
    public void UxFeedback_FactoriesCoverAllSeverities(UxSeverity expected)
    {
        UxFeedback fb = expected switch
        {
            UxSeverity.Info => UxFeedback.Info("t", "m"),
            UxSeverity.Success => UxFeedback.Success("t", "m"),
            UxSeverity.Warning => UxFeedback.Warning("t", "m"),
            UxSeverity.Error => UxFeedback.Error("t", "m"),
            _ => throw new ArgumentException($"unhandled severity {expected}")
        };
        fb.Severity.Should().Be(expected);
    }

    // ─── RecordingFeedbackSink ─────────────────────────────────

    [Fact]
    public void RecordingSink_PreservesEmissionOrder()
    {
        var sink = new RecordingFeedbackSink();
        sink.Emit(UxFeedback.Info("first", "."));
        sink.Emit(UxFeedback.Warning("second", "."));
        sink.Emit(UxFeedback.Error("third", "."));

        sink.Count.Should().Be(3);
        sink.Items.Select(f => f.Title).Should().ContainInOrder("first", "second", "third");
        sink.Last!.Title.Should().Be("third");
    }

    [Fact]
    public void RecordingSink_BySeverity_FiltersCorrectly()
    {
        var sink = new RecordingFeedbackSink();
        sink.Emit(UxFeedback.Success("good1", "."));
        sink.Emit(UxFeedback.Error("bad1", "."));
        sink.Emit(UxFeedback.Success("good2", "."));

        sink.BySeverity(UxSeverity.Success).Should().HaveCount(2);
        sink.BySeverity(UxSeverity.Error).Should().HaveCount(1);
        sink.BySeverity(UxSeverity.Warning).Should().BeEmpty();
    }

    [Fact]
    public void RecordingSink_Clear_ResetsBuffer()
    {
        var sink = new RecordingFeedbackSink();
        sink.Emit(UxFeedback.Info("x", "."));
        sink.Clear();
        sink.Count.Should().Be(0);
        sink.Last.Should().BeNull();
    }

    [Fact]
    public void NullSink_AcceptsEmissionsSilently()
    {
        Action emit = () => NullFeedbackSink.Instance.Emit(UxFeedback.Info("x", "."));
        emit.Should().NotThrow();
    }

    // ─── FeatureToggleCoordinator: enable/disable cycle ─────────

    [Fact]
    public async Task ToggleAsync_EnableThenDisable_RunsBothCallbacks_AndEmitsFeedback()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);

        var enableCalls = 0;
        var disableCalls = 0;

        await coord.ToggleAsync("god_mode", enable: true,
            action: _ => { enableCalls++; return Task.FromResult(UxFeedback.Success("god_mode on", "OK", "god_mode")); },
            disableAction: _ => { disableCalls++; return Task.FromResult(UxFeedback.Info("god_mode off", "OK", "god_mode")); });

        coord.IsEnabled("god_mode").Should().BeTrue();
        enableCalls.Should().Be(1);
        disableCalls.Should().Be(0);

        await coord.ToggleAsync("god_mode", enable: false,
            action: _ => { disableCalls++; return Task.FromResult(UxFeedback.Info("god_mode off", "OK", "god_mode")); });

        coord.IsEnabled("god_mode").Should().BeFalse();
        disableCalls.Should().Be(1);
        sink.Count.Should().Be(2);
    }

    [Fact]
    public async Task ToggleAsync_DoubleEnable_IsIdempotent()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);
        var calls = 0;

        await coord.ToggleAsync("ohk", enable: true,
            action: _ => { calls++; return Task.FromResult(UxFeedback.Success("ohk on", ".", "ohk")); });
        await coord.ToggleAsync("ohk", enable: true,
            action: _ => { calls++; return Task.FromResult(UxFeedback.Success("ohk on", ".", "ohk")); });

        calls.Should().Be(1, "the second enable should be a no-op");
        sink.Count.Should().Be(2, "no-op still emits an Info acknowledgement");
        sink.Items[1].Severity.Should().Be(UxSeverity.Info);
        sink.Items[1].Message.Should().Contain("idempotent");
    }

    // ─── Cleanup-on-disable (the #155 critical invariant) ───────

    [Fact]
    public async Task CleanupAllAsync_DisablesAllEnabledFeatures()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);

        var disabled = new HashSet<string>();
        Task<UxFeedback> Disable(string id, CancellationToken _)
        {
            disabled.Add(id);
            return Task.FromResult(UxFeedback.Info($"{id} off", "OK", id));
        }

        await coord.ToggleAsync("god_mode", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("on", ".", "god_mode")),
            disableAction: ct => Disable("god_mode", ct));
        await coord.ToggleAsync("ohk", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("on", ".", "ohk")),
            disableAction: ct => Disable("ohk", ct));
        await coord.ToggleAsync("free_cam", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("on", ".", "free_cam")),
            disableAction: ct => Disable("free_cam", ct));

        coord.EnabledFeatures().Should().HaveCount(3);

        var cleanedCount = await coord.CleanupAllAsync();
        cleanedCount.Should().Be(3);
        disabled.Should().BeEquivalentTo("god_mode", "ohk", "free_cam");
        coord.EnabledFeatures().Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupAllAsync_SkipsDisableForFeaturesEnabledWithoutDisableCallback()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);

        await coord.ToggleAsync("no_disable_registered", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("on", ".", "x")));

        var cleaned = await coord.CleanupAllAsync();
        cleaned.Should().Be(0, "no disable callback was registered");
        // The feature stays flagged enabled because cleanup didn't apply.
        coord.IsEnabled("no_disable_registered").Should().BeTrue();
    }

    [Fact]
    public async Task CleanupAllAsync_ContinuesAfterOneCallbackThrows()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);

        var goodDisabled = false;

        await coord.ToggleAsync("bad", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("on", ".", "bad")),
            disableAction: _ => throw new InvalidOperationException("boom"));
        await coord.ToggleAsync("good", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("on", ".", "good")),
            disableAction: _ => { goodDisabled = true; return Task.FromResult(UxFeedback.Info("good off", "OK", "good")); });

        var cleaned = await coord.CleanupAllAsync();

        cleaned.Should().Be(1, "good was cleaned, bad threw");
        goodDisabled.Should().BeTrue();
        sink.BySeverity(UxSeverity.Warning).Should().HaveCount(1);
        sink.BySeverity(UxSeverity.Warning)[0].Message.Should().Contain("boom");
    }

    [Fact]
    public async Task ToggleAsync_RecordsLastChangedTimestamp()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await coord.ToggleAsync("god_mode", enable: true,
            action: _ => Task.FromResult(UxFeedback.Success("on", ".", "god_mode")));

        var state = coord.States["god_mode"];
        state.Enabled.Should().BeTrue();
        state.LastChanged.Should().BeAfter(before);
        state.LastReason.Should().Be(".");
    }

    [Fact]
    public async Task ToggleAsync_DifferentFeaturesAreIndependent()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);

        await coord.ToggleAsync("a", true, _ => Task.FromResult(UxFeedback.Success("on", ".", "a")));
        await coord.ToggleAsync("b", true, _ => Task.FromResult(UxFeedback.Success("on", ".", "b")));

        coord.EnabledFeatures().Should().BeEquivalentTo("a", "b");

        await coord.ToggleAsync("a", false, _ => Task.FromResult(UxFeedback.Info("a off", ".", "a")));
        coord.EnabledFeatures().Should().BeEquivalentTo("b");
    }

    [Fact]
    public async Task ToggleAsync_FeatureIdIsCaseInsensitive()
    {
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);
        await coord.ToggleAsync("GodMode", true, _ => Task.FromResult(UxFeedback.Success("on", ".", "GodMode")));
        coord.IsEnabled("godmode").Should().BeTrue();
        coord.IsEnabled("GODMODE").Should().BeTrue();
    }
}
