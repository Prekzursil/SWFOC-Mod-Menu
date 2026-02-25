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
        var mapsRoot = CreateMapsRoot();

        try
        {
            var fingerprint = CreateFingerprint("fp-test", "swfoc.exe", "C:/games/swfoc.exe");

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

            await WriteMapAsync(mapsRoot, "fp-test", mapJson);

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
        var mapsRoot = CreateMapsRoot();

        try
        {
            var fingerprint = CreateFingerprint("fp-test", "swfoc.exe", "C:/games/swfoc.exe");

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
            await WriteMapAsync(mapsRoot, "fp-test", mapJson);

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
        var mapsRoot = CreateMapsRoot();

        try
        {
            var fingerprint = CreateFingerprint("fp-ghidra", "StarWarsG.exe", "C:/games/StarWarsG.exe");

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
            await WriteMapAsync(mapsRoot, "fp-ghidra", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "freeze_timer",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "freeze_timer_patch" });

            result.State.Should().Be(SdkCapabilityStatus.Available);
            result.ReasonCode.Should().Be(CapabilityReasonCode.AllRequiredAnchorsPresent);
            result.Metadata.SourceReasonCode.Should().Be("CAPABILITY_PROBE_PASS");
            result.Metadata.SourceState.Should().Be("Verified");
            result.Metadata.DeclaredAvailable.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldPreserveUnavailableHintMetadata_WhenCapabilityIsDeclaredUnavailable()
    {
        var mapsRoot = CreateMapsRoot();

        try
        {
            var result = await ResolveUnavailableFreezeTimerCapabilityAsync(mapsRoot, "fp-ghidra");

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
            result.ReasonCode.Should().Be(CapabilityReasonCode.RequiredAnchorsMissing);
            result.Metadata.SourceReasonCode.Should().Be("CAPABILITY_REQUIRED_MISSING");
            result.Metadata.SourceState.Should().Be("Unavailable");
            result.Metadata.DeclaredAvailable.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldFailClosed_WhenCapabilityHintDeclaresUnavailable()
    {
        var mapsRoot = CreateMapsRoot();

        try
        {
            var result = await ResolveUnavailableFreezeTimerCapabilityAsync(mapsRoot, "fp-ghidra-unavailable");

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
            result.ReasonCode.Should().Be(CapabilityReasonCode.RequiredAnchorsMissing);
            result.MissingAnchors.Should().ContainSingle().Which.Should().Be("freeze_timer_patch");
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_ShouldAllow_CustomSwfoc_Profile_When_DefaultProfile_Is_BaseSwfoc()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);

        try
        {
            var fingerprint = new BinaryFingerprint(
                FingerprintId: "fp-custom-profile",
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
              "fingerprintId": "fp-custom-profile",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-24T00:00:00Z",
              "operations": {
                "freeze_timer": {
                  "requiredAnchors": ["freeze_timer_patch"],
                  "optionalAnchors": []
                }
              }
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(mapsRoot, "fp-custom-profile.json"), mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "custom_enhanced_conflict_aotr_3664004146_swfoc",
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
    public async Task ResolveAsync_ShouldReject_CustomSwfoc_Profile_When_DefaultProfile_Is_Sweaw()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);

        try
        {
            var fingerprint = new BinaryFingerprint(
                FingerprintId: "fp-custom-profile-mismatch",
                FileSha256: "abc123",
                ModuleName: "sweaw.exe",
                ProductVersion: "1.0",
                FileVersion: "1.0.0.0",
                TimestampUtc: DateTimeOffset.UtcNow,
                ModuleList: Array.Empty<string>(),
                SourcePath: "C:/games/sweaw.exe");

            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-custom-profile-mismatch",
              "defaultProfileId": "base_sweaw",
              "generatedAtUtc": "2026-02-24T00:00:00Z",
              "operations": {
                "freeze_timer": {
                  "requiredAnchors": ["freeze_timer_patch"],
                  "optionalAnchors": []
                }
              }
            }
            """;
            await File.WriteAllTextAsync(Path.Combine(mapsRoot, "fp-custom-profile-mismatch.json"), mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "custom_enhanced_conflict_aotr_3664004146_swfoc",
                operationId: "freeze_timer",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "freeze_timer_patch" });

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
            result.ReasonCode.Should().Be(CapabilityReasonCode.RequestedProfileMismatch);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    private static string CreateMapsRoot()
    {
        var mapsRoot = Path.Combine(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mapsRoot);
        return mapsRoot;
    }

    private static BinaryFingerprint CreateFingerprint(string fingerprintId, string moduleName, string sourcePath)
    {
        return new BinaryFingerprint(
            FingerprintId: fingerprintId,
            FileSha256: "abc123",
            ModuleName: moduleName,
            ProductVersion: "1.0",
            FileVersion: "1.0.0.0",
            TimestampUtc: DateTimeOffset.UtcNow,
            ModuleList: Array.Empty<string>(),
            SourcePath: sourcePath);
    }

    private static Task WriteMapAsync(string mapsRoot, string fingerprintId, string mapJson)
    {
        return File.WriteAllTextAsync(Path.Combine(mapsRoot, $"{fingerprintId}.json"), mapJson);
    }

    private static async Task<CapabilityResolutionResult> ResolveUnavailableFreezeTimerCapabilityAsync(
        string mapsRoot,
        string fingerprintId)
    {
        var fingerprint = CreateFingerprint(fingerprintId, "StarWarsG.exe", "C:/games/StarWarsG.exe");
        await WriteMapAsync(mapsRoot, fingerprintId, BuildUnavailableFreezeTimerMapJson(fingerprintId));

        var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
        return await resolver.ResolveAsync(
            fingerprint,
            requestedProfileId: "base_swfoc",
            operationId: "freeze_timer",
            resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "freeze_timer_patch" });
    }

    private static string BuildUnavailableFreezeTimerMapJson(string fingerprintId)
    {
        return $$"""
        {
          "schemaVersion": "1.0",
          "fingerprintId": "{{fingerprintId}}",
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
    }
}
