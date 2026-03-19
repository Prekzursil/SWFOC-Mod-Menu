-- AOTR helper bridge for hero state control.

local function Has_Value(value)
    return value ~= nil and value ~= ""
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

local function Complete_Helper_Operation(result, operation_token, applied_key)
    if Has_Value(operation_token) then
        local status = result and "APPLIED" or "FAILED"
        local key_segment = ""
        if Has_Value(applied_key) then
            key_segment = " globalKey=" .. applied_key
        end

        Try_Output_Debug("SWFOC_TRAINER_" .. status .. " " .. operation_token .. key_segment)
    end

    return result
end

function SWFOC_Trainer_Set_Hero_Respawn(global_key, value, operation_token)
    if not Has_Value(global_key) then
        return Complete_Helper_Operation(false, operation_token, global_key)
    end

    local applied = pcall(function()
        Set_Global_Variable(global_key, value)
    end)
    return Complete_Helper_Operation(applied, operation_token, global_key)
end
