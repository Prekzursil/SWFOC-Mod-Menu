using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 315, Thread D arc post-finale 5/?; 4th asset class):
/// pin tests for <see cref="UnitIconResolver.ResolvePlanetIcon"/> +
/// <see cref="UnitIconResolver.LocatePlanetIconDds"/> extension. 4th asset
/// class served by the shared LocateByConvention helper. Validates the
/// abstraction at 4 plugins — sufficient evidence that the pattern shape
/// is correct for arbitrary future asset types.
///
/// Pinned to the same xUnit collection as iter-307+308+312+313+314 for
/// env-var orthogonality on SWFOC_THUMB_CACHE.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter315PlanetIconResolverTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter315PlanetIconResolverTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter315_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter315_cache_{Guid.NewGuid():N}");
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
    public void ResolvePlanetIcon_NullRoot_ReturnsNull()
    {
        var resolver = new UnitIconResolver(null);
        resolver.ResolvePlanetIcon("CORUSCANT").Should().BeNull(
            because: "no root configured = no icons; graceful operator UX");
    }

    [Fact]
    public void ResolvePlanetIcon_EmptyPlanetName_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolvePlanetIcon(string.Empty).Should().BeNull();
        resolver.ResolvePlanetIcon("   ").Should().BeNull();
    }

    [Fact]
    public void LocatePlanetIconDds_FindsAtCanonicalPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_planet_HOTH.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xDD });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.LocatePlanetIconDds("HOTH");
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(ddsPath),
            because: "iter-315 planet convention finds i_planet_<name>.dds at the same 5-relpath walk");
    }

    [Fact]
    public void LocatePlanetIconDds_DoesNotMatchOther3Conventions()
    {
        // 4-way prefix discriminator: i_button_*, i_portrait_*, i_faction_*
        // must NONE satisfy ResolvePlanetIcon. Stage all 3 prior-convention
        // files for the same NAME — only the planet-prefix one should match.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_HOTH.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_HOTH.dds"), new byte[] { 0x02 });
        File.WriteAllBytes(Path.Combine(dir, "i_faction_HOTH.dds"), new byte[] { 0x03 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocatePlanetIconDds("HOTH").Should().BeNull(
            because: "none of i_button_*, i_portrait_*, i_faction_* may satisfy ResolvePlanetIcon — strict 4-way discriminator");
    }

    [Fact]
    public void Other3Convention_Resolvers_DoNotMatchPlanetConvention()
    {
        // Reverse 4-way symmetry — i_planet_* must satisfy NONE of the prior
        // 3 resolvers. Asymmetric pin matrix grows N×(N-1) — at N=4 we need
        // 12 directional assertions; this single test covers the 3 that point
        // FROM planet TO others.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_planet_TATOOINE.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateDds("TATOOINE").Should().BeNull(
            because: "i_planet_* must NOT satisfy LocateDds (unit-icon convention)");
        resolver.LocatePortraitDds("TATOOINE").Should().BeNull(
            because: "i_planet_* must NOT satisfy LocatePortraitDds (portrait convention)");
        resolver.LocateFactionEmblemDds("TATOOINE").Should().BeNull(
            because: "i_planet_* must NOT satisfy LocateFactionEmblemDds (faction-emblem convention)");
    }

    [Fact]
    public void ResolvePlanetIcon_DdsExists_AndCachePopulated_ReturnsCachedPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_planet_DAGOBAH.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 96));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolvePlanetIcon("DAGOBAH", size: 96);
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cachedPng),
            because: "happy path — DDS + cache present at default-96-size lookup");
    }

    [Fact]
    public void ResolvePlanetIcon_DefaultSize_Is96_NotUnit32_NorFaction48_NorPortrait64()
    {
        // 4-way default-arg pin — tightest possible default-drift catcher.
        // Sizes intentionally distinct: 32 (units), 48 (factions), 64 (portraits),
        // 96 (planets). Stage cache PNG ONLY at size 96 — drift to ANY of the
        // other 3 misses.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_planet_X.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA });
        var cached96 = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 96));
        File.WriteAllBytes(cached96, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolvePlanetIcon("X");  // no size arg = default
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cached96),
            because: "ResolvePlanetIcon default size must be 96 — distinct from 32/48/64 to catch any consolidation refactor");
    }

    [Fact]
    public void ResolvePlanetIcon_DdsExists_CacheMissing_ReturnsNull()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_planet_KASHYYYK.dds"), new byte[] { 0xCA });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolvePlanetIcon("KASHYYYK").Should().BeNull(
            because: "DDS exists but cache PNG missing — same graceful-null contract as the other 3 asset classes");
    }

    [Fact]
    public void ResolvePlanetIcon_UnsupportedSize_ReturnsNull_DoesNotThrow()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_planet_Y.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var act = () => resolver.ResolvePlanetIcon("Y", size: 33);
        act.Should().NotThrow();
        resolver.ResolvePlanetIcon("Y", size: 33).Should().BeNull();
    }

    [Fact]
    public void LocatePlanetIconDds_NotPresent_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocatePlanetIconDds("Planet_That_Does_Not_Exist").Should().BeNull();
    }

    [Fact]
    public void All_4_AssetClasses_CoExist_AtSameDir_WithoutCollision()
    {
        // End-to-end 4-way validation: with all 4 conventions populated for
        // the same NAME at the same dir, each surfaces its own path without
        // swap. Operator with EAW vanilla "EMPIRE" sees:
        //   - unit icon (Empire AT-AT button)
        //   - hero portrait (rare — heroes rarely named after factions)
        //   - faction emblem (Empire crest)
        //   - planet icon (rare — planets aren't named after factions)
        // These MUST be 4 different images.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_NAME.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_NAME.dds"), new byte[] { 0x02 });
        File.WriteAllBytes(Path.Combine(dir, "i_faction_NAME.dds"), new byte[] { 0x03 });
        File.WriteAllBytes(Path.Combine(dir, "i_planet_NAME.dds"), new byte[] { 0x04 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var unit = resolver.LocateDds("NAME");
        var portrait = resolver.LocatePortraitDds("NAME");
        var faction = resolver.LocateFactionEmblemDds("NAME");
        var planet = resolver.LocatePlanetIconDds("NAME");

        // All 4 must resolve to non-null.
        unit.Should().NotBeNull();
        portrait.Should().NotBeNull();
        faction.Should().NotBeNull();
        planet.Should().NotBeNull();

        // All 4 must resolve to DIFFERENT paths.
        var allPaths = new[] { unit, portrait, faction, planet };
        var uniqueCount = allPaths.Distinct().Count();
        uniqueCount.Should().Be(4,
            because: "all 4 asset classes for the same NAME must resolve to 4 distinct paths — never collide");
    }
}
