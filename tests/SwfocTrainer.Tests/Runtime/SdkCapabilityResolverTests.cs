using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class SdkCapabilityResolverTests
{
    [Fact]
    public void Resolve_Should_Return_Available_When_All_Anchors_Resolve()
    {
        using var fixture = new SdkMapFixture();
        fixture.WriteMap("test_profile", """
        {
          "schemaVersion": "1.0",
          "operations": [
            {
              "operationId": "set_hp",
              "readOnly": false,
              "requiredMode": "Tactical",
              "requiredSymbols": ["selected_hp"]
            }
          ]
        }
        """);

        var resolver = new SdkCapabilityResolver(fixture.RootPath);
        var profile = BuildProfile("test_profile");
        var process = BuildProcess(RuntimeMode.Tactical);
        var symbols = BuildSymbols(("selected_hp", (nint)1));

        var report = resolver.Resolve(profile, process, symbols);

        report.TryGetCapability(SdkOperationId.SetHp, out var capability).Should().BeTrue();
        capability!.Status.Should().Be(SdkCapabilityStatus.Available);
        capability.RequiredMode.Should().Be(RuntimeMode.Tactical);
        capability.ReasonCode.Should().Be("anchors_resolved");
    }

    [Fact]
    public void Resolve_Should_Return_Degraded_When_Only_Partial_Anchors_Resolve()
    {
        using var fixture = new SdkMapFixture();
        fixture.WriteMap("test_profile", """
        {
          "schemaVersion": "1.0",
          "operations": [
            {
              "operationId": "kill",
              "readOnly": false,
              "requiredMode": "Tactical",
              "requiredSymbols": ["selected_hp", "selected_shield"]
            }
          ]
        }
        """);

        var resolver = new SdkCapabilityResolver(fixture.RootPath);
        var profile = BuildProfile("test_profile");
        var process = BuildProcess(RuntimeMode.Tactical);
        var symbols = BuildSymbols(("selected_hp", (nint)1), ("selected_shield", nint.Zero));

        var report = resolver.Resolve(profile, process, symbols);

        report.TryGetCapability(SdkOperationId.Kill, out var capability).Should().BeTrue();
        capability!.Status.Should().Be(SdkCapabilityStatus.Degraded);
        capability.ReasonCode.Should().Be("anchors_partial");
    }

    [Fact]
    public void Resolve_Should_Return_Unavailable_Report_When_Map_Is_Missing()
    {
        using var fixture = new SdkMapFixture();
        var resolver = new SdkCapabilityResolver(fixture.RootPath);

        var report = resolver.Resolve(
            BuildProfile("unknown_profile"),
            BuildProcess(RuntimeMode.Unknown),
            BuildSymbols(("selected_hp", (nint)1)));

        report.Operations.Should().NotBeEmpty();
        report.Operations.Should().OnlyContain(x => x.Status == SdkCapabilityStatus.Unavailable);
        report.Operations.Should().OnlyContain(x => x.ReasonCode == "operation_map_missing");
    }

    private static TrainerProfile BuildProfile(string profileId)
    {
        return new TrainerProfile(
            profileId,
            profileId,
            null,
            ExeTarget.Swfoc,
            null,
            [],
            new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(),
            new Dictionary<string, bool>(),
            [],
            "schema",
            [],
            null);
    }

    private static ProcessMetadata BuildProcess(RuntimeMode mode)
    {
        return new ProcessMetadata(
            1234,
            "StarWarsG",
            @"C:\Games\Corruption\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=1397421866",
            ExeTarget.Swfoc,
            mode);
    }

    private static SymbolMap BuildSymbols(params (string Name, nint Address)[] entries)
    {
        var map = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, address) in entries)
        {
            map[name] = new SymbolInfo(
                name,
                address,
                SymbolValueType.Float,
                address == nint.Zero ? AddressSource.None : AddressSource.Signature,
                Confidence: address == nint.Zero ? 0.0 : 1.0,
                HealthStatus: address == nint.Zero ? SymbolHealthStatus.Unresolved : SymbolHealthStatus.Healthy,
                HealthReason: address == nint.Zero ? "missing" : "ok");
        }

        return new SymbolMap(map);
    }

    private sealed class SdkMapFixture : IDisposable
    {
        public SdkMapFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "swfoctrainer-sdk-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void WriteMap(string profileId, string json)
        {
            var profileRoot = Path.Combine(RootPath, profileId);
            Directory.CreateDirectory(profileRoot);
            var formatted = JsonDocument.Parse(json).RootElement.GetRawText();
            File.WriteAllText(Path.Combine(profileRoot, "sdk_operation_map.json"), formatted);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures on temporary paths.
            }
        }
    }
}
