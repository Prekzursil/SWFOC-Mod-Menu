-- SWFOC Trainer helper bridge (common)
-- This script acts as a stable anchor for helper-dispatched spawn operations.

require("PGSpawnUnits")

local function Resolve_Object_Type(entity_id, unit_id)
    local candidate = entity_id
    if candidate == nil or candidate == "" then
        candidate = unit_id
    end

    if candidate == nil or candidate == "" then
        return nil
    end

    return Find_Object_Type(candidate)
end

local function Resolve_Player(target_faction)
    local player = Find_Player(target_faction)
    if player then
        return player
    end

    return Find_Player("Neutral")
end

local function Spawn_Object(entity_id, unit_id, entry_marker, player_name)
    local player = Resolve_Player(player_name)
    if not player then
        return false
    end

    local type_ref = Resolve_Object_Type(entity_id, unit_id)
    if not type_ref then
        return false
    end

    if entry_marker == nil or entry_marker == "" then
        entry_marker = "Land_Reinforcement_Point"
    end

    Spawn_Unit(type_ref, entry_marker, player)
    return true
end

function SWFOC_Trainer_Spawn(object_type, entry_marker, player_name)
    local player = Find_Player(player_name)
    if not player then
        return false
    end

    local type_ref = Find_Object_Type(object_type)
    if not type_ref then
        return false
    end

    Spawn_Unit(type_ref, entry_marker, player)
    return true
end

function SWFOC_Trainer_Spawn_Context(entity_id, unit_id, entry_marker, faction, runtime_mode, persistence_policy, population_policy, world_position)
    -- Runtime policy flags are tracked in diagnostics; game-side spawn still uses core Spawn_Unit API.
    return Spawn_Object(entity_id, unit_id, entry_marker, faction)
end

function SWFOC_Trainer_Place_Building(entity_id, entry_marker, target_faction, force_override)
    return Spawn_Object(entity_id, nil, entry_marker, target_faction)
end
