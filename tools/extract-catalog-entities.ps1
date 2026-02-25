# Extract all entity names from AOTR and ROE XML files and generate catalog JSONs
# Filters out templates, death clones, dummies, spawners, facing variants

function Get-EntityNames {
    param(
        [string]$FilePath,
        [string]$ElementPattern
    )
    $names = @()
    if (Test-Path $FilePath) {
        $content = Get-Content $FilePath -Raw -Encoding UTF8
        $entityMatches = [regex]::Matches($content, $ElementPattern)
        foreach ($m in $entityMatches) {
            $names += $m.Groups[1].Value
        }
    }
    return $names
}

function Select-EntityValues {
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

Write-Output "========================================"
Write-Output "  AOTR ENTITY EXTRACTION"
Write-Output "========================================"

# --- AOTR SPACE UNITS ---
$aotrSpace = @()
$spacePattern = '<SpaceUnit\s+Name="([^"]+)"'
foreach ($f in @("SPACEUNITSCAPITAL.XML","SPACEUNITSFRIGATES.XML","SPACEUNITSFIGHTERS.XML","SpaceUnits_NonVictory.xml")) {
    $names = Get-EntityNames "$aotrXml\$f" $spacePattern
    Write-Output "  $f : $($names.Count) entries"
    $aotrSpace += $names
}

# --- AOTR GROUND VEHICLES ---
$aotrGV = @()
$gvPattern = '<GroundVehicle\s+Name="([^"]+)"'
foreach ($f in (Get-ChildItem "$aotrXml\GroundVehicles_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Get-EntityNames $f.FullName $gvPattern
    Write-Output "  $($f.Name) : $($names.Count) entries"
    $aotrGV += $names
}

# --- AOTR GROUND INFANTRY ---
$aotrGI = @()
$giPattern = '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
foreach ($f in (Get-ChildItem "$aotrXml\GroundInfantry_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Get-EntityNames $f.FullName $giPattern
    Write-Output "  $($f.Name) : $($names.Count) entries"
    $aotrGI += $names
}

# --- AOTR HEROES ---
$aotrHeroes = @()
$heroPattern = '<(?:HeroUnit|HeroCompany|UniqueUnit|GenericHeroUnit)\s+Name="([^"]+)"'
foreach ($f in (Get-ChildItem "$aotrXml\Heroes_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Get-EntityNames $f.FullName $heroPattern
    Write-Output "  $($f.Name) : $($names.Count) entries"
    $aotrHeroes += $names
}

# --- AOTR PLANETS ---
$aotrPlanets = Get-EntityNames "$aotrXml\PLANETS_AOTR.XML" '<Planet\s+Name="([^"]+)"'
Write-Output "  PLANETS_AOTR.XML : $($aotrPlanets.Count) entries"

# --- AOTR FACTIONS ---
$aotrFactions = Get-EntityNames "$aotrXml\FACTIONS.XML" '<Faction\s+Name="([^"]+)"'
Write-Output "  FACTIONS.XML : $($aotrFactions.Count) entries"

# Filter AOTR entities
$aotrSpaceFiltered = Select-EntityValues $aotrSpace
$aotrGVFiltered = Select-EntityValues $aotrGV
$aotrGIFiltered = Select-EntityValues $aotrGI
$aotrHeroesFiltered = Select-EntityValues $aotrHeroes
$aotrPlanetsFiltered = Select-EntityValues $aotrPlanets

# Combine all units
$aotrAllUnits = @()
$aotrAllUnits += $aotrSpaceFiltered
$aotrAllUnits += $aotrGVFiltered
$aotrAllUnits += $aotrGIFiltered
$aotrAllUnits = $aotrAllUnits | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

$aotrHeroList = $aotrHeroesFiltered | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique
$aotrPlanetList = $aotrPlanetsFiltered | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique
$aotrFactionList = $aotrFactions | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

Write-Output "`n--- AOTR FINAL COUNTS ---"
Write-Output "  unit_catalog:    $($aotrAllUnits.Count)"
Write-Output "  hero_catalog:    $($aotrHeroList.Count)"
Write-Output "  planet_catalog:  $($aotrPlanetList.Count)"
Write-Output "  faction_catalog: $($aotrFactionList.Count)"

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
Write-Output "`nAOTR catalog written to: $aotrOutput"

Write-Output "`n========================================"
Write-Output "  ROE SUBMOD ENTITY EXTRACTION"
Write-Output "========================================"

$roeBase = "$roeXml\ROE"

# --- ROE SPACE UNITS (Empire) ---
$roeEmpireSpace = @()
$spaceUnitPattern = '<(?:SpaceUnit|Spaceunit)\s+Name="([^"]+)"'
foreach ($f in @("$roeBase\Empire\roe_space_super.xml","$roeBase\Empire\roe_space_cruisers.XML","$roeBase\Empire\roe_space_capital.XML")) {
    $names = Get-EntityNames $f $spaceUnitPattern
    Write-Output "  $(Split-Path $f -Leaf) : $($names.Count) entries"
    $roeEmpireSpace += $names
}

# --- ROE CIS SPACE UNITS ---
$roeCISSpace = @()
foreach ($f in (Get-ChildItem "$roeBase\CIS\roe_cis_space_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Get-EntityNames $f.FullName '<(?:SpaceUnit|Freighter|StarBase|Container)\s+Name="([^"]+)"'
    Write-Output "  $($f.Name) : $($names.Count) entries"
    $roeCISSpace += $names
}

# --- ROE CIS LAND VEHICLES ---
$roeCISVehicles = @()
$names = Get-EntityNames "$roeBase\CIS\roe_cis_land_vehicles.xml" '<(?:GroundVehicle|GroundCompany)\s+Name="([^"]+)"'
Write-Output "  roe_cis_land_vehicles.xml : $($names.Count) entries"
$roeCISVehicles += $names

# --- ROE CIS LAND INFANTRY ---
$roeCISInfantry = @()
$names = Get-EntityNames "$roeBase\CIS\roe_cis_land_infantry.xml" '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
Write-Output "  roe_cis_land_infantry.xml : $($names.Count) entries"
$roeCISInfantry += $names

# --- ROE CIS STRUCTURES ---
$roeCISStructures = @()
foreach ($f in @("$roeBase\CIS\roe_cis_space_structures.xml","$roeBase\CIS\roe_cis_starbases.xml","$roeBase\CIS\roe_cis_land_groundstructures.xml","$roeBase\CIS\roe_cis_specialstructures.xml","$roeBase\CIS\roe_cis_land_structures.xml")) {
    if (Test-Path $f) {
        $names = Get-EntityNames $f '<(?:SpaceStructure|StarBase|GroundStructure|Container|SpecialStructure)\s+Name="([^"]+)"'
        Write-Output "  $(Split-Path $f -Leaf) : $($names.Count) entries"
        $roeCISStructures += $names
    }
}

# --- ROE EMPIRE CLONES ---
$roeClones = @()
$names = Get-EntityNames "$roeBase\Empire\roe_clones.xml" '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
Write-Output "  roe_clones.xml : $($names.Count) entries"
$roeClones += $names

$names = Get-EntityNames "$roeBase\Empire\roe_clones_companies.xml" '<(?:GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
Write-Output "  roe_clones_companies.xml : $($names.Count) entries"
$roeClones += $names

# --- ROE EMPIRE VEHICLES ---
$roeEmpVehicles = @()
$names = Get-EntityNames "$roeBase\Empire\roe_vehicles.xml" '<(?:GroundVehicle|GroundCompany)\s+Name="([^"]+)"'
Write-Output "  roe_vehicles.xml : $($names.Count) entries"
$roeEmpVehicles += $names

# --- ROE MISSION UNITS ---
$roeMission = @()
$names = Get-EntityNames "$roeBase\Empire\roe_mission_units.xml" '<(?:GroundInfantry|GroundCompany|UniqueUnit|HeroUnit|GroundVehicle|Container)\s+Name="([^"]+)"'
Write-Output "  roe_mission_units.xml : $($names.Count) entries"
$roeMission += $names

# --- ROE HEROES ---
$roeHeroes = @()
foreach ($f in (Get-ChildItem "$roeBase\Empire\roe_hero_*.xml" -ErrorAction SilentlyContinue)) {
    $names = Get-EntityNames $f.FullName '<(?:HeroUnit|HeroCompany|UniqueUnit|GenericHeroUnit|SpaceUnit|Spaceunit|GroundInfantry|GroundCompany)\s+Name="([^"]+)"'
    Write-Output "  $($f.Name) : $($names.Count) entries"
    $roeHeroes += $names
}

# --- ROE TRANSPORTS ---
$roeTransports = @()
foreach ($f in (Get-ChildItem "$roeBase\roe_cw_transports.xml" -ErrorAction SilentlyContinue)) {
    $names = Get-EntityNames $f.FullName '<(?:TransportUnit|UniqueUnit|SpaceUnit)\s+Name="([^"]+)"'
    Write-Output "  $($f.Name) : $($names.Count) entries"
    $roeTransports += $names
}

# --- ROE SQUADRONS ---
$roeSquadrons = @()
$names = Get-EntityNames "$roeBase\roe_cw_squadrons.xml" '<(?:Squadron|SpaceUnit)\s+Name="([^"]+)"'
Write-Output "  roe_cw_squadrons.xml : $($names.Count) entries"
$roeSquadrons += $names

# --- ROE PLANETS ---
$roePlanets = Get-EntityNames "$roeBase\roe_planets.xml" '<Planet\s+Name="([^"]+)"'
Write-Output "  roe_planets.xml : $($roePlanets.Count) entries"

# --- ROE FACTIONS ---
$roeFactions = Get-EntityNames "$roeBase\roe_factions.xml" '<Faction\s+Name="([^"]+)"'
Write-Output "  roe_factions.xml : $($roeFactions.Count) entries"

# Filter ROE entities
$roeAllSpaceFiltered = Select-EntityValues ($roeEmpireSpace + $roeCISSpace)
$roeCISVehFiltered = Select-EntityValues $roeCISVehicles
$roeCISInfFiltered = Select-EntityValues $roeCISInfantry
$roeClonesFiltered = Select-EntityValues $roeClones
$roeEmpVehFiltered = Select-EntityValues $roeEmpVehicles
$roeMissionFiltered = Select-EntityValues $roeMission
$roeTransFiltered = Select-EntityValues $roeTransports
$roeSquadFiltered = Select-EntityValues $roeSquadrons
$roeStructFiltered = Select-EntityValues $roeCISStructures

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
$roeHeroesFiltered = Select-EntityValues $roeHeroes
$roeHeroList = $roeHeroesFiltered | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

$roePlanetList = (Select-EntityValues $roePlanets) | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique
$roeFactionList = $roeFactions | ForEach-Object { $_.ToUpper() } | Sort-Object -Unique

Write-Output "`n--- ROE FINAL COUNTS ---"
Write-Output "  unit_catalog:    $($roeAllUnits.Count)"
Write-Output "  hero_catalog:    $($roeHeroList.Count)"
Write-Output "  planet_catalog:  $($roePlanetList.Count)"
Write-Output "  faction_catalog: $($roeFactionList.Count)"

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
Write-Output "`nROE catalog written to: $roeOutput"

Write-Output "`n========================================"
Write-Output "  DONE - Both catalogs generated!"
Write-Output "========================================"
