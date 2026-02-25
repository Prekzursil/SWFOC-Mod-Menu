using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class CapabilityMapResolverTests
{
    [Fact]
    public async Task ResolveAsync_ShouldReturnAvailable_WhenRequiredAnchorsPresent()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);

        try
        {
            var fingerprint = new BinaryFingerprint(
                FingerprintId: "fp-test",
                FileSha256: "abc123",
                ModuleName: "swfoc.exe",
                ProductVersion: "1.0",
                FileVersion: "1.0.0.0",
                TimestampUtc: DateTimeOffset.UtcNow,
                ModuleList: Array.Empty<string>(),
                SourcePath: "C:/games/swfoc.exe");

            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-test",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "set_hp": {
                  "requiredAnchors": ["selected_hp_write"],
                  "optionalAnchors": ["selected_hp_read"]
                }
              }
            }
            """;

            await File.WriteAllTextAsync(Path.Combine(mapsRoot, "fp-test.json"), mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);

            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "selected_hp_write", "selected_hp_read" });

            result.State.Should().Be(SdkCapabilityStatus.Available);
            result.ReasonCode.Should().Be(CapabilityReasonCode.AllRequiredAnchorsPresent);
            result.ProfileId.Should().Be("base_swfoc");
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldFailClosed_WhenMapMissing()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);

        try
        {
            var fingerprint = new BinaryFingerprint(
                FingerprintId: "missing-fp",
                FileSha256: "abc123",
                ModuleName: "swfoc.exe",
                ProductVersion: "1.0",
                FileVersion: "1.0.0.0",
                TimestampUtc: DateTimeOffset.UtcNow,
                ModuleList: Array.Empty<string>(),
                SourcePath: "C:/games/swfoc.exe");

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);

            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
            result.ReasonCode.Should().Be(CapabilityReasonCode.FingerprintMapMissing);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnDegraded_WhenRequiredAnchorsMissing()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);

        try
        {
            var fingerprint = new BinaryFingerprint(
                FingerprintId: "fp-test",
                FileSha256: "abc123",
                ModuleName: "swfoc.exe",
                ProductVersion: "1.0",
                FileVersion: "1.0.0.0",
                TimestampUtc: DateTimeOffset.UtcNow,
                ModuleList: Array.Empty<string>(),
                SourcePath: "C:/games/swfoc.exe");

            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-test",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "set_shield": {
                  "requiredAnchors": ["selected_shield_write"],
                  "optionalAnchors": []
                }
              }
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(mapsRoot, "fp-test.json"), mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "set_shield",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "selected_hp_write" });

            result.State.Should().Be(SdkCapabilityStatus.Degraded);
            result.ReasonCode.Should().Be(CapabilityReasonCode.RequiredAnchorsMissing);
            result.MissingAnchors.Should().ContainSingle().Which.Should().Be("selected_shield_write");
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldSupportCapabilitiesArrayShape_WhenOperationsMissing()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);

        try
        {
            var fingerprint = new BinaryFingerprint(
                FingerprintId: "fp-ghidra",
                FileSha256: "abc123",
                ModuleName: "StarWarsG.exe",
                ProductVersion: "1.0",
                FileVersion: "1.0.0.0",
                TimestampUtc: DateTimeOffset.UtcNow,
                ModuleList: Array.Empty<string>(),
                SourcePath: "C:/games/StarWarsG.exe");

            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-ghidra",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-24T00:00:00Z",
              "capabilities": [
                {
                  "featureId": "freeze_timer",
                  "available": true,
                  "state": "Verified",
                  "reasonCode": "CAPABILITY_PROBE_PASS",
                  "requiredAnchors": ["freeze_timer_patch"]
                }
              ]
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(mapsRoot, "fp-ghidra.json"), mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "freeze_timer",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "freeze_timer_patch" });

            result.State.Should().Be(SdkCapabilityStatus.Available);
            result.ReasonCode.Should().Be(CapabilityReasonCode.AllRequiredAnchorsPresent);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldFailClosed_WhenCapabilityHintDeclaresUnavailable()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);

        try
        {
            var fingerprint = new BinaryFingerprint(
                FingerprintId: "fp-ghidra-unavailable",
                FileSha256: "abc123",
                ModuleName: "StarWarsG.exe",
                ProductVersion: "1.0",
                FileVersion: "1.0.0.0",
                TimestampUtc: DateTimeOffset.UtcNow,
                ModuleList: Array.Empty<string>(),
                SourcePath: "C:/games/StarWarsG.exe");

            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-ghidra-unavailable",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-24T00:00:00Z",
              "capabilities": [
                {
                  "featureId": "freeze_timer",
                  "available": false,
                  "state": "Unavailable",
                  "reasonCode": "CAPABILITY_REQUIRED_MISSING",
                  "requiredAnchors": ["freeze_timer_patch"]
                }
              ]
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(mapsRoot, "fp-ghidra-unavailable.json"), mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "freeze_timer",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "freeze_timer_patch" });

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
            result.ReasonCode.Should().Be(CapabilityReasonCode.RequiredAnchorsMissing);
            result.MissingAnchors.Should().ContainSingle().Which.Should().Be("freeze_timer_patch");
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }
}
