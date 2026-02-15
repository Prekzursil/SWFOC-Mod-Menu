# Extract all entity names from AOTR and ROE XML files and generate catalog JSONs
# Filters out templates, death clones, dummies, spawners, facing variants

function Extract-Names {
    param(
        [string]$FilePath,
        [string]$ElementPattern
    )
    $names = @()
    if (Test-Path $FilePath) {
        $content = Get-Content $FilePath -Raw -Encoding UTF8
        $matches = [regex]::Matches($content, $ElementPattern)
        foreach ($m in $matches) {
            $names += $m.Groups[1].Value
        }
    }
    return $names
}

function Filter-Entities {
    param([string[]]$entities)
    $filtered = $entities | Where-Object {
        $_ -notmatch '^T_' -and
        $_ -notmatch '^TC_' -and
        $_ -notmatch '_Death_Clone' -and
        $_ -notmatch '_DC$' -and
        $_ -notmatch '_FW_DC$' -and
        $_ -notmatch '_FL_DC$' -and
        $_ -notmatch '_Dummy$' -and
        $_ -notmatch '_Garrison_Dummy$' -and
        $_ -notmatch '_Spawner$' -and
        $_ -notmatch '_Team_Spawner$' -and
        $_ -notmatch '^DELETE_' -and
        $_ -notmatch '^UPGRADE_' -and
        $_ -notmatch '_Orbital_Dummy' -and
        $_ -notmatch '_Facing_'
    }
    return ($filtered | Sort-Object -Unique)
}

$workspace = "c:\Users\Prekzursil\Downloads\SWFOC editor"
$aotrXml = "$workspace\1397421866(original mod)\Data\XML"
$roeXml = "$workspace\3447786229(submod)\Data\XML"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AOTR ENTITY EXTRACTION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# --- AOTR SPACE UNITS ---
$aotrSpace = @()
$spacePattern = '<SpaceUnit\s+Name="([^"]+)"'
foreach ($f in @("SPACEUNITSCAPITAL.XML","SPACEUNITSFRIGATES.XML","SPACEUNITSFIGHTERS.XML","SpaceUnits_NonVictory.xml")) {
    $names = Extract-Names "$aotrXml\$f" $spacePattern
    Write-Host "  $f : $($names.Count) entries"
    $aotrSpace += $names
}

