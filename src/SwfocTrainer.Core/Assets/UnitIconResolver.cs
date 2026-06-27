namespace SwfocTrainer.Core.Assets;

/// <summary>
/// 2026-05-07 (iter 308, Thread D arc FINALE; extended iter 313 with hero
/// portraits + iter 314 with faction emblems + iter 315 with planet icons +
/// iter 331 with weapon hardpoint icons + iter 332 with ability icons) —
/// maps a SWFOC unit-type name, hero-name, faction-name, planet-name,
/// weapon-hardpoint-name, OR ability-name to the cached thumbnail PNG path
/// emitted by iter-306 Python writer + looked up via iter-307
/// <see cref="ThumbnailCache"/>.
///
/// Filename conventions used (5-relpath walk for each):
///   - Unit icons:        <c>i_button_&lt;UnitTypeName&gt;.dds</c>       (iter 308, default size 32)
///   - Hero portraits:    <c>i_portrait_&lt;HeroName&gt;.dds</c>         (iter 313, default size 64)
///   - Faction emblems:   <c>i_faction_&lt;FactionName&gt;.dds</c>       (iter 314, default size 48)
///   - Planet icons:      <c>i_planet_&lt;PlanetName&gt;.dds</c>         (iter 315, default size 96)
///   - Weapon hardpoints: <c>i_button_hp_&lt;WeaponName&gt;.dds</c>      (iter 331, default size 32)
///   - Ability icons:     <c>i_button_ability_&lt;AbilityName&gt;.dds</c>(iter 332, default size 32)
/// All 6 walk the same 5 candidate relpaths under the operator-supplied root
/// via the shared <c>LocateByConvention</c> private helper.
///
/// Honest scope: iter-308 doesn't open .meg files at runtime — operator
/// pre-extracts via Python CLI. .meg-on-demand extraction is iter-309+
/// territory (would require taking a dependency on SwfocTrainer.Meg from
/// Core, which iter-307 explicitly chose not to do).
/// </summary>
public sealed class UnitIconResolver
{
    // Built via Path.Combine so the platform-native separator
    // (backslash on Windows) is used inside each candidate relpath. Hardcoding
    // forward-slash literals here would produce mixed-separator paths like
    // "C:\root\Data/Art/Textures/Units\i_button_X.dds", which File.Exists
    // tolerates but breaks string-equality pin tests against the expected path.
    private static readonly string[] CandidateRelPaths =
    {
        Path.Combine("Data", "Art", "Textures", "Units"),
        Path.Combine("Data", "Art", "Textures"),
        Path.Combine("Art", "Textures", "Units"),
        Path.Combine("Art", "Textures"),
        string.Empty,
    };

    private readonly string? _extractedDdsRoot;

    public UnitIconResolver(string? extractedDdsRoot)
    {
        _extractedDdsRoot = string.IsNullOrWhiteSpace(extractedDdsRoot)
            ? null
            : extractedDdsRoot;
    }

    /// <summary>
    /// Returns the cached thumbnail PNG path for <paramref name="unitTypeName"/>
    /// at <paramref name="size"/>, or null if the DDS isn't found OR the
    /// Python writer hasn't generated the thumbnail yet.
    ///
    /// Returns null silently — null binding in WPF hides the Image control,
    /// which is exactly the operator UX we want when icons aren't available
    /// (no broken image placeholder, no error noise).
    /// </summary>
    public string? Resolve(string unitTypeName, int size = 32)
    {
        if (_extractedDdsRoot is null || string.IsNullOrWhiteSpace(unitTypeName))
        {
            return null;
        }

        var ddsPath = LocateDds(unitTypeName);
        if (ddsPath is null)
        {
            return null;
        }

        try
        {
            return ThumbnailCache.GetCachedPathOrNull(ddsPath, size);
        }
        catch (ArgumentException)
        {
            // Unsupported size — return null instead of throwing so the
            // VM doesn't have to wrap every Resolve() in a try/catch.
            return null;
        }
    }

    /// <summary>
    /// Walks the candidate-relpath list looking for an
    /// <c>i_button_&lt;name&gt;.dds</c> file. Returns the first hit's full
    /// path, or null.
    /// </summary>
    public string? LocateDds(string unitTypeName)
        => LocateByConvention("i_button_", unitTypeName);

