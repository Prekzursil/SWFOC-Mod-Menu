using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeModeProbeResolverTests
{
    [Fact]
    public void Resolve_ShouldReturnTactical_WhenOnlyTacticalSignalsPresent()
    {
        var symbols = BuildSymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["selected_hp"] = Symbol("selected_hp", 0x1000)
        });

        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);

        result.EffectiveMode.Should().Be(RuntimeMode.AnyTactical);
        result.ReasonCode.Should().Be("mode_probe_tactical_signals");
        result.TacticalSignalCount.Should().BeGreaterThan(0);
        result.GalacticSignalCount.Should().Be(0);
    }

    [Fact]
    public void Resolve_ShouldReturnGalactic_WhenOnlyGalacticSignalsPresent()
    {
        var symbols = BuildSymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["planet_owner"] = Symbol("planet_owner", 0x2000)
        });

        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);

        result.EffectiveMode.Should().Be(RuntimeMode.Galactic);
        result.ReasonCode.Should().Be("mode_probe_galactic_signals");
        result.GalacticSignalCount.Should().BeGreaterThan(0);
        result.TacticalSignalCount.Should().Be(0);
    }

    [Fact]
    public void Resolve_ShouldKeepHint_WhenSignalsAreAmbiguous()
    {
        var symbols = BuildSymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["selected_hp"] = Symbol("selected_hp", 0x1000),
            ["planet_owner"] = Symbol("planet_owner", 0x2000)
        });

        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Galactic, symbols);

        result.EffectiveMode.Should().Be(RuntimeMode.Galactic);
        result.ReasonCode.Should().Be("mode_probe_ambiguous_keep_hint");
    }

    [Fact]
    public void Resolve_ShouldUseHint_WhenNoSignalsPresent()
    {
        var symbols = BuildSymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase));

        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.AnyTactical, symbols);

        result.EffectiveMode.Should().Be(RuntimeMode.AnyTactical);
        result.ReasonCode.Should().Be("mode_probe_no_signals_use_hint");
    }

    private static SymbolMap BuildSymbolMap(IReadOnlyDictionary<string, SymbolInfo> symbols)
    {
        return new SymbolMap(symbols);
    }

    private static SymbolInfo Symbol(string name, long address)
    {
        return new SymbolInfo(
            Name: name,
            Address: (nint)address,
            ValueType: SymbolValueType.Int32,
            Source: AddressSource.Signature,
            HealthStatus: SymbolHealthStatus.Healthy,
            Confidence: 1.0d);
    }
}
