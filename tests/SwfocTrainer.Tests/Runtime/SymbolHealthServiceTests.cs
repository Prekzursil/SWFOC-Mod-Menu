using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class SymbolHealthServiceTests
{
    [Fact]
    public void Evaluate_Should_Return_Unresolved_When_Address_Is_Zero()
    {
        var service = new SymbolHealthService();
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var symbol = new SymbolInfo(
            Name: "credits",
            Address: nint.Zero,
            ValueType: SymbolValueType.Int32,
            Source: AddressSource.None);

        var result = service.Evaluate(symbol, profile, RuntimeMode.Unknown);

        result.Status.Should().Be(SymbolHealthStatus.Unresolved);
        result.Reason.Should().Be("symbol_address_unresolved");
        result.Confidence.Should().Be(0.0d);
    }

    [Fact]
    public void Evaluate_Should_Degrade_For_Critical_Fallback_With_Mode_Mismatch()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["criticalSymbols"] = "credits",
            ["symbolValidationRules"] =
                "[{\"Symbol\":\"credits\",\"Mode\":\"AnyTactical\",\"IntMin\":0,\"IntMax\":2000000000,\"Critical\":true}]"
        };
        var profile = CreateProfile(metadata);
        var service = new SymbolHealthService();
        var symbol = new SymbolInfo(
            Name: "credits",
            Address: (nint)0x123456,
            ValueType: SymbolValueType.Int32,
            Source: AddressSource.Fallback,
            Diagnostics: "fallback");

        var result = service.Evaluate(symbol, profile, RuntimeMode.Galactic);

        result.Status.Should().Be(SymbolHealthStatus.Degraded);
        result.Reason.Should().Contain("fallback_offset");
        result.Reason.Should().Contain("mode_mismatch");
        result.Reason.Should().Contain("critical");
        result.Confidence.Should().BeLessThanOrEqualTo(0.55d);
        result.IsCritical.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Should_Return_Healthy_For_Signature_Symbol()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var service = new SymbolHealthService();
        var symbol = new SymbolInfo(
            Name: "game_speed",
            Address: (nint)0x5555,
            ValueType: SymbolValueType.Float,
            Source: AddressSource.Signature,
            Diagnostics: "sig");

        var result = service.Evaluate(symbol, profile, RuntimeMode.Unknown);

        result.Status.Should().Be(SymbolHealthStatus.Healthy);
        result.Reason.Should().Be("signature_resolved");
        result.Confidence.Should().BeGreaterThan(0.90d);
        result.IsCritical.Should().BeFalse();
    }

    private static TrainerProfile CreateProfile(IReadOnlyDictionary<string, string> metadata)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test Profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new ActionSpec(
                    "set_credits",
                    ActionCategory.Economy,
                    RuntimeMode.Unknown,
                    ExecutionKind.Memory,
                    new JsonObject(),
                    VerifyReadback: true,
                    CooldownMs: 100,
                    Description: "test")
            },
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }
}