# --- AOTR GROUND VEHICLES ---
$aotrGV = @()
$gvPattern = '<GroundVehicle\s+Name="([^"]+)"'
foreach ($f in (Get-ChildItem "$aotrXml\GroundVehicles_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Extract-Names $f.FullName $gvPattern
    Write-Host "  $($f.Name) : $($names.Count) entries"
    $aotrGV += $names
}

# --- AOTR GROUND INFANTRY ---
$aotrGI = @()
$giPattern = '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
foreach ($f in (Get-ChildItem "$aotrXml\GroundInfantry_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Extract-Names $f.FullName $giPattern
    Write-Host "  $($f.Name) : $($names.Count) entries"
    $aotrGI += $names
}

# --- AOTR HEROES ---
$aotrHeroes = @()
$heroPattern = '<(?:HeroUnit|HeroCompany|UniqueUnit|GenericHeroUnit)\s+Name="([^"]+)"'
foreach ($f in (Get-ChildItem "$aotrXml\Heroes_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Extract-Names $f.FullName $heroPattern
    Write-Host "  $($f.Name) : $($names.Count) entries"
    $aotrHeroes += $names
}

# --- AOTR PLANETS ---
$aotrPlanets = Extract-Names "$aotrXml\PLANETS_AOTR.XML" '<Planet\s+Name="([^"]+)"'
Write-Host "  PLANETS_AOTR.XML : $($aotrPlanets.Count) entries"

# --- AOTR FACTIONS ---
$aotrFactions = Extract-Names "$aotrXml\FACTIONS.XML" '<Faction\s+Name="([^"]+)"'
Write-Host "  FACTIONS.XML : $($aotrFactions.Count) entries"

# Filter AOTR entities
$aotrSpaceFiltered = Filter-Entities $aotrSpace
$aotrGVFiltered = Filter-Entities $aotrGV
$aotrGIFiltered = Filter-Entities $aotrGI
$aotrHeroesFiltered = Filter-Entities $aotrHeroes
$aotrPlanetsFiltered = Filter-Entities $aotrPlanets

# Combine all units
$aotrAllUnits = @()
$aotrAllUnits += $aotrSpaceFiltered
$aotrAllUnits += $aotrGVFiltered
$aotrAllUnits += $aotrGIFiltered
$aotrAllUnits = $aotrAllUnits | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

$aotrHeroList = $aotrHeroesFiltered | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique
$aotrPlanetList = $aotrPlanetsFiltered | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique
$aotrFactionList = $aotrFactions | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

Write-Host "`n--- AOTR FINAL COUNTS ---" -ForegroundColor Green
Write-Host "  unit_catalog:    $($aotrAllUnits.Count)"
Write-Host "  hero_catalog:    $($aotrHeroList.Count)"
Write-Host "  planet_catalog:  $($aotrPlanetList.Count)"
Write-Host "  faction_catalog: $($aotrFactionList.Count)"

# Build AOTR JSON
$aotrCatalog = [ordered]@{
    unit_catalog = @($aotrAllUnits)
    hero_catalog = @($aotrHeroList)
    planet_catalog = @($aotrPlanetList)
    faction_catalog = @($aotrFactionList)
    action_constraints = @(
        "helper_required:set_hero_state_helper",
        "campaign_only:set_hero_respawn_timer",
        "tactical_only:toggle_tactical_god_mode"
    )
}
$aotrJson = $aotrCatalog | ConvertTo-Json -Depth 3
$aotrOutput = "$workspace\profiles\default\catalog\aotr_1397421866_swfoc\catalog.json"
$aotrJson | Set-Content -Path $aotrOutput -Encoding UTF8
Write-Host "`nAOTR catalog written to: $aotrOutput" -ForegroundColor Yellow

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  ROE SUBMOD ENTITY EXTRACTION" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$roeBase = "$roeXml\ROE"

# --- ROE SPACE UNITS (Empire) ---
$roeEmpireSpace = @()
$spaceUnitPattern = '<(?:SpaceUnit|Spaceunit)\s+Name="([^"]+)"'
foreach ($f in @("$roeBase\Empire\roe_space_super.xml","$roeBase\Empire\roe_space_cruisers.XML","$roeBase\Empire\roe_space_capital.XML")) {
    $names = Extract-Names $f $spaceUnitPattern
    Write-Host "  $(Split-Path $f -Leaf) : $($names.Count) entries"
    $roeEmpireSpace += $names
}

# --- ROE CIS SPACE UNITS ---
$roeCISSpace = @()
foreach ($f in (Get-ChildItem "$roeBase\CIS\roe_cis_space_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Extract-Names $f.FullName '<(?:SpaceUnit|Freighter|StarBase|Container)\s+Name="([^"]+)"'
    Write-Host "  $($f.Name) : $($names.Count) entries"
    $roeCISSpace += $names
}

# --- ROE CIS LAND VEHICLES ---
$roeCISVehicles = @()
$names = Extract-Names "$roeBase\CIS\roe_cis_land_vehicles.xml" '<(?:GroundVehicle|GroundCompany)\s+Name="([^"]+)"'
Write-Host "  roe_cis_land_vehicles.xml : $($names.Count) entries"
$roeCISVehicles += $names

# --- ROE CIS LAND INFANTRY ---
$roeCISInfantry = @()
$names = Extract-Names "$roeBase\CIS\roe_cis_land_infantry.xml" '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
Write-Host "  roe_cis_land_infantry.xml : $($names.Count) entries"
$roeCISInfantry += $names

# --- ROE CIS STRUCTURES ---
$roeCISStructures = @()
foreach ($f in @("$roeBase\CIS\roe_cis_space_structures.xml","$roeBase\CIS\roe_cis_starbases.xml","$roeBase\CIS\roe_cis_land_groundstructures.xml","$roeBase\CIS\roe_cis_specialstructures.xml","$roeBase\CIS\roe_cis_land_structures.xml")) {
    if (Test-Path $f) {
        $names = Extract-Names $f '<(?:SpaceStructure|StarBase|GroundStructure|Container|SpecialStructure)\s+Name="([^"]+)"'
        Write-Host "  $(Split-Path $f -Leaf) : $($names.Count) entries"
        $roeCISStructures += $names
    }
}

# --- ROE EMPIRE CLONES ---
$roeClones = @()
$names = Extract-Names "$roeBase\Empire\roe_clones.xml" '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
Write-Host "  roe_clones.xml : $($names.Count) entries"
$roeClones += $names

$names = Extract-Names "$roeBase\Empire\roe_clones_companies.xml" '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
Write-Host "  roe_clones_companies.xml : $($names.Count) entries"
$roeClones += $names

# --- ROE EMPIRE VEHICLES ---
$roeEmpVehicles = @()
$names = Extract-Names "$roeBase\Empire\roe_vehicles.xml" '<(?:GroundVehicle|GroundCompany)\s+Name="([^"]+)"'
Write-Host "  roe_vehicles.xml : $($names.Count) entries"
$roeEmpVehicles += $names

# --- ROE MISSION UNITS ---
$roeMission = @()
$names = Extract-Names "$roeBase\Empire\roe_mission_units.xml" '<(?:GroundInfantry|GroundCompany|UniqueUnit|HeroUnit|GroundVehicle|Container)\s+Name="([^"]+)"'
Write-Host "  roe_mission_units.xml : $($names.Count) entries"
$roeMission += $names

# --- ROE HEROES ---
$roeHeroes = @()
foreach ($f in (Get-ChildItem "$roeBase\Empire\roe_hero_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Extract-Names $f.FullName '<(?:HeroUnit|HeroCompany|UniqueUnit|GenericHeroUnit|SpaceUnit|Spaceunit|GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
    Write-Host "  $($f.Name) : $($names.Count) entries"
    $roeHeroes += $names
}

# --- ROE TRANSPORTS ---
$roeTransports = @()
foreach ($f in (Get-ChildItem "$roeBase\roe_cw_transports.xml" -ErrorAction SilentlyContinue)) {
    $names = Extract-Names $f.FullName '<(?:TransportUnit|UniqueUnit|SpaceUnit)\s+Name="([^"]+)"'
    Write-Host "  $($f.Name) : $($names.Count) entries"
    $roeTransports += $names
}

# --- ROE SQUADRONS ---
$roeSquadrons = @()
$names = Extract-Names "$roeBase\roe_cw_squadrons.xml" '<(?:Squadron|SpaceUnit)\s+Name="([^"]+)"'
Write-Host "  roe_cw_squadrons.xml : $($names.Count) entries"
$roeSquadrons += $names

# --- ROE PLANETS ---
$roePlanets = Extract-Names "$roeBase\roe_planets.xml" '<Planet\s+Name="([^"]+)"'
Write-Host "  roe_planets.xml : $($roePlanets.Count) entries"

# --- ROE FACTIONS ---
$roeFactions = Extract-Names "$roeBase\roe_factions.xml" '<Faction\s+Name="([^"]+)"'
Write-Host "  roe_factions.xml : $($roeFactions.Count) entries"

# Filter ROE entities
$roeAllSpaceFiltered = Filter-Entities ($roeEmpireSpace + $roeCISSpace)
$roeCISVehFiltered = Filter-Entities $roeCISVehicles
$roeCISInfFiltered = Filter-Entities $roeCISInfantry
$roeClonesFiltered = Filter-Entities $roeClones
$roeEmpVehFiltered = Filter-Entities $roeEmpVehicles
$roeMissionFiltered = Filter-Entities $roeMission
$roeTransFiltered = Filter-Entities $roeTransports
$roeSquadFiltered = Filter-Entities $roeSquadrons
$roeStructFiltered = Filter-Entities $roeCISStructures

# Combine all ROE units
$roeAllUnits = @()
$roeAllUnits += $roeAllSpaceFiltered
$roeAllUnits += $roeCISVehFiltered
$roeAllUnits += $roeCISInfFiltered
$roeAllUnits += $roeClonesFiltered
$roeAllUnits += $roeEmpVehFiltered
$roeAllUnits += $roeMissionFiltered
$roeAllUnits += $roeTransFiltered
$roeAllUnits += $roeSquadFiltered
$roeAllUnits += $roeStructFiltered
$roeAllUnits = $roeAllUnits | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

# Filter ROE heroes
$roeHeroesFiltered = Filter-Entities $roeHeroes
$roeHeroList = $roeHeroesFiltered | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

$roePlanetList = (Filter-Entities $roePlanets) | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique
$roeFactionList = $roeFactions | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

Write-Host "`n--- ROE FINAL COUNTS ---" -ForegroundColor Green
Write-Host "  unit_catalog:    $($roeAllUnits.Count)"
Write-Host "  hero_catalog:    $($roeHeroList.Count)"
Write-Host "  planet_catalog:  $($roePlanetList.Count)"
Write-Host "  faction_catalog: $($roeFactionList.Count)"

# Build ROE JSON
$roeCatalog = [ordered]@{
    unit_catalog = @($roeAllUnits)
    hero_catalog = @($roeHeroList)
    planet_catalog = @($roePlanetList)
    faction_catalog = @($roeFactionList)
    action_constraints = @(
        "requires_parent_mod:1397421866",
        "requires_submod:3447786229",
        "helper_required:toggle_roe_respawn_helper"
    )
}
$roeJson = $roeCatalog | ConvertTo-Json -Depth 3
$roeOutput = "$workspace\profiles\default\catalog\roe_3447786229_swfoc\catalog.json"
$roeJson | Set-Content -Path $roeOutput -Encoding UTF8
Write-Host "`nROE catalog written to: $roeOutput" -ForegroundColor Yellow

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  DONE - Both catalogs generated!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
