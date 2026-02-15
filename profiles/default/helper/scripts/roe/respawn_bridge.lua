-- ROE helper bridge for cloned hero respawn state orchestration.

function SWFOC_Trainer_Toggle_Respawn(active)
    if active then
        Set_Global_Variable("ROE_RESPAWN_ACTIVE", true)
    else
        Set_Global_Variable("ROE_RESPAWN_ACTIVE", false)
    end
    return true
end
