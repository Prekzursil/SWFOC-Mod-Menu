using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Assets;
using Xunit;

namespace SwfocTrainer.Tests.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 332, post-iter-331 mirror; 6th asset class):
/// pin tests for <see cref="UnitIconResolver.ResolveAbilityIcon"/> +
/// <see cref="UnitIconResolver.LocateAbilityIconDds"/> extension. 6th asset
/// class served by the shared LocateByConvention helper. Validates the
/// abstraction at 6 plugins — extends iter-313/314/315/331 5-class
/// validation to ability icons (i_button_ability_ prefix). Closes
/// iter-294 Audit E ability-icon class extension (deferred at iter-313
/// LocateByConvention abstraction kickoff alongside weapons).
///
/// Pinned to the same xUnit collection as iter-307+308+312+313+314+315+331
/// for env-var orthogonality on SWFOC_THUMB_CACHE.
/// </summary>
[Collection("ThumbnailCacheEnv")]
public sealed class Iter332AbilityIconResolverTests : IDisposable
{
    private readonly string _ddsRoot;
    private readonly string _cacheDir;
    private readonly string? _origCacheEnv;

    public Iter332AbilityIconResolverTests()
    {
        _ddsRoot = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter332_dds_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(),
            $"swfoc_iter332_cache_{Guid.NewGuid():N}");
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
    public void ResolveAbilityIcon_NullRoot_ReturnsNull()
    {
        var resolver = new UnitIconResolver(null);
        resolver.ResolveAbilityIcon("Force_Push").Should().BeNull(
            because: "no root configured = no icons; graceful operator UX");
    }

