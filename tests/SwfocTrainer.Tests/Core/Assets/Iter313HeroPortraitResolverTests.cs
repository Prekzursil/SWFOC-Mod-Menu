using FluentAssertions;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 313, Thread D arc post-finale 3/3): pin tests for the
/// <see cref="UnitIconResolver.ResolvePortrait"/> + <see cref="UnitIconResolver.LocatePortraitDds"/>
/// extension. Validates the i_portrait_*.dds convention shipping alongside
/// the iter-308 i_button_*.dds convention via the shared LocateByConvention
/// helper.
///
/// Pinned to the same xUnit collection as iter-307+308+312 because all
/// touch SWFOC_THUMB_CACHE during cache lookup.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter313HeroPortraitResolverTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter313HeroPortraitResolverTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter313_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter313_cache_{Guid.NewGuid():N}");
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
    public void ResolvePortrait_NullRoot_ReturnsNull()
    {
        var resolver = new UnitIconResolver(null);
        resolver.ResolvePortrait("Han_Solo").Should().BeNull(
            because: "no root configured = no portraits; graceful operator UX");
    }

    [Fact]
    public void ResolvePortrait_EmptyHeroName_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolvePortrait(string.Empty).Should().BeNull();
        resolver.ResolvePortrait("   ").Should().BeNull();
    }

    [Fact]
    public void LocatePortraitDds_FindsAtCanonicalPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_portrait_Han_Solo.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xDD });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.LocatePortraitDds("Han_Solo");
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(ddsPath),
            because: "iter-313 portrait convention finds i_portrait_<name>.dds at the same 5-relpath walk as iter-308");
    }

    [Fact]
    public void LocatePortraitDds_DoesNotMatchUnitIconConvention()
    {
        // Verify the new portrait wrapper doesn't accidentally match iter-308's
        // i_button_*.dds files — the prefix discriminator must be exact.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_Han_Solo.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocatePortraitDds("Han_Solo").Should().BeNull(
            because: "i_button_* must NOT satisfy ResolvePortrait — would surface unit icons as hero portraits and confuse operators");
    }

    [Fact]
    public void LocateDds_DoesNotMatchPortraitConvention()
    {
        // Reverse symmetry — i_portrait_* must NOT satisfy LocateDds either.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_Han_Solo.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateDds("Han_Solo").Should().BeNull(
            because: "i_portrait_* is the hero convention, not the unit-icon one — symmetric to the previous test");
    }

    [Fact]
    public void ResolvePortrait_DdsExists_AndCachePopulated_ReturnsCachedPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_portrait_Luke_Skywalker.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 64));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolvePortrait("Luke_Skywalker", size: 64);
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cachedPng),
            because: "happy path — DDS + cache present at default-64-size lookup");
    }

    [Fact]
    public void ResolvePortrait_DefaultSize_Is64_NotUnit32()
    {
        // Hero portraits render larger than unit icons by convention. Default
        // size 64 instead of 32 reflects that. Pin the default explicitly so
        // a future refactor doesn't silently match the unit-icon default.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_portrait_X.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA });
        // Drop a cache PNG at the size-64 path — if the default is 32 the lookup
        // misses; if the default is 64 it hits.
        var cached64 = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 64));
        File.WriteAllBytes(cached64, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolvePortrait("X");  // no size arg = default
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cached64),
            because: "ResolvePortrait default size must be 64 — pin it so future refactor doesn't drift to 32");
    }

    [Fact]
    public void ResolvePortrait_DdsExists_CacheMissing_ReturnsNull()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_Vader.dds"), new byte[] { 0xCA });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolvePortrait("Vader").Should().BeNull(
            because: "DDS exists but cache PNG missing — same graceful-null contract as iter-308 ResolveUnitIcon");
    }

    [Fact]
    public void ResolvePortrait_UnsupportedSize_ReturnsNull_DoesNotThrow()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_Y.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var act = () => resolver.ResolvePortrait("Y", size: 33);
        act.Should().NotThrow(because: "ArgumentException from ThumbnailCache.ValidateSize is caught internally");
        resolver.ResolvePortrait("Y", size: 33).Should().BeNull();
    }

    [Fact]
    public void LocatePortraitDds_NotPresent_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocatePortraitDds("Hero_That_Does_Not_Exist").Should().BeNull(
            because: "missing DDS → null, mirrors iter-308 LocateDds contract exactly");
    }
}
