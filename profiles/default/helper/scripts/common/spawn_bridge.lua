-- SWFOC Trainer helper bridge (common)
-- This script acts as a stable anchor for helper-dispatched spawn/build/allegiance operations.

require("PGSpawnUnits")

local function Has_Value(value)
    return value ~= nil and value ~= ""
end

local function Resolve_Object_Type(entity_id, unit_id)
    local candidate = entity_id
    if not Has_Value(candidate) then
        candidate = unit_id
    end

    if not Has_Value(candidate) then
        return nil
    end

    return Find_Object_Type(candidate)
end

local function Resolve_Player(target_faction)
    if Has_Value(target_faction) then
        local explicit = Find_Player(target_faction)
        if explicit then
            return explicit
        end
    end

    return Find_Player("Neutral")
end

local function Resolve_Entry_Marker(entry_marker)
    if Has_Value(entry_marker) then
        return entry_marker
    end

    return "Land_Reinforcement_Point"
end

local function Try_Find_Object(entity_id)
    if not Has_Value(entity_id) then
        return nil
    end

    local ok, object = pcall(function()
        return Find_First_Object(entity_id)
    end)

    if ok then
        return object
    end

    return nil
end

local function Try_Change_Owner(object, player)
    if object == nil or player == nil then
        return false
    end

    if not object.Change_Owner then
        return false
    end

    local changed = pcall(function()
        object.Change_Owner(player)
    end)

    return changed
end

local function Try_Reinforce_Unit(type_ref, entry_marker, player)
    if not Reinforce_Unit then
        return false
    end

    local marker = Resolve_Entry_Marker(entry_marker)
    local ok = pcall(function()
        Reinforce_Unit(type_ref, marker, player, true)
    end)
    return ok
end

local function Spawn_Object(entity_id, unit_id, entry_marker, player_name, placement_mode)
    local player = Resolve_Player(player_name)
    if not player then
        return false
    end

    local type_ref = Resolve_Object_Type(entity_id, unit_id)
    if not type_ref then
        return false
    end

    local marker = Resolve_Entry_Marker(entry_marker)
    if placement_mode == "reinforcement_zone" and Try_Reinforce_Unit(type_ref, marker, player) then
        return true
    end

    local ok = pcall(function()
        Spawn_Unit(type_ref, marker, player)
    end)
    return ok
end

local function Try_Output_Debug(message)
    if not Has_Value(message) then
        return
    end

    if _OuputDebug then
        pcall(function()
            _OuputDebug(message)
        end)
        return
    end

    if _OutputDebug then
        pcall(function()
            _OutputDebug(message)
        end)
    end
end

local function Resolve_Operation_Token_From_Variadic(args)
    if args == nil then
        return nil
    end

    for _, value in ipairs(args) do
        if type(value) == "string" then
            if string.match(value, "^[0-9a-fA-F]+$") and string.len(value) >= 16 then
                return value
            end

            if string.sub(value, 1, 6) == "token:" then
                return string.sub(value, 7)
            end
        elseif type(value) == "table" then
            local candidate = value["operationToken"]
            if not Has_Value(candidate) then
                candidate = value["operation_token"]
            end

            if Has_Value(candidate) then
                return candidate
            end
        end
    end

    return nil
end

local function Complete_Helper_Operation(result, operation_token, applied_entity_id)
    if Has_Value(operation_token) then
        local status = result and "APPLIED" or "FAILED"
        local entity_segment = ""
        if Has_Value(applied_entity_id) then
            entity_segment = " entity=" .. applied_entity_id
        end

        Try_Output_Debug("SWFOC_TRAINER_" .. status .. " " .. operation_token .. entity_segment)
    end

    return result
end

local function Try_Story_Event(event_name, a, b, c)
    if not Story_Event then
        return false
    end

    local ok = pcall(function()
        Story_Event(event_name, a, b, c)
    end)

    return ok
end

function SWFOC_Trainer_Spawn(object_type, entry_marker, player_name, operation_token)
    local player = Resolve_Player(player_name)
    if not player then
        return false
    end

    local type_ref = Find_Object_Type(object_type)
    if not type_ref then
        return false
    end

    local ok = pcall(function()
        Spawn_Unit(type_ref, Resolve_Entry_Marker(entry_marker), player)
    end)

    return Complete_Helper_Operation(ok, operation_token, object_type)
