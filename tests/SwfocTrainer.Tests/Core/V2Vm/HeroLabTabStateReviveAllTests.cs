using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Core.V2Vm;

/// <summary>
/// Tests for the 2026-04-27 <see cref="HeroLabTabState.ReviveAllHeroesAsync"/>
/// composite — walks the cached Heroes list and fires SWFOC_ReviveUnit
/// per addr.
/// </summary>
public sealed class HeroLabTabStateReviveAllTests
{
    [Fact]
    public async Task ReviveAll_NoHeroesLoaded_ReturnsWarning()
    {
        var sink = new RecordingFeedbackSink();
        var dispatcher = new RecordingDispatcher();
        var state = new HeroLabTabState(dispatcher, sink, new FeatureToggleCoordinator(sink));

        var result = await state.ReviveAllHeroesAsync();

        result.Severity.Should().Be(UxSeverity.Warning);
        result.Message.Should().Contain("Refresh");
        dispatcher.RevivedAddrs.Should().BeEmpty();
    }

    [Fact]
    public async Task ReviveAll_AllSucceed_ReturnsSuccess()
    {
        var sink = new RecordingFeedbackSink();
        var dispatcher = new RecordingDispatcher();
        dispatcher.HeroesToReturn = new[]
        {
            new HeroRow(0x1000, "REBEL_HERO", 0, true, 5000, true),
            new HeroRow(0x2000, "EMPIRE_HERO", 1, true, 5000, true),
            new HeroRow(0x3000, "UNDERWORLD_HERO", 2, true, 5000, true),
        };
        var state = new HeroLabTabState(dispatcher, sink, new FeatureToggleCoordinator(sink));
        await state.RefreshHeroesAsync();

        var result = await state.ReviveAllHeroesAsync();

        result.Severity.Should().Be(UxSeverity.Success);
        result.Message.Should().Contain("revived 3");
        dispatcher.RevivedAddrs.Should().BeEquivalentTo(new long[] { 0x1000, 0x2000, 0x3000 });
    }

    [Fact]
    public async Task ReviveAll_SomeFail_ReturnsWarningWithMixedTally()
    {
        var sink = new RecordingFeedbackSink();
        var dispatcher = new RecordingDispatcher
        {
            HeroesToReturn = new[]
            {
                new HeroRow(0x1000, "REBEL_HERO", 0, true, 5000, true),
                new HeroRow(0x2000, "EMPIRE_HERO", 1, true, 5000, true),
                new HeroRow(0x3000, "UNDERWORLD_HERO", 2, true, 5000, true),
            },
            ReviveResultByAddr =
            {
                [0x1000] = true,
                [0x2000] = false,
                [0x3000] = true,
            },
        };
        var state = new HeroLabTabState(dispatcher, sink, new FeatureToggleCoordinator(sink));
        await state.RefreshHeroesAsync();

        var result = await state.ReviveAllHeroesAsync();

        result.Severity.Should().Be(UxSeverity.Warning);
        result.Message.Should().Contain("revived 2");
        result.Message.Should().Contain("failed 1");
    }

    [Fact]
    public async Task ReviveAll_AllFail_ReturnsWarning()
    {
        var sink = new RecordingFeedbackSink();
        var dispatcher = new RecordingDispatcher
        {
            HeroesToReturn = new[]
            {
                new HeroRow(0x1000, "REBEL_HERO", 0, true, 5000, true),
                new HeroRow(0x2000, "EMPIRE_HERO", 1, true, 5000, true),
            },
            DefaultReviveResult = false,
        };
        var state = new HeroLabTabState(dispatcher, sink, new FeatureToggleCoordinator(sink));
        await state.RefreshHeroesAsync();

        var result = await state.ReviveAllHeroesAsync();

        result.Severity.Should().Be(UxSeverity.Warning);
        result.Message.Should().Contain("revived 0");
        result.Message.Should().Contain("failed 2");
    }

    [Fact]
    public async Task ReviveAll_SkipsHeroesWithZeroAddr()
    {
        var sink = new RecordingFeedbackSink();
        var dispatcher = new RecordingDispatcher
        {
            HeroesToReturn = new[]
            {
                new HeroRow(0, "PHANTOM_ROW", -1, false, 0, false),
                new HeroRow(0x1000, "REBEL_HERO", 0, true, 5000, true),
            },
        };
        var state = new HeroLabTabState(dispatcher, sink, new FeatureToggleCoordinator(sink));
        await state.RefreshHeroesAsync();

        var result = await state.ReviveAllHeroesAsync();

        result.Severity.Should().Be(UxSeverity.Success);
        result.Message.Should().Contain("revived 1");
        dispatcher.RevivedAddrs.Should().BeEquivalentTo(new long[] { 0x1000 });
    }

    private sealed class RecordingDispatcher : IHeroLabDispatcher
    {
        public IReadOnlyList<HeroRow> HeroesToReturn { get; set; } = Array.Empty<HeroRow>();
        public bool DefaultReviveResult { get; set; } = true;
        public Dictionary<long, bool> ReviveResultByAddr { get; } = new();
        public List<long> RevivedAddrs { get; } = new();

        public Task<IReadOnlyList<HeroRow>> ListHeroesAsync(CancellationToken ct)
            => Task.FromResult(HeroesToReturn);

        public Task<bool> SetHeroRespawnTimerAsync(long addr, int ms, CancellationToken ct)
            => Task.FromResult(true);

        public Task<bool> SetPermadeathAsync(long addr, bool permadeath, CancellationToken ct)
            => Task.FromResult(true);

        public Task<bool> KillHeroAsync(long addr, CancellationToken ct)
            => Task.FromResult(true);

        public Task<bool> ReviveHeroAsync(long addr, CancellationToken ct)
        {
            RevivedAddrs.Add(addr);
            return Task.FromResult(
                ReviveResultByAddr.TryGetValue(addr, out var v) ? v : DefaultReviveResult);
        }

        public Task<bool> EditHeroStatAsync(long addr, string field, float value, CancellationToken ct)
            => Task.FromResult(true);
    }
}
