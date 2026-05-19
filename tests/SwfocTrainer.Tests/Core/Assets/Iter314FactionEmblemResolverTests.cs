using FluentAssertions;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 314, Thread D arc post-finale 4/?): pin tests for the
/// <see cref="UnitIconResolver.ResolveFactionEmblem"/> + <see cref="UnitIconResolver.LocateFactionEmblemDds"/>
/// extension. Validates the i_faction_*.dds convention as the 3rd asset
/// class served by the shared LocateByConvention helper. Format mirrors
/// iter-313 hero portrait tests exactly so the pattern is self-documenting.
///
/// Pinned to the same xUnit collection as iter-307+308+312+313 for env-var
/// orthogonality on SWFOC_THUMB_CACHE.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter314FactionEmblemResolverTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter314FactionEmblemResolverTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter314_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter314_cache_{Guid.NewGuid():N}");
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
    public void ResolveFactionEmblem_NullRoot_ReturnsNull()
    {
        var resolver = new UnitIconResolver(null);
        resolver.ResolveFactionEmblem("EMPIRE").Should().BeNull(
            because: "no root configured = no emblems; graceful operator UX");
    }

    [Fact]
    public void ResolveFactionEmblem_EmptyFactionName_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolveFactionEmblem(string.Empty).Should().BeNull();
        resolver.ResolveFactionEmblem("   ").Should().BeNull();
    }

    [Fact]
    public void LocateFactionEmblemDds_FindsAtCanonicalPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_faction_EMPIRE.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xDD });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.LocateFactionEmblemDds("EMPIRE");
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(ddsPath),
            because: "iter-314 faction convention finds i_faction_<name>.dds at the same 5-relpath walk as iter-308 + iter-313");
    }

    [Fact]
    public void LocateFactionEmblemDds_DoesNotMatchUnitIcon_NorPortrait()
    {
        // Triple-discriminator pin: i_button_* and i_portrait_* must NOT
        // satisfy ResolveFactionEmblem. Any cross-class match would silently
        // surface the wrong image and confuse operators.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_EMPIRE.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_EMPIRE.dds"), new byte[] { 0x02 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateFactionEmblemDds("EMPIRE").Should().BeNull(
            because: "neither i_button_EMPIRE.dds nor i_portrait_EMPIRE.dds may satisfy ResolveFactionEmblem — strict 3-way discriminator");
    }

    [Fact]
    public void LocateDds_AndLocatePortraitDds_DoNotMatchFactionConvention()
    {
        // Reverse triple-symmetry — i_faction_* must NOT satisfy LocateDds
        // OR LocatePortraitDds either.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_faction_EMPIRE.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateDds("EMPIRE").Should().BeNull(
            because: "i_faction_EMPIRE.dds is the faction-emblem convention, not the unit-icon one");
        resolver.LocatePortraitDds("EMPIRE").Should().BeNull(
            because: "i_faction_EMPIRE.dds is the faction-emblem convention, not the hero-portrait one");
    }

    [Fact]
    public void ResolveFactionEmblem_DdsExists_AndCachePopulated_ReturnsCachedPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_faction_REBEL.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 48));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolveFactionEmblem("REBEL", size: 48);
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cachedPng),
            because: "happy path — DDS + cache present at default-48-size lookup");
    }

    [Fact]
    public void ResolveFactionEmblem_DefaultSize_Is48_NotUnit32_NorPortrait64()
    {
        // Triple-default-arg pin: faction emblems sit between unit icons (32)
        // and hero portraits (64). Stage cache PNG ONLY at size 48 — if the
        // default drifts to 32 OR 64, the lookup misses.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_faction_X.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA });
        var cached48 = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 48));
        File.WriteAllBytes(cached48, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolveFactionEmblem("X");  // no size arg = default
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cached48),
            because: "ResolveFactionEmblem default size must be 48 — pin it so future refactor doesn't drift to 32 (unit icon default) or 64 (portrait default)");
    }

    [Fact]
    public void ResolveFactionEmblem_DdsExists_CacheMissing_ReturnsNull()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_faction_UNDERWORLD.dds"), new byte[] { 0xCA });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolveFactionEmblem("UNDERWORLD").Should().BeNull(
            because: "DDS exists but cache PNG missing — same graceful-null contract as iter-308 + iter-313");
    }

    [Fact]
    public void ResolveFactionEmblem_UnsupportedSize_ReturnsNull_DoesNotThrow()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_faction_Y.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var act = () => resolver.ResolveFactionEmblem("Y", size: 33);
        act.Should().NotThrow();
        resolver.ResolveFactionEmblem("Y", size: 33).Should().BeNull();
    }

    [Fact]
    public void LocateFactionEmblemDds_NotPresent_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateFactionEmblemDds("Faction_That_Does_Not_Exist").Should().BeNull();
    }

    [Fact]
    public void All_3_AssetClasses_CoExist_AtSameDir_WithoutCollision()
    {
        // End-to-end validation: ALL 3 asset classes can have files for the
        // same NAME at the same dir without colliding. Operator with one
        // mod that ships hero "Vader" + faction "EMPIRE" + unit "Empire_AT_AT"
        // sees each asset surface its own image, never accidentally swapped.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_NAME.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_NAME.dds"), new byte[] { 0x02 });
        File.WriteAllBytes(Path.Combine(dir, "i_faction_NAME.dds"), new byte[] { 0x03 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var unit = resolver.LocateDds("NAME");
        var portrait = resolver.LocatePortraitDds("NAME");
        var faction = resolver.LocateFactionEmblemDds("NAME");

        unit.Should().NotBeNull();
        portrait.Should().NotBeNull();
        faction.Should().NotBeNull();
        unit.Should().NotBe(portrait, because: "unit-icon and portrait paths must differ");
        unit.Should().NotBe(faction, because: "unit-icon and faction-emblem paths must differ");
        portrait.Should().NotBe(faction, because: "portrait and faction-emblem paths must differ");
    }
}
