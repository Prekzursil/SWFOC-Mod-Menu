-- AOTR helper bridge for hero state control.

function SWFOC_Trainer_Set_Hero_Respawn(global_key, value)
    if global_key == nil then
        return false
    end

    Set_Global_Variable(global_key, value)
    return true
end