    /// <summary>
    /// 2026-05-07 (iter 313): hero-portrait counterpart to <see cref="Resolve"/>.
    /// Returns the cached thumbnail PNG path for <paramref name="heroName"/>
    /// at <paramref name="size"/>, looking up <c>i_portrait_&lt;HeroName&gt;.dds</c>
    /// instead of the unit-icon convention. Same 5-candidate-relpath walk +
    /// same iter-307 ThumbnailCache lookup. Default size 64 because hero
    /// portraits are typically rendered larger than unit icons in the editor.
    /// </summary>
    public string? ResolvePortrait(string heroName, int size = 64)
    {
        if (_extractedDdsRoot is null || string.IsNullOrWhiteSpace(heroName))
        {
            return null;
        }

        var ddsPath = LocatePortraitDds(heroName);
        if (ddsPath is null)
        {
            return null;
        }

        try
        {
            return ThumbnailCache.GetCachedPathOrNull(ddsPath, size);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 313): hero-portrait counterpart to <see cref="LocateDds"/>.
    /// Walks the candidate-relpath list looking for an
    /// <c>i_portrait_&lt;name&gt;.dds</c> file.
    /// </summary>
    public string? LocatePortraitDds(string heroName)
        => LocateByConvention("i_portrait_", heroName);

    /// <summary>
    /// 2026-05-07 (iter 314): faction-emblem counterpart to <see cref="Resolve"/>.
    /// Returns the cached thumbnail PNG path for <paramref name="factionName"/>
    /// at <paramref name="size"/>, looking up <c>i_faction_&lt;FactionName&gt;.dds</c>
    /// via the shared 5-candidate-relpath walk + iter-307 ThumbnailCache lookup.
    /// Default size 48 — between unit icons (32) and hero portraits (64) —
    /// because faction emblems typically render at medium scale (header
    /// badges, faction-picker dropdowns, slot labels).
    /// </summary>
    public string? ResolveFactionEmblem(string factionName, int size = 48)
    {
        if (_extractedDdsRoot is null || string.IsNullOrWhiteSpace(factionName))
        {
            return null;
        }

        var ddsPath = LocateFactionEmblemDds(factionName);
        if (ddsPath is null)
        {
            return null;
        }

        try
        {
            return ThumbnailCache.GetCachedPathOrNull(ddsPath, size);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 314): faction-emblem counterpart to <see cref="LocateDds"/>.
    /// Walks the candidate-relpath list looking for an
    /// <c>i_faction_&lt;name&gt;.dds</c> file.
    /// </summary>
    public string? LocateFactionEmblemDds(string factionName)
        => LocateByConvention("i_faction_", factionName);

    /// <summary>
    /// 2026-05-07 (iter 315): planet-icon counterpart to <see cref="Resolve"/>.
    /// Returns the cached thumbnail PNG path for <paramref name="planetName"/>
    /// at <paramref name="size"/>, looking up <c>i_planet_&lt;PlanetName&gt;.dds</c>
    /// via the shared 5-candidate-relpath walk + iter-307 ThumbnailCache lookup.
    /// Default size 96 — largest of the 4 asset classes — because the galactic-mode
    /// planet view renders planets at a larger scale than units, portraits, or
    /// faction emblems. Pinned distinct from the other 3 defaults (32/48/64) so
    /// any "consolidating" refactor that drifts the default catches in tests.
    /// </summary>
    public string? ResolvePlanetIcon(string planetName, int size = 96)
    {
        if (_extractedDdsRoot is null || string.IsNullOrWhiteSpace(planetName))
        {
            return null;
        }

        var ddsPath = LocatePlanetIconDds(planetName);
        if (ddsPath is null)
        {
            return null;
        }

        try
        {
            return ThumbnailCache.GetCachedPathOrNull(ddsPath, size);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 315): planet-icon counterpart to <see cref="LocateDds"/>.
    /// Walks the candidate-relpath list looking for an
    /// <c>i_planet_&lt;name&gt;.dds</c> file.
    /// </summary>
    public string? LocatePlanetIconDds(string planetName)
        => LocateByConvention("i_planet_", planetName);

    /// <summary>
    /// 2026-05-07 (iter 331): weapon-hardpoint counterpart to <see cref="Resolve"/>.
    /// Returns the cached thumbnail PNG path for <paramref name="weaponName"/>
    /// at <paramref name="size"/>, looking up <c>i_button_hp_&lt;WeaponName&gt;.dds</c>
    /// via the shared 5-candidate-relpath walk + iter-307 ThumbnailCache lookup.
    /// Default size 32 matches unit icons because hardpoint icons render at
    /// the same scale in the SWFOC unit-detail UI (per-unit weapon roster
    /// row). The <c>hp_</c> infix is SWFOC's canonical Hardpoint prefix —
    /// distinct from plain unit icons (<c>i_button_&lt;UnitTypeName&gt;.dds</c>)
    /// so a weapon icon never collides with a unit icon at lookup time.
    /// Closes the iter-294 Audit E weapon-icon class extension (5th plugin
    /// in the iter-313 LocateByConvention plugin set).
    /// </summary>
    public string? ResolveWeaponIcon(string weaponName, int size = 32)
    {
        if (_extractedDdsRoot is null || string.IsNullOrWhiteSpace(weaponName))
        {
            return null;
        }

        var ddsPath = LocateWeaponIconDds(weaponName);
        if (ddsPath is null)
        {
            return null;
        }

        try
        {
            return ThumbnailCache.GetCachedPathOrNull(ddsPath, size);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 331): weapon-hardpoint counterpart to <see cref="LocateDds"/>.
    /// Walks the candidate-relpath list looking for an
    /// <c>i_button_hp_&lt;name&gt;.dds</c> file.
    /// </summary>
    public string? LocateWeaponIconDds(string weaponName)
        => LocateByConvention("i_button_hp_", weaponName);

    /// <summary>
    /// 2026-05-07 (iter 332): ability-icon counterpart to <see cref="Resolve"/>.
    /// Returns the cached thumbnail PNG path for <paramref name="abilityName"/>
    /// at <paramref name="size"/>, looking up
    /// <c>i_button_ability_&lt;AbilityName&gt;.dds</c> via the shared
    /// 5-candidate-relpath walk + iter-307 ThumbnailCache lookup. Default
    /// size 32 matches unit icons + weapon hardpoints because ability icons
    /// render at the same scale in SWFOC's per-unit ability roster UI. The
    /// <c>ability_</c> infix is SWFOC's canonical ability prefix —
    /// distinct from plain unit icons (<c>i_button_&lt;UnitTypeName&gt;.dds</c>)
    /// AND weapon hardpoints (<c>i_button_hp_&lt;WeaponName&gt;.dds</c>) so an
    /// ability icon never collides with either at lookup time. Closes the
    /// iter-294 Audit E ability-icon class extension (6th plugin in the
    /// iter-313 LocateByConvention plugin set; pattern stable at N=6).
    /// </summary>
    public string? ResolveAbilityIcon(string abilityName, int size = 32)
    {
        if (_extractedDdsRoot is null || string.IsNullOrWhiteSpace(abilityName))
        {
            return null;
        }

        var ddsPath = LocateAbilityIconDds(abilityName);
        if (ddsPath is null)
        {
            return null;
        }

        try
        {
            return ThumbnailCache.GetCachedPathOrNull(ddsPath, size);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 332): ability-icon counterpart to <see cref="LocateDds"/>.
    /// Walks the candidate-relpath list looking for an
    /// <c>i_button_ability_&lt;name&gt;.dds</c> file.
    /// </summary>
    public string? LocateAbilityIconDds(string abilityName)
        => LocateByConvention("i_button_ability_", abilityName);

    /// <summary>
    /// 2026-05-07 (iter 313): private helper extracted so Locate{Dds,PortraitDds}
    /// share the 5-relpath walk verbatim. Caller supplies the filename prefix
    /// (e.g. "i_button_" / "i_portrait_") and the asset name; helper builds
    /// <c>&lt;prefix&gt;&lt;name&gt;.dds</c> and walks the candidate-relpath list.
    ///
    /// Eliminates the duplicated-walk drift risk flagged at iter-310 (where
    /// SettingsTabViewModel.CountIconsAtRoot mirrored LocateDds). Future
    /// asset types (faction emblems, planet icons, etc.) plug in by adding
    /// another LocateXyz wrapper around this helper.
    /// </summary>
    private string? LocateByConvention(string filenamePrefix, string assetName)
    {
        if (_extractedDdsRoot is null || !Directory.Exists(_extractedDdsRoot))
        {
            return null;
        }

        var fileName = $"{filenamePrefix}{assetName}.dds";
        foreach (var rel in CandidateRelPaths)
        {
            var full = string.IsNullOrEmpty(rel)
                ? Path.Combine(_extractedDdsRoot, fileName)
                : Path.Combine(_extractedDdsRoot, rel, fileName);
            if (File.Exists(full))
            {
                return full;
            }
        }
        return null;
    }
}