    [Fact]
    public void ResolveAbilityIcon_EmptyAbilityName_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolveAbilityIcon(string.Empty).Should().BeNull();
        resolver.ResolveAbilityIcon("   ").Should().BeNull();
    }

    [Fact]
    public void LocateAbilityIconDds_FindsAtCanonicalPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_button_ability_Force_Push.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xDD });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.LocateAbilityIconDds("Force_Push");
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(ddsPath),
            because: "iter-332 ability convention finds i_button_ability_<name>.dds at the same 5-relpath walk");
    }

    [Fact]
    public void LocateAbilityIconDds_DoesNotMatchOther5Conventions()
    {
        // 6-way prefix discriminator: i_button_*, i_portrait_*, i_faction_*,
        // i_planet_*, i_button_hp_* must NONE satisfy ResolveAbilityIcon.
        // Stage all 5 prior-convention files for the same NAME — only the
        // i_button_ability_ prefix should match.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_Force_Push.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_Force_Push.dds"), new byte[] { 0x02 });
        File.WriteAllBytes(Path.Combine(dir, "i_faction_Force_Push.dds"), new byte[] { 0x03 });
        File.WriteAllBytes(Path.Combine(dir, "i_planet_Force_Push.dds"), new byte[] { 0x04 });
        File.WriteAllBytes(Path.Combine(dir, "i_button_hp_Force_Push.dds"), new byte[] { 0x05 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateAbilityIconDds("Force_Push").Should().BeNull(
            because: "none of i_button_*, i_portrait_*, i_faction_*, i_planet_*, i_button_hp_* may satisfy ResolveAbilityIcon — strict 6-way discriminator");
    }

    [Fact]
    public void Other5Convention_Resolvers_DoNotMatchAbilityConvention()
    {
        // Reverse 6-way symmetry — i_button_ability_* must satisfy NONE of
        // the prior 5 resolvers. Asymmetric pin matrix grows N×(N-1) — at N=6
        // we need 30 directional assertions; this single test covers the 5
        // that point FROM ability TO others.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_ability_Force_Lightning.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateDds("Force_Lightning").Should().BeNull(
            because: "i_button_ability_* must NOT satisfy LocateDds (unit-icon convention requires i_button_<name>.dds, not i_button_ability_<name>.dds)");
        resolver.LocatePortraitDds("Force_Lightning").Should().BeNull(
            because: "i_button_ability_* must NOT satisfy LocatePortraitDds (portrait convention)");
        resolver.LocateFactionEmblemDds("Force_Lightning").Should().BeNull(
            because: "i_button_ability_* must NOT satisfy LocateFactionEmblemDds (faction-emblem convention)");
        resolver.LocatePlanetIconDds("Force_Lightning").Should().BeNull(
            because: "i_button_ability_* must NOT satisfy LocatePlanetIconDds (planet convention)");
        resolver.LocateWeaponIconDds("Force_Lightning").Should().BeNull(
            because: "i_button_ability_* must NOT satisfy LocateWeaponIconDds (weapon-hardpoint convention)");
    }

    [Fact]
    public void ResolveAbilityIcon_DdsExists_AndCachePopulated_ReturnsCachedPath()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_button_ability_Mind_Trick.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA, 0xFE });
        var cachedPng = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cachedPng, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolveAbilityIcon("Mind_Trick", size: 32);
        actual.Should().NotBeNull();
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cachedPng),
            because: "happy path — DDS + cache present at default-32-size lookup");
    }

    [Fact]
    public void ResolveAbilityIcon_DefaultSize_Is32_MatchesUnitIconScale()
    {
        // Default-arg pin — ability icons render at the same scale as unit
        // icons + weapon hardpoints in the SWFOC unit-detail UI (per-unit
        // ability roster row), so default size 32 matches Resolve() +
        // ResolveWeaponIcon(). 3rd asset class to share the size-32 default
        // (units, weapons, abilities); distinct from 48/64/96 of the other
        // 3 classes (factions, portraits, planets). Pin it so any future
        // "consolidating" refactor that drifts the default catches in tests.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        var ddsPath = Path.Combine(dir, "i_button_ability_X.dds");
        File.WriteAllBytes(ddsPath, new byte[] { 0xCA });
        var cached32 = Path.Combine(_cacheDir,
            ThumbnailCache.ComputeCacheFilename(ddsPath, 32));
        File.WriteAllBytes(cached32, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var actual = resolver.ResolveAbilityIcon("X");  // no size arg = default
        Path.GetFullPath(actual!).Should().Be(Path.GetFullPath(cached32),
            because: "ResolveAbilityIcon default size must be 32 — matches unit-icon + weapon-icon scale, distinct from 48/64/96 of factions/portraits/planets");
    }

    [Fact]
    public void ResolveAbilityIcon_DdsExists_CacheMissing_ReturnsNull()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_ability_Heal_Self.dds"), new byte[] { 0xCA });

        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.ResolveAbilityIcon("Heal_Self").Should().BeNull(
            because: "DDS exists but cache PNG missing — same graceful-null contract as the other 5 asset classes");
    }

    [Fact]
    public void ResolveAbilityIcon_UnsupportedSize_ReturnsNull_DoesNotThrow()
    {
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_ability_Y.dds"), new byte[] { 0x01 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var act = () => resolver.ResolveAbilityIcon("Y", size: 33);
        act.Should().NotThrow();
        resolver.ResolveAbilityIcon("Y", size: 33).Should().BeNull();
    }

    [Fact]
    public void LocateAbilityIconDds_NotPresent_ReturnsNull()
    {
        var resolver = new UnitIconResolver(_ddsRoot);
        resolver.LocateAbilityIconDds("Ability_That_Does_Not_Exist").Should().BeNull();
    }

    [Fact]
    public void All_6_AssetClasses_CoExist_AtSameDir_WithoutCollision()
    {
        // End-to-end 6-way validation: with all 6 conventions populated for
        // the same NAME at the same dir, each surfaces its own path without
        // swap. Operator with EAW vanilla "EMPIRE" sees:
        //   - unit icon (Empire AT-AT button)
        //   - hero portrait (rare — heroes rarely named after factions)
        //   - faction emblem (Empire crest)
        //   - planet icon (rare — planets aren't named after factions)
        //   - weapon icon (Empire-themed hardpoint button)
        //   - ability icon (Empire-themed ability button — e.g. orbital strike)
        // These MUST be 6 different images.
        var dir = Path.Combine(_ddsRoot, "Data", "Art", "Textures", "Units");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "i_button_NAME.dds"), new byte[] { 0x01 });
        File.WriteAllBytes(Path.Combine(dir, "i_portrait_NAME.dds"), new byte[] { 0x02 });
        File.WriteAllBytes(Path.Combine(dir, "i_faction_NAME.dds"), new byte[] { 0x03 });
        File.WriteAllBytes(Path.Combine(dir, "i_planet_NAME.dds"), new byte[] { 0x04 });
        File.WriteAllBytes(Path.Combine(dir, "i_button_hp_NAME.dds"), new byte[] { 0x05 });
        File.WriteAllBytes(Path.Combine(dir, "i_button_ability_NAME.dds"), new byte[] { 0x06 });

        var resolver = new UnitIconResolver(_ddsRoot);
        var unit = resolver.LocateDds("NAME");
        var portrait = resolver.LocatePortraitDds("NAME");
        var faction = resolver.LocateFactionEmblemDds("NAME");
        var planet = resolver.LocatePlanetIconDds("NAME");
        var weapon = resolver.LocateWeaponIconDds("NAME");
        var ability = resolver.LocateAbilityIconDds("NAME");

        // All 6 must resolve to non-null.
        unit.Should().NotBeNull();
        portrait.Should().NotBeNull();
        faction.Should().NotBeNull();
        planet.Should().NotBeNull();
        weapon.Should().NotBeNull();
        ability.Should().NotBeNull();

        // All 6 must resolve to DIFFERENT paths.
        var allPaths = new[] { unit, portrait, faction, planet, weapon, ability };
        var uniqueCount = allPaths.Distinct().Count();
        uniqueCount.Should().Be(6,
            because: "all 6 asset classes for the same NAME must resolve to 6 distinct paths — never collide");
    }
}
