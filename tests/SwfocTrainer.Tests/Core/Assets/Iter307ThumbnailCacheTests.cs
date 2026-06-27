using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 307, Thread D iter 4 of 5): pin tests for the C# read-side
/// mirror of iter-306 Python thumbnail_cache.py. Locks the cache-key shape
/// (sha256_size.png) so the Python writer + C# reader agree byte-for-byte;
/// any drift on either side breaks these tests.
///
/// Tests use the SWFOC_THUMB_CACHE env var to redirect the cache root to a
/// per-test tmp dir for hermetic isolation. Same env override the Python
/// suite uses (iter-306 _smoke_iter306.py). Pinned to the same xUnit
/// collection as iter-308 tests so concurrent classes don't race on the
/// process-wide env var (catch shipped iter-308 mid-iter; would have
/// silently shipped a flaky test if iter-307 had a sibling class touching
/// the env at the same time).
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter307ThumbnailCacheTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string? _origEnv;

    public Iter307ThumbnailCacheTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_thumb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _origEnv = Environment.GetEnvironmentVariable("SWFOC_THUMB_CACHE");
        Environment.SetEnvironmentVariable("SWFOC_THUMB_CACHE", _tmpDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SWFOC_THUMB_CACHE", _origEnv);
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    [Fact]
    public void CacheRoot_RespectsEnvOverride()
    {
        ThumbnailCache.CacheRoot().Should().Be(_tmpDir,
            because: "SWFOC_THUMB_CACHE env var must take precedence over %LOCALAPPDATA%");
    }

    [Fact]
    public void ComputeCacheFilename_FormatMatchesPythonWriter()
    {
        var ddsPath = WriteFakeDds(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var expectedHash = ComputeExpectedSha256Hex(ddsPath);

        var name = ThumbnailCache.ComputeCacheFilename(ddsPath, size: 64);

        name.Should().Be($"{expectedHash}_64.png",
            because: "the Python writer at iter-306 emits this exact format; drift breaks interop");
    }

    [Theory]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(96)]
    [InlineData(128)]
    [InlineData(256)]
    public void ComputeCacheFilename_AcceptsSupportedSizes(int size)
    {
        var ddsPath = WriteFakeDds(new byte[] { 0x01 });
        var act = () => ThumbnailCache.ComputeCacheFilename(ddsPath, size);
        act.Should().NotThrow(because: "Python iter-306 SUPPORTED_SIZES must match this list exactly");
    }

    [Theory]
    [InlineData(33)]
    [InlineData(65)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(512)]
    public void ComputeCacheFilename_RejectsUnsupportedSize(int size)
    {
        var ddsPath = WriteFakeDds(new byte[] { 0x01 });
        var act = () => ThumbnailCache.ComputeCacheFilename(ddsPath, size);
        act.Should().Throw<ArgumentException>(
            because: "iter-306 Python ValueError must mirror as C# ArgumentException");
    }

    [Fact]
    public void ComputeCacheFilename_ThrowsWhenDdsMissing()
    {
        var ghost = Path.Combine(_tmpDir, "nonexistent.dds");
        var act = () => ThumbnailCache.ComputeCacheFilename(ghost);
        act.Should().Throw<FileNotFoundException>(
            because: "missing DDS must surface explicitly, mirroring Python iter-306");
    }

    [Fact]
    public void TryGetCachedPath_ReturnsFalse_OnCacheMiss()
    {
        var ddsPath = WriteFakeDds(new byte[] { 0xCA, 0xFE });
        var ok = ThumbnailCache.TryGetCachedPath(ddsPath, 64, out var path);
        ok.Should().BeFalse(because: "no cache file has been written yet");
        path.Should().BeEmpty();
    }

    [Fact]
    public void TryGetCachedPath_ReturnsTrue_WhenPythonWriterSeededCache()
    {
        var ddsPath = WriteFakeDds(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });
        var expectedFilename = ThumbnailCache.ComputeCacheFilename(ddsPath, 64);
        // Simulate the Python writer having dropped a PNG into the cache dir.
        // Real Pillow output isn't needed for the lookup test — content can be
        // any bytes; the lookup only checks file existence.
        var cachedPng = Path.Combine(_tmpDir, expectedFilename);
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var ok = ThumbnailCache.TryGetCachedPath(ddsPath, 64, out var path);

        ok.Should().BeTrue(because: "cache file exists at the SHA256-keyed location");
        path.Should().Be(cachedPng,
            because: "TryGetCachedPath must return the exact path the Python writer used");
    }

    [Fact]
    public void TryGetCachedPath_ReturnsFalse_WhenDdsDoesNotExist()
    {
        var ghost = Path.Combine(_tmpDir, "missing.dds");
        var ok = ThumbnailCache.TryGetCachedPath(ghost, 64, out var path);
        ok.Should().BeFalse(because: "missing DDS = no cache lookup possible");
        path.Should().BeEmpty();
    }

    [Fact]
    public void TryGetCachedPath_DifferentSizes_MapToDifferentFiles()
    {
        var ddsPath = WriteFakeDds(new byte[] { 0xAA, 0xBB });
        var name32 = ThumbnailCache.ComputeCacheFilename(ddsPath, 32);
        var name64 = ThumbnailCache.ComputeCacheFilename(ddsPath, 64);
        name32.Should().NotBe(name64,
            because: "size suffix in the cache filename keeps multiple thumbnail sizes addressable side-by-side");
        name32.Should().EndWith("_32.png");
        name64.Should().EndWith("_64.png");
    }

    [Fact]
    public void TryGetCachedPath_DifferentDdsContents_ProduceDifferentKeys()
    {
        var ddsPath1 = WriteFakeDds(new byte[] { 0x01, 0x02, 0x03 });
        var ddsPath2 = WriteFakeDds(new byte[] { 0x04, 0x05, 0x06 });

        var name1 = ThumbnailCache.ComputeCacheFilename(ddsPath1, 64);
        var name2 = ThumbnailCache.ComputeCacheFilename(ddsPath2, 64);

        name1.Should().NotBe(name2,
            because: "SHA256 of DDS bytes differs => cache filename must differ (no key collision)");
    }

    [Fact]
    public void GetCachedPathOrNull_ReturnsNull_OnCacheMiss()
    {
        var ddsPath = WriteFakeDds(new byte[] { 0xFF });
        ThumbnailCache.GetCachedPathOrNull(ddsPath).Should().BeNull(
            because: "convenience wrapper for WPF binding — null binding hides the icon naturally");
    }

    [Fact]
    public void GetCachedPathOrNull_ReturnsPath_OnCacheHit()
    {
        var ddsPath = WriteFakeDds(new byte[] { 0xFF, 0xEE });
        var expectedFilename = ThumbnailCache.ComputeCacheFilename(ddsPath, 64);
        var cachedPng = Path.Combine(_tmpDir, expectedFilename);
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        ThumbnailCache.GetCachedPathOrNull(ddsPath).Should().Be(cachedPng);
    }

    private string WriteFakeDds(byte[] bytes)
    {
        var path = Path.Combine(_tmpDir,
            $"fake_{Guid.NewGuid():N}.dds");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string ComputeExpectedSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
