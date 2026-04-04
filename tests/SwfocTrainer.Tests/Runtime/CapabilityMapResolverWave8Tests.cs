using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 8 branch-coverage tests for CapabilityMapResolver — targets remaining uncovered
/// branches in profile mismatch, universal_auto bypass, custom profile compatibility,
/// anchor resolution, declared unavailable, MapExternalReasonCode, and BuildConfidence.
/// </summary>
public sealed class CapabilityMapResolverWave8Tests : IDisposable
{
    private static readonly BindingFlags NonPublicStatic =
        BindingFlags.Static | BindingFlags.NonPublic;

    private readonly string _tempDir;
    private readonly ILogger<CapabilityMapResolver> _logger;

    public CapabilityMapResolverWave8Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"capmap_wave8_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger = NullLogger<CapabilityMapResolver>.Instance;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* cleanup best-effort */ }
    }

    // ── Null guards ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullMapsRootPath_Throws()
    {
        var act = () => new CapabilityMapResolver(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new CapabilityMapResolver(_tempDir, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_NullFingerprint_Throws()
    {
        var resolver = CreateResolver();
        var act = () => resolver.ResolveAsync(null!, "profile", "op", new HashSet<string>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_NullProfileId_Throws()
    {
        var resolver = CreateResolver();
        var act = () => resolver.ResolveAsync(CreateFingerprint("fp1"), null!, "op", new HashSet<string>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_NullOperationId_Throws()
    {
        var resolver = CreateResolver();
        var act = () => resolver.ResolveAsync(CreateFingerprint("fp1"), "profile", null!, new HashSet<string>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_NullAnchors_Throws()
    {
        var resolver = CreateResolver();
        var act = () => resolver.ResolveAsync(CreateFingerprint("fp1"), "profile", "op", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Map missing ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_MapFileMissing_ReturnsUnavailable()
    {
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("missing"), "profile", "op", new HashSet<string>());
        result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        result.ReasonCode.Should().Be(CapabilityReasonCode.FingerprintMapMissing);
    }

    // ── Invalid JSON ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_InvalidJson_ReturnsUnavailable()
    {
        WriteMap("badjson", "NOT VALID JSON{{{");
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("badjson"), "profile", "op", new HashSet<string>());
        result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        result.ReasonCode.Should().Be(CapabilityReasonCode.FingerprintMapMissing);
    }

    // ── Null deserialization ─────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_NullDeserialization_ReturnsUnavailable()
    {
        WriteMap("nullmap", "null");
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("nullmap"), "profile", "op", new HashSet<string>());
        result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        result.ReasonCode.Should().Be(CapabilityReasonCode.FingerprintMapMissing);
    }

    // ── Profile mismatch ─────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ProfileMismatch_ReturnsUnavailable()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_mismatch",
            DefaultProfileId = "vanilla_swfoc",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1" }, OptionalAnchors = Array.Empty<string>() }
            }
        };
        WriteMap("fp_mismatch", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_mismatch"), "different_profile", "op1", new HashSet<string> { "a1" });
        result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        result.ReasonCode.Should().Be(CapabilityReasonCode.RequestedProfileMismatch);
    }

    // ── universal_auto bypass ────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UniversalAutoProfile_BypassesMismatch()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_univ",
            DefaultProfileId = "vanilla_swfoc",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1" }, OptionalAnchors = Array.Empty<string>() }
            }
        };
        WriteMap("fp_univ", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_univ"), "universal_auto", "op1", new HashSet<string> { "a1" });
        result.State.Should().NotBe(SdkCapabilityStatus.Unavailable);
    }

    // ── Custom profile compatibility ─────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CustomSwfocProfile_Compatible()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_custom",
            DefaultProfileId = "vanilla_swfoc",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1" }, OptionalAnchors = Array.Empty<string>() }
            }
        };
        WriteMap("fp_custom", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_custom"), "custom_thrawn_swfoc", "op1", new HashSet<string> { "a1" });
        result.State.Should().NotBe(SdkCapabilityStatus.Unavailable);
    }

    [Fact]
    public async Task ResolveAsync_CustomSweawProfile_Compatible()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_sweaw_custom",
            DefaultProfileId = "vanilla_sweaw",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1" }, OptionalAnchors = Array.Empty<string>() }
            }
        };
        WriteMap("fp_sweaw_custom", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_sweaw_custom"), "custom_test_sweaw", "op1", new HashSet<string> { "a1" });
        result.State.Should().NotBe(SdkCapabilityStatus.Unavailable);
    }

    // ── Operation not mapped ─────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_OperationNotMapped_ReturnsUnavailable()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_noops",
            DefaultProfileId = "test_profile",
            Operations = new Dictionary<string, object>()
        };
        WriteMap("fp_noops", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_noops"), "test_profile", "missing_op", new HashSet<string>());
        result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        result.ReasonCode.Should().Be(CapabilityReasonCode.OperationNotMapped);
    }

    // ── All required anchors present ─────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_AllAnchorsPresent_ReturnsAvailable()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_ok",
            DefaultProfileId = "test_profile",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1", "a2" }, OptionalAnchors = Array.Empty<string>() }
            }
        };
        WriteMap("fp_ok", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_ok"), "test_profile", "op1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a1", "a2" });
        result.State.Should().Be(SdkCapabilityStatus.Available);
        result.ReasonCode.Should().Be(CapabilityReasonCode.AllRequiredAnchorsPresent);
        result.Confidence.Should().Be(1.0d);
    }

    // ── Required anchors missing ─────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_RequiredAnchorsMissing_ReturnsDegraded()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_missing",
            DefaultProfileId = "test_profile",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1", "a2" }, OptionalAnchors = Array.Empty<string>() }
            }
        };
        WriteMap("fp_missing", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_missing"), "test_profile", "op1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a1" });
        result.State.Should().Be(SdkCapabilityStatus.Degraded);
        result.ReasonCode.Should().Be(CapabilityReasonCode.RequiredAnchorsMissing);
        result.Confidence.Should().BeApproximately(0.5d, 0.01d);
    }

    // ── Optional anchors missing ─────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_OptionalAnchorsMissing_ReturnsDegradedAt085()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_opt",
            DefaultProfileId = "test_profile",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1" }, OptionalAnchors = new[] { "opt1" } }
            }
        };
        WriteMap("fp_opt", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_opt"), "test_profile", "op1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a1" });
        result.State.Should().Be(SdkCapabilityStatus.Degraded);
        result.ReasonCode.Should().Be(CapabilityReasonCode.OptionalAnchorsMissing);
        result.Confidence.Should().Be(0.85d);
    }

    // ── Declared unavailable via hints ────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_DeclaredUnavailableViaHints_ReturnsUnavailable()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_hint",
            DefaultProfileId = "test_profile",
            Operations = new Dictionary<string, object>
            {
                ["op1"] = new { RequiredAnchors = new[] { "a1" }, OptionalAnchors = Array.Empty<string>() }
            },
            Capabilities = new[]
            {
                new { FeatureId = "op1", Available = false, State = "blocked", ReasonCode = "SAFETY_FAIL_CLOSED", RequiredAnchors = new[] { "a1" } }
            }
        };
        WriteMap("fp_hint", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_hint"), "test_profile", "op1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a1" });
        result.State.Should().Be(SdkCapabilityStatus.Unavailable);
    }

    // ── MapExternalReasonCode ────────────────────────────────────────────

    [Theory]
    [InlineData(null, CapabilityReasonCode.Unknown)]
    [InlineData("", CapabilityReasonCode.Unknown)]
    [InlineData("   ", CapabilityReasonCode.Unknown)]
    [InlineData("CAPABILITY_REQUIRED_MISSING", CapabilityReasonCode.RequiredAnchorsMissing)]
    [InlineData("CAPABILITY_PROBE_PASS", CapabilityReasonCode.AllRequiredAnchorsPresent)]
    [InlineData("CAPABILITY_ANCHOR_INVALID", CapabilityReasonCode.RequiredAnchorsMissing)]
    [InlineData("CAPABILITY_ANCHOR_UNREADABLE", CapabilityReasonCode.RequiredAnchorsMissing)]
    [InlineData("CAPABILITY_BACKEND_UNAVAILABLE", CapabilityReasonCode.RuntimeNotAttached)]
    [InlineData("SAFETY_FAIL_CLOSED", CapabilityReasonCode.MutationBlockedByCapabilityState)]
    [InlineData("UNKNOWN_CODE_XYZ", CapabilityReasonCode.Unknown)]
    public void MapExternalReasonCode_AllBranches(string? reasonCode, CapabilityReasonCode expected)
    {
        var method = typeof(CapabilityMapResolver).GetMethod("MapExternalReasonCode", NonPublicStatic)!;
        var result = (CapabilityReasonCode)method.Invoke(null, new object?[] { reasonCode })!;
        result.Should().Be(expected);
    }

    // ── BuildConfidence ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0.50d)]
    [InlineData(0, -1, 0.50d)]
    [InlineData(1, 2, 0.50d)]
    [InlineData(2, 2, 1.0d)]
    [InlineData(0, 2, 0.0d)]
    public void BuildConfidence_EdgeCases(int matched, int total, double expected)
    {
        var method = typeof(CapabilityMapResolver).GetMethod("BuildConfidence", NonPublicStatic)!;
        var result = (double)method.Invoke(null, new object[] { matched, total })!;
        result.Should().BeApproximately(expected, 0.01d);
    }

    // ── ResolveDefaultProfileIdAsync ─────────────────────────────────────

    [Fact]
    public async Task ResolveDefaultProfileIdAsync_MapMissing_ReturnsNull()
    {
        var resolver = CreateResolver();
        var result = await resolver.ResolveDefaultProfileIdAsync(CreateFingerprint("nonexistent"));
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveDefaultProfileIdAsync_MapPresent_ReturnsProfileId()
    {
        var map = new { DefaultProfileId = "my_profile", FingerprintId = "fp_default", Operations = new Dictionary<string, object>() };
        WriteMap("fp_default", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveDefaultProfileIdAsync(CreateFingerprint("fp_default"));
        result.Should().Be("my_profile");
    }

    [Fact]
    public async Task ResolveDefaultProfileIdAsync_NullFingerprint_Throws()
    {
        var resolver = CreateResolver();
        var act = () => resolver.ResolveDefaultProfileIdAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Capabilities-based operation fallback ────────────────────────────

    [Fact]
    public async Task ResolveAsync_NoOperations_FallsBackToCapabilities()
    {
        var map = new
        {
            SchemaVersion = "1.0",
            FingerprintId = "fp_capfallback",
            DefaultProfileId = "test_profile",
            Capabilities = new[]
            {
                new { FeatureId = "op_from_cap", Available = true, State = "active", ReasonCode = "CAPABILITY_PROBE_PASS", RequiredAnchors = new[] { "anchor_a" } }
            }
        };
        WriteMap("fp_capfallback", JsonSerializer.Serialize(map));
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateFingerprint("fp_capfallback"), "test_profile", "op_from_cap",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "anchor_a" });
        result.State.Should().Be(SdkCapabilityStatus.Available);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private CapabilityMapResolver CreateResolver() => new(_tempDir, _logger);

    private void WriteMap(string fingerprintId, string json)
    {
        File.WriteAllText(Path.Join(_tempDir, $"{fingerprintId}.json"), json);
    }

    private static BinaryFingerprint CreateFingerprint(string id)
    {
        return new BinaryFingerprint(id, "abc123", "test.exe", "1.0", "1.0", DateTimeOffset.UtcNow, Array.Empty<string>(), "test.exe");
    }
}