end

function SWFOC_Trainer_Spawn_Context(entity_id, unit_id, entry_marker, faction, ...)
    -- Runtime policy flags are tracked in diagnostics; tactical defaults use reinforcement-zone behavior when available.
    local args = {...}
    local runtime_mode = args[1]
    local operation_token = Resolve_Operation_Token_From_Variadic(args)
    local placement_mode = args[5]
    local effective_placement_mode = placement_mode
    if not Has_Value(effective_placement_mode) and runtime_mode ~= nil and runtime_mode ~= "Galactic" then
        effective_placement_mode = "reinforcement_zone"
    end

    local spawned = Spawn_Object(entity_id, unit_id, entry_marker, faction, effective_placement_mode)
    local applied_entity_id = entity_id
    if not Has_Value(applied_entity_id) then
        applied_entity_id = unit_id
    end

    return Complete_Helper_Operation(spawned, operation_token, applied_entity_id)
end

function SWFOC_Trainer_Place_Building(entity_id, entry_marker, target_faction, force_override, operation_token)
    local placed = Spawn_Object(entity_id, nil, entry_marker, target_faction, "safe_rules")
    return Complete_Helper_Operation(placed, operation_token, entity_id)
end

function SWFOC_Trainer_Set_Context_Allegiance(entity_id, target_faction, source_faction, runtime_mode, allow_cross_faction, operation_token)
    if not Has_Value(target_faction) then
        return false
    end

    local target_player = Resolve_Player(target_faction)
    if not target_player then
        return false
    end

    if not Has_Value(entity_id) then
        -- No explicit object supplied; helper request is still considered valid for context-based handlers.
        return Complete_Helper_Operation(true, operation_token, target_faction)
    end

    local object = Try_Find_Object(entity_id)
    local changed = Try_Change_Owner(object, target_player)
    return Complete_Helper_Operation(changed, operation_token, entity_id)
end

local function Is_Force_Override(value)
    return value == true or value == "true"
end

local function Validate_Fleet_Transfer_Request(fleet_entity_id, source_faction, target_faction, safe_planet_id, force_override)
    if not Has_Value(fleet_entity_id) or not Has_Value(source_faction) or not Has_Value(target_faction) then
        return false
    end

    if source_faction == target_faction then
        return false
    end

    if Has_Value(safe_planet_id) then
        return true
    end

    return Is_Force_Override(force_override)
end

function SWFOC_Trainer_Transfer_Fleet_Safe(fleet_entity_id, source_faction, target_faction, safe_planet_id, force_override, operation_token)
    if not Validate_Fleet_Transfer_Request(fleet_entity_id, source_faction, target_faction, safe_planet_id, force_override) then
        return false
    end

    local target_player = Resolve_Player(target_faction)
    local fleet = Try_Find_Object(fleet_entity_id)

    if Has_Value(safe_planet_id) then
        -- Prefer relocation-first to minimize auto-battle triggers.
        Try_Story_Event("MOVE_FLEET", fleet_entity_id, safe_planet_id, target_faction)
    end

    if Try_Change_Owner(fleet, target_player) then
        return Complete_Helper_Operation(true, operation_token, fleet_entity_id)
    end

    -- Story-event fallback for mods that expose transactional fleet transfer hooks.
    local moved = Try_Story_Event("MOVE_FLEET", fleet_entity_id, safe_planet_id, target_faction)
    return Complete_Helper_Operation(moved, operation_token, fleet_entity_id)
end

local function Normalize_Flip_Mode(mode)
    if not Has_Value(mode) then
        return "convert_everything"
    end

    if mode == "empty_and_retreat" or mode == "convert_everything" then
        return mode
    end

    return nil
end

local function Emit_Planet_Flip_Followups(planet_entity_id, target_faction, mode)
    if mode == "empty_and_retreat" then
        -- Best-effort semantic marker for mods that expose retreat cleanup rewards.
        Try_Story_Event("PLANET_RETREAT_ALL", planet_entity_id, target_faction, "empty")
        return
    end

    Try_Story_Event("PLANET_CONVERT_ALL", planet_entity_id, target_faction, "convert")
