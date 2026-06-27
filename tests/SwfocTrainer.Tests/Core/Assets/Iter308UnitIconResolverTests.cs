using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 308, Thread D arc FINALE): pin tests for the
/// <see cref="UnitIconResolver"/> service that maps unit-type names to
/// cached thumbnail PNG paths via the iter-307 ThumbnailCache lookup.
///
/// Uses the same SWFOC_THUMB_CACHE env override pattern as iter-307 so
/// per-test cache contents stay hermetic. xUnit collection pin serializes
/// against Iter308SpawningRowCollectionTests so the process-wide env var
/// doesn't race between the two classes (iter-307 test class only sets the
/// env in its OWN tests, but iter-308 has two classes that both touch it).
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter308UnitIconResolverTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter308UnitIconResolverTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_dds_root_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_ddsRoot);
        Directory.CreateDirectory(_cacheDir);
        _origCacheEnv = Environment.GetEnvironmentVariable("SWFOC_THUMB_CACHE");
        Environment.SetEnvironmentVariable("SWFOC_THUMB_CACHE", _cacheDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SWFOC_THUMB_CACHE", _origCacheEnv);
        try { Directory.Delete(_ddsRoot, recursive: true); } catch { }
        try { Directory.Delete(_cacheDir, recursive: true); } catch { }
    }

    [Fact]
    public void Resolve_NullRoot_AlwaysReturnsNull()
    {
        var resolver = new UnitIconResolver(null);
        resolver.Resolve("Empire_AT_AT").Should().BeNull(
            because: "no root configured = no icons; graceful operator UX (no broken-image placeholder)");
    }

    [Fact]
    public void Resolve_EmptyOrWhitespaceRoot_AlwaysReturnsNull()
    {
        new UnitIconResolver(string.Empty).Resolve("X").Should().BeNull();
        new UnitIconResolver("   ").Resolve("X").Should().BeNull();
    }

    [Fact]
    public void Resolve_NonExistentRoot_ReturnsNull()
    {
        var resolver = new UnitIconResolver(Path.Combine(_ddsRoot, "does_not_exist"));
        resolver.Resolve("Empire_AT_AT").Should().BeNull();
    }

    [Fact]
    public void Resolve_EmptyUnitName_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.Resolve(string.Empty).Should().BeNull();
        resolver.Resolve("   ").Should().BeNull();
    }

    public static IEnumerable<object[]> CandidateRelPaths => new[]
    {
        new object[] { new[] { "Data", "Art", "Textures", "Units" } },
        new object[] { new[] { "Data", "Art", "Textures" } },
        new object[] { new[] { "Art", "Textures", "Units" } },
        new object[] { new[] { "Art", "Textures" } },
        new object[] { Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(CandidateRelPaths))]
    public void LocateDds_FindsDdsAtAnyCandidatePath(string[] segments)
    {
        // Build the per-test DDS dir via Path.Combine so the expected path
        // uses the platform-native separator (matches what the source's
        // Path.Combine-based candidate-relpath produces).
        var ddsDir = segments.Length == 0
            ? _ddsRoot
            : Path.Combine(new[] { _ddsRoot }.Concat(segments).ToArray());
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_Empire_AT_AT.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xDD, 0x50 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.LocateDds("Empire_AT_AT");
        actual.Should().NotBeNull();
        // Canonicalize both sides via GetFullPath so any leftover separator
        // differences (Windows accepts both \ and /) are normalized.
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(ddsPath),
            because: "DDS under " + string.Join("/", segments) + " should be found by the candidate-relpath walk");
    }

    [Fact]
    public void LocateDds_PrefersFirstMatch_DataArtTexturesUnitsWinsOverFallbacks()
    {
        // Drop a DDS file in BOTH the highest-priority slot AND a fallback.
        // The first slot in the candidate-relpath list (Data/Art/Textures/Units)
        // should win.
        var preferred = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        var fallback = Path.Combine(_ddsRoot, "Data", "Art", "Textures");
        Directory.CreateDirectory(preferred);
        Directory.CreateDirectory(fallback);
        var preferredPath = Path.Combine(preferred, "i_button_Rebel_Trooper.dds");
        var fallbackPath = Path.Combine(fallback, "i_button_Rebel_Trooper.dds");
        File.WriteAllBytes(preferredPath, new byte[] { 0x01 });
        File.WriteAllBytes(fallbackPath, new byte[] { 0x02 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.LocateDds("Rebel_Trooper");
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(preferredPath),
            because: "the higher-priority candidate path wins");
    }

    [Fact]
    public void LocateDds_DdsNotPresent_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateDds("This_Unit_Does_Not_Exist_Anywhere").Should().BeNull();
    }

    [Fact]
    public void Resolve_DdsExists_ButCacheNotPopulated_ReturnsNull()
    {
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_Some_Unit.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xAA, 0xBB });

        var resolver = new UnitIconResolver(_ddsRoot);
        // No PNG dropped into the cache yet — Python writer hasn't run.
        resolver.Resolve("Some_Unit").Should().BeNull(
            because: "DDS exists but iter-306 Python writer hasn't generated the cached PNG yet");
    }

    [Fact]
    public void Resolve_DdsExists_AndCachePopulated_ReturnsCachedPath()
    {
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_Empire_AT_AT.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });

        // Simulate the Python writer having dropped a PNG with the
        // SHA256-keyed name iter-307 expects.
        var expectedFilename = ThumbnailCache.ComputeCacheFilename(ddsPath, 32);
        var cachedPng = Path.Combine(_cacheDir, expectedFilename);
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.Resolve("Empire_AT_AT", size: 32);
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cachedPng),
            because: "with DDS + cache both present, resolver returns the cached PNG path for WPF binding");
    }

    [Fact]
    public void Resolve_UnsupportedSize_ReturnsNull_DoesNotThrow()
    {
        var ddsDir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(ddsDir);
        var ddsPath = Path.Combine(ddsDir, "i_button_X.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        // size=33 is invalid (not in iter-307 SupportedSizes). Resolver
        // catches the ArgumentException internally and returns null so
        // the VM doesn't have to wrap every call in try/catch.
        var act = () => resolver.Resolve("X", size: 33);
        act.Should().NotThrow();
        resolver.Resolve("X", size: 33).Should().BeNull();
    }
}
