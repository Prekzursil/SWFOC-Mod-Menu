-- SWFOC Trainer helper bridge (common)
-- This script acts as a stable anchor for helper-dispatched spawn operations.

require("PGSpawnUnits")

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