end

function SWFOC_Trainer_Flip_Planet_Owner(planet_entity_id, target_faction, flip_mode, force_override, operation_token)
    if not Has_Value(planet_entity_id) or not Has_Value(target_faction) then
        return false
    end

    local mode = Normalize_Flip_Mode(flip_mode)
    if not mode then
        return false
    end

    local planet = Try_Find_Object(planet_entity_id)
    local target_player = Resolve_Player(target_faction)
    local changed = Try_Change_Owner(planet, target_player)

    if not changed then
        changed = Try_Story_Event("PLANET_FACTION", planet_entity_id, target_faction, mode)
    end

    if not changed then
        return false
    end

    Emit_Planet_Flip_Followups(planet_entity_id, target_faction, mode)
    return Complete_Helper_Operation(true, operation_token, planet_entity_id)
end

function SWFOC_Trainer_Switch_Player_Faction(target_faction, operation_token)
    if not Has_Value(target_faction) then
        return false
    end

    local switched = Try_Story_Event("SWITCH_SIDES", target_faction, nil, nil)
    return Complete_Helper_Operation(switched, operation_token, target_faction)
end

local function Is_Hero_Death_State(state)
    return state == "dead" or state == "permadead" or state == "remove"
end

local function Try_Remove_Hero(hero)
    if hero and hero.Despawn then
        return pcall(function()
            hero.Despawn()
        end)
    end

    return false
end

local function Try_Apply_Hero_Story_State(hero_entity_id, state, hero_global_key)
    return Try_Story_Event("SET_HERO_STATE", hero_entity_id, state, hero_global_key)
end

local function Try_Set_Hero_Respawn_Pending(hero_entity_id, hero_global_key)
    return Try_Story_Event("SET_HERO_RESPAWN", hero_entity_id, hero_global_key, "pending")
end

local function Is_Valid_Hero_State(state)
    return state == "alive" or state == "dead" or state == "respawn_pending" or state == "permadead" or state == "remove"
end

local function Try_Handle_Hero_Alive_State(hero, hero_entity_id, hero_global_key, allow_duplicate)
    if hero ~= nil then
        return true
    end

    if Is_Force_Override(allow_duplicate) then
        return Spawn_Object(hero_entity_id, hero_entity_id, nil, nil, "reinforcement_zone")
    end

    return Try_Apply_Hero_Story_State(hero_entity_id, "alive", hero_global_key)
end

function SWFOC_Trainer_Edit_Hero_State(hero_entity_id, hero_global_key, desired_state, allow_duplicate, operation_token)
    if not Has_Value(hero_entity_id) and not Has_Value(hero_global_key) then
        return false
    end

    local hero = Try_Find_Object(hero_entity_id)
    local state = desired_state or "alive"
    if not Is_Valid_Hero_State(state) then
        return false
    end

    if Is_Hero_Death_State(state) then
        local removed = Try_Remove_Hero(hero) or Try_Apply_Hero_Story_State(hero_entity_id, state, hero_global_key)
        return Complete_Helper_Operation(removed, operation_token, hero_entity_id)
    end

    if state == "respawn_pending" then
        local pending = Try_Set_Hero_Respawn_Pending(hero_entity_id, hero_global_key)
        return Complete_Helper_Operation(pending, operation_token, hero_entity_id)
    end

    local alive = Try_Handle_Hero_Alive_State(hero, hero_entity_id, hero_global_key, allow_duplicate)
    return Complete_Helper_Operation(alive, operation_token, hero_entity_id)
end

function SWFOC_Trainer_Create_Hero_Variant(source_hero_id, variant_hero_id, target_faction, operation_token)
    if not Has_Value(source_hero_id) or not Has_Value(variant_hero_id) then
        return false
    end

    local faction = target_faction
    if not Has_Value(faction) then
        faction = "Neutral"
    end

    if Spawn_Object(variant_hero_id, variant_hero_id, nil, faction, "reinforcement_zone") then
        return Complete_Helper_Operation(true, operation_token, variant_hero_id)
    end

    local created = Try_Story_Event("CREATE_HERO_VARIANT", source_hero_id, variant_hero_id, faction)
    return Complete_Helper_Operation(created, operation_token, variant_hero_id)
end


