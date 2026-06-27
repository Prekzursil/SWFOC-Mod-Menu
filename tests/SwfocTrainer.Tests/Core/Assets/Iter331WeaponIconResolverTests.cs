using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 331, post-iter-323-arc feature-velocity restore; 5th asset class):
/// pin tests for <see cref="UnitIconResolver.ResolveWeaponIcon"/> +
/// <see cref="UnitIconResolver.LocateWeaponIconDds"/> extension. 5th asset
/// class served by the shared LocateByConvention helper. Validates the
/// abstraction at 5 plugins — extends iter-313/314/315 4-class validation
/// to weapon hardpoints (i_button_hp_ prefix). Closes iter-294 Audit E
/// weapon-icon class extension.
///
/// Pinned to the same xUnit collection as iter-307+308+312+313+314+315 for
/// env-var orthogonality on SWFOC_THUMB_CACHE.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter331WeaponIconResolverTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter331WeaponIconResolverTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter331_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter331_cache_{Guid.NewGuid():N}");
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
    public void ResolveWeaponIcon_NullRoot_ReturnsNull()
    {
        var resolver = new UnitIconResolver(null);
        resolver.ResolveWeaponIcon("TIE_Laser").Should().BeNull(
            because: "no root configured = no icons; graceful operator UX");
    }

    [Fact]
    public void ResolveWeaponIcon_EmptyWeaponName_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolveWeaponIcon(string.Empty).Should().BeNull();
        resolver.ResolveWeaponIcon("   ").Should().BeNull();
    }

    [Fact]
    public void LocateWeaponIconDds_FindsAtCanonicalPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_button_hp_TIE_Laser.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xDD });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.LocateWeaponIconDds("TIE_Laser");
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(ddsPath),
            because: "iter-331 weapon convention finds i_button_hp_<name>.dds at the same 5-relpath walk");
    }

    [Fact]
    public void LocateWeaponIconDds_DoesNotMatchOther4Conventions()
    {
        // 5-way prefix discriminator: i_button_*, i_portrait_*, i_faction_*,
        // i_planet_* must NONE satisfy ResolveWeaponIcon. Stage all 4 prior-
        // convention files for the same NAME — only the i_button_hp_ prefix
        // should match.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_TIE_Laser.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_TIE_Laser.dds"), new byte[] { 0x02 });
        File.WriteAllBytes(Path.Combine(dir, "i_faction_TIE_Laser.dds"), new byte[] { 0x03 });
        File.WriteAllBytes(Path.Combine(dir, "i_planet_TIE_Laser.dds"), new byte[] { 0x04 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateWeaponIconDds("TIE_Laser").Should().BeNull(
            because: "none of i_button_*, i_portrait_*, i_faction_*, i_planet_* may satisfy ResolveWeaponIcon — strict 5-way discriminator");
    }

    [Fact]
    public void Other4Convention_Resolvers_DoNotMatchWeaponConvention()
    {
        // Reverse 5-way symmetry — i_button_hp_* must satisfy NONE of the prior
        // 4 resolvers. Asymmetric pin matrix grows N×(N-1) — at N=5 we need
        // 20 directional assertions; this single test covers the 4 that point
        // FROM weapon TO others.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_hp_AT_AT_Cannon.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateDds("AT_AT_Cannon").Should().BeNull(
            because: "i_button_hp_* must NOT satisfy LocateDds (unit-icon convention requires i_button_<name>.dds, not i_button_hp_<name>.dds)");
        resolver.LocatePortraitDds("AT_AT_Cannon").Should().BeNull(
            because: "i_button_hp_* must NOT satisfy LocatePortraitDds (portrait convention)");
        resolver.LocateFactionEmblemDds("AT_AT_Cannon").Should().BeNull(
            because: "i_button_hp_* must NOT satisfy LocateFactionEmblemDds (faction-emblem convention)");
        resolver.LocatePlanetIconDds("AT_AT_Cannon").Should().BeNull(
            because: "i_button_hp_* must NOT satisfy LocatePlanetIconDds (planet convention)");
    }

    [Fact]
    public void ResolveWeaponIcon_DdsExists_AndCachePopulated_ReturnsCachedPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_button_hp_Star_Destroyer_Turbolaser.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolveWeaponIcon("Star_Destroyer_Turbolaser", size: 32);
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cachedPng),
            because: "happy path — DDS + cache present at default-32-size lookup");
    }

    [Fact]
    public void ResolveWeaponIcon_DefaultSize_Is32_MatchesUnitIconScale()
    {
        // Default-arg pin — weapon icons render at the same scale as unit
        // icons in the SWFOC unit-detail UI (per-unit weapon roster row),
        // so default size 32 matches Resolve(). Unlike the other 3 asset
        // classes (48/64/96), weapon icons SHARE the unit-icon default — pin
        // it so any future "consolidating" refactor that drifts the default
        // catches in tests.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_button_hp_X.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA });
        var cached32 = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cached32, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolveWeaponIcon("X");  // no size arg = default
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cached32),
            because: "ResolveWeaponIcon default size must be 32 — matches unit-icon scale, distinct from 48/64/96 of the other 3 asset classes");
    }

    [Fact]
    public void ResolveWeaponIcon_DdsExists_CacheMissing_ReturnsNull()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_hp_KASHYYYK_Cannon.dds"), new byte[] { 0xCA });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolveWeaponIcon("KASHYYYK_Cannon").Should().BeNull(
            because: "DDS exists but cache PNG missing — same graceful-null contract as the other 4 asset classes");
    }

    [Fact]
    public void ResolveWeaponIcon_UnsupportedSize_ReturnsNull_DoesNotThrow()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_hp_Y.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var act = () => resolver.ResolveWeaponIcon("Y", size: 33);
        act.Should().NotThrow();
        resolver.ResolveWeaponIcon("Y", size: 33).Should().BeNull();
    }

    [Fact]
    public void LocateWeaponIconDds_NotPresent_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateWeaponIconDds("Weapon_That_Does_Not_Exist").Should().BeNull();
    }

    [Fact]
    public void All_5_AssetClasses_CoExist_AtSameDir_WithoutCollision()
    {
        // End-to-end 5-way validation: with all 5 conventions populated for
        // the same NAME at the same dir, each surfaces its own path without
        // swap. Operator with EAW vanilla "EMPIRE" sees:
        //   - unit icon (Empire AT-AT button)
        //   - hero portrait (rare — heroes rarely named after factions)
        //   - faction emblem (Empire crest)
        //   - planet icon (rare — planets aren't named after factions)
        //   - weapon icon (Empire-themed hardpoint button)
        // These MUST be 5 different images.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_NAME.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_NAME.dds"), new byte[] { 0x02 });
        File.WriteAllBytes(Path.Combine(dir, "i_faction_NAME.dds"), new byte[] { 0x03 });
        File.WriteAllBytes(Path.Combine(dir, "i_planet_NAME.dds"), new byte[] { 0x04 });
        File.WriteAllBytes(Path.Combine(dir, "i_button_hp_NAME.dds"), new byte[] { 0x05 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var unit = resolver.LocateDds("NAME");
        var portrait = resolver.LocatePortraitDds("NAME");
        var faction = resolver.LocateFactionEmblemDds("NAME");
        var planet = resolver.LocatePlanetIconDds("NAME");
        var weapon = resolver.LocateWeaponIconDds("NAME");

        // All 5 must resolve to non-null.
        unit.Should().NotBeNull();
        portrait.Should().NotBeNull();
        faction.Should().NotBeNull();
        planet.Should().NotBeNull();
        weapon.Should().NotBeNull();

        // All 5 must resolve to DIFFERENT paths.
        var allPaths = new[] { unit, portrait, faction, planet, weapon };
        var uniqueCount = allPaths.Distinct().Count();
        uniqueCount.Should().Be(5,
            because: "all 5 asset classes for the same NAME must resolve to 5 distinct paths — never collide");
    }
}
