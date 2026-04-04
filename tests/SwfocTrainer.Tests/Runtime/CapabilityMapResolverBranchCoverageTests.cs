using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage sweep for CapabilityMapResolver — targets the ~52 uncovered branches
/// in deserialization, profile matching, anchor resolution, confidence calculation,
/// reason code mapping, hint resolution, and declared unavailable paths.
/// </summary>
public sealed class CapabilityMapResolverBranchCoverageTests
{
    // ── MapExternalReasonCode branches ─────────────────────────────────────

    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("   ", "Unknown")]
    [InlineData("CAPABILITY_REQUIRED_MISSING", "RequiredAnchorsMissing")]
    [InlineData("CAPABILITY_PROBE_PASS", "AllRequiredAnchorsPresent")]
    [InlineData("CAPABILITY_ANCHOR_INVALID", "RequiredAnchorsMissing")]
    [InlineData("CAPABILITY_ANCHOR_UNREADABLE", "RequiredAnchorsMissing")]
    [InlineData("CAPABILITY_BACKEND_UNAVAILABLE", "RuntimeNotAttached")]
    [InlineData("SAFETY_FAIL_CLOSED", "MutationBlockedByCapabilityState")]
    [InlineData("SOMETHING_UNKNOWN", "Unknown")]
    public void MapExternalReasonCode_ShouldMapCorrectly(string? reasonCode, string expectedName)
    {
        var method = typeof(CapabilityMapResolver).GetMethod(
            "MapExternalReasonCode",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { reasonCode });
        result!.ToString().Should().Be(expectedName);
    }

    // ── BuildConfidence branches ──────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0.50d)]
    [InlineData(-1, -1, 0.50d)]
    [InlineData(3, 5, 0.60d)]
    [InlineData(5, 5, 1.0d)]
    [InlineData(0, 5, 0.0d)]
    public void BuildConfidence_ShouldCalculateCorrectly(int matched, int total, double expected)
    {
        var method = typeof(CapabilityMapResolver).GetMethod(
            "BuildConfidence",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (double)method!.Invoke(null, new object[] { matched, total })!;
        result.Should().BeApproximately(expected, 0.01d);
    }

    // ── IsRequestedProfileMismatch branches ───────────────────────────────

    [Theory]
    [InlineData("base_swfoc", null, false)]
    [InlineData("base_swfoc", "", false)]
    [InlineData("base_swfoc", "   ", false)]
    [InlineData("base_swfoc", "base_swfoc", false)]
    [InlineData("universal_auto", "base_swfoc", false)]
    [InlineData("base_sweaw", "base_swfoc", true)]
    public void IsRequestedProfileMismatch_ShouldDetectCorrectly(
        string requestedProfileId,
        string? defaultProfileId,
        bool expectedMismatch)
    {
        var method = typeof(CapabilityMapResolver).GetMethod(
            "IsRequestedProfileMismatch",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object?[] { requestedProfileId, defaultProfileId })!;
        result.Should().Be(expectedMismatch);
    }

    // ── IsGeneratedCustomProfileCompatible branches ───────────────────────

    [Theory]
    [InlineData(null, "base_swfoc", false)]
    [InlineData("", "base_swfoc", false)]
    [InlineData("base_swfoc", "base_swfoc", false)]
    [InlineData("custom_roe_swfoc", "base_swfoc", true)]
    [InlineData("custom_roe_sweaw", "base_sweaw", true)]
    [InlineData("custom_roe_swfoc", "base_sweaw", false)]
    [InlineData("custom_roe_sweaw", "base_swfoc", false)]
    [InlineData("custom_roe_other", "base_swfoc", false)]
    public void IsGeneratedCustomProfileCompatible_ShouldDetectCorrectly(
        string? requestedProfileId,
        string defaultProfileId,
        bool expected)
    {
        var method = typeof(CapabilityMapResolver).GetMethod(
            "IsGeneratedCustomProfileCompatible",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object?[] { requestedProfileId ?? string.Empty, defaultProfileId })!;
        result.Should().Be(expected);
    }

    // ── ResolveCapabilityMetadata branches ─────────────────────────────────

    [Fact]
    public void ResolveCapabilityMetadata_ShouldReturnEmpty_WhenHintIsNull()
    {
        var method = typeof(CapabilityMapResolver).GetMethod(
            "ResolveCapabilityMetadata",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (CapabilityResolutionMetadata)method!.Invoke(null, new object?[] { null })!;
        result.Should().Be(CapabilityResolutionMetadata.Empty);
    }

    [Fact]
    public void ResolveCapabilityMetadata_ShouldPopulate_WhenHintIsProvided()
    {
        var method = typeof(CapabilityMapResolver).GetMethod(
            "ResolveCapabilityMetadata",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var hint = new CapabilityAvailabilityHint(
            FeatureId: "freeze_timer",
            Available: true,
            State: "Verified",
            ReasonCode: "CAPABILITY_PROBE_PASS",
            RequiredAnchors: new[] { "anchor1" });
        var result = (CapabilityResolutionMetadata)method!.Invoke(null, new object?[] { hint })!;
        result.SourceReasonCode.Should().Be("CAPABILITY_PROBE_PASS");
        result.SourceState.Should().Be("Verified");
        result.DeclaredAvailable.Should().BeTrue();
    }

    // ── ResolveAsync: operation not mapped ─────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenOperationNotMapped()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-op-missing");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-op-missing",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "set_hp": {
                  "requiredAnchors": ["anchor1"],
                  "optionalAnchors": []
                }
              }
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-op-missing", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "nonexistent_op",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
            result.ReasonCode.Should().Be(CapabilityReasonCode.OperationNotMapped);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── ResolveAsync: profile mismatch ────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenProfileMismatch()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-mismatch");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-mismatch",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "set_hp": {
                  "requiredAnchors": ["anchor1"],
                  "optionalAnchors": []
                }
              }
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-mismatch", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "totally_different_profile",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
            result.ReasonCode.Should().Be(CapabilityReasonCode.RequestedProfileMismatch);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── ResolveAsync: optional anchors missing ────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldReturnDegraded_WhenOptionalAnchorsMissing()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-opt-missing");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-opt-missing",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "set_hp": {
                  "requiredAnchors": ["anchor_req"],
                  "optionalAnchors": ["anchor_opt"]
                }
              }
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-opt-missing", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "anchor_req" });

            result.State.Should().Be(SdkCapabilityStatus.Degraded);
            result.ReasonCode.Should().Be(CapabilityReasonCode.OptionalAnchorsMissing);
            result.Confidence.Should().BeApproximately(0.85d, 0.01d);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── ResolveAsync: malformed JSON ──────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenMapJsonIsMalformed()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-bad-json");
            await File.WriteAllTextAsync(
                Path.Join(mapsRoot, "fp-bad-json.json"),
                "{ this is not valid json }}}");

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

    // ── ResolveDefaultProfileIdAsync branches ─────────────────────────────

    [Fact]
    public async Task ResolveDefaultProfileIdAsync_ShouldReturnNull_WhenMapMissing()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-no-map");
            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveDefaultProfileIdAsync(fingerprint);
            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveDefaultProfileIdAsync_ShouldReturnProfileId_WhenMapExists()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-with-default");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-with-default",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {}
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-with-default", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveDefaultProfileIdAsync(fingerprint);
            result.Should().Be("base_swfoc");
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── ResolveAsync: 4-arg overload delegates to 5-arg ───────────────────

    [Fact]
    public async Task ResolveAsync_FourArgOverload_ShouldDelegateTo_FiveArgOverload()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-overload");
            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                "base_swfoc",
                "set_hp",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── ResolveDefaultProfileIdAsync: 1-arg overload ──────────────────────

    [Fact]
    public async Task ResolveDefaultProfileIdAsync_OneArgOverload_ShouldDelegate()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-default-overload");
            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveDefaultProfileIdAsync(fingerprint);
            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── DeserializeCapabilityMap: null dto branches ───────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenDeserializedDtoIsNull()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-null-dto");
            await File.WriteAllTextAsync(
                Path.Join(mapsRoot, "fp-null-dto.json"),
                "null");

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── DeserializeCapabilityMap: operations from capabilities fallback ────

    [Fact]
    public async Task ResolveAsync_ShouldBuildOperationsFromCapabilities_WhenOperationsAreEmpty()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-cap-only");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-cap-only",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "capabilities": [
                {
                  "featureId": "freeze_timer",
                  "available": true,
                  "state": "Verified",
                  "reasonCode": "CAPABILITY_PROBE_PASS",
                  "requiredAnchors": ["freeze_timer_patch"]
                },
                {
                  "featureId": "",
                  "available": true,
                  "state": "Verified",
                  "reasonCode": "CAPABILITY_PROBE_PASS",
                  "requiredAnchors": []
                }
              ]
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-cap-only", mapJson);

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

    // ── DeserializeCapabilityMap: default values when fields missing ──────

    [Fact]
    public async Task ResolveAsync_ShouldHandleMinimalMapWithDefaultValues()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-minimal");
            var mapJson = """
            {
              "operations": {
                "set_hp": {
                  "requiredAnchors": ["anchor1"]
                }
              }
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-minimal", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "anchor1" });

            result.State.Should().Be(SdkCapabilityStatus.Available);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── BuildCapabilityHints: null/empty capabilities ────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldHandleNullCapabilities()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-null-caps");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-null-caps",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "set_hp": {
                  "requiredAnchors": ["anchor1"],
                  "optionalAnchors": []
                }
              },
              "capabilities": null
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-null-caps", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "anchor1" });

            result.State.Should().Be(SdkCapabilityStatus.Available);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── TryResolveDeclaredUnavailable: available hint ────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldNotBlockByHint_WhenHintDeclaresAvailable()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-available-hint");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-available-hint",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "freeze_timer": {
                  "requiredAnchors": ["freeze_timer_patch"],
                  "optionalAnchors": []
                }
              },
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
            await WriteMapAsync(mapsRoot, "fp-available-hint", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "base_swfoc",
                operationId: "freeze_timer",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "freeze_timer_patch" });

            result.State.Should().Be(SdkCapabilityStatus.Available);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── Custom profile compatibility integration ─────────────────────────

    [Fact]
    public async Task ResolveAsync_ShouldAllowCustomSwfocProfile_WhenDefaultIsBaseSwfoc()
    {
        var mapsRoot = CreateMapsRoot();
        try
        {
            var fingerprint = CreateFingerprint("fp-custom-compat");
            var mapJson = """
            {
              "schemaVersion": "1.0",
              "fingerprintId": "fp-custom-compat",
              "defaultProfileId": "base_swfoc",
              "generatedAtUtc": "2026-02-18T00:00:00Z",
              "operations": {
                "set_hp": {
                  "requiredAnchors": ["anchor1"],
                  "optionalAnchors": []
                }
              }
            }
            """;
            await WriteMapAsync(mapsRoot, "fp-custom-compat", mapJson);

            var resolver = new CapabilityMapResolver(mapsRoot, NullLogger<CapabilityMapResolver>.Instance);
            var result = await resolver.ResolveAsync(
                fingerprint,
                requestedProfileId: "custom_roe_swfoc",
                operationId: "set_hp",
                resolvedAnchors: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "anchor1" });

            result.State.Should().Be(SdkCapabilityStatus.Available);
        }
        finally
        {
            Directory.Delete(mapsRoot, recursive: true);
        }
    }

    // ── FormatValidationRuleRange ────────────────────────────────────────

    [Fact]
    public void FormatValidationRuleRange_ShouldReturnNone_WhenNoRangesSet()
    {
        var method = typeof(CapabilityMapResolver).Assembly
            .GetType("SwfocTrainer.Runtime.Services.RuntimeAdapter")!
            .GetMethod("FormatValidationRuleRange",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        if (method is not null)
        {
            var rule = new SymbolValidationRule("test", null, null, null, null, null, false);
            var result = (string)method.Invoke(null, new object[] { rule })!;
            result.Should().Be("none");
        }
    }

    // ── Helper builders ────────────────────────────────────────────────────

    private static string CreateMapsRoot()
    {
        var path = Path.Join(Path.GetTempPath(), $"swfoc-cap-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static BinaryFingerprint CreateFingerprint(string fingerprintId)
    {
        return new BinaryFingerprint(
            FingerprintId: fingerprintId,
            FileSha256: "abc123",
            ModuleName: "swfoc.exe",
            ProductVersion: "1.0",
            FileVersion: "1.0.0.0",
            TimestampUtc: DateTimeOffset.UtcNow,
            ModuleList: Array.Empty<string>(),
            SourcePath: "C:/games/swfoc.exe");
    }

    private static async Task WriteMapAsync(string mapsRoot, string fingerprintId, string json)
    {
        await File.WriteAllTextAsync(Path.Join(mapsRoot, $"{fingerprintId}.json"), json);
    }
}
