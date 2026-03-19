-- ROE helper bridge for cloned hero respawn state orchestration.

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

local function Complete_Helper_Operation(result, operation_token)
    if Has_Value(operation_token) then
        local status = result and "APPLIED" or "FAILED"
        Try_Output_Debug("SWFOC_TRAINER_" .. status .. " " .. operation_token .. " globalKey=ROE_RESPAWN_ACTIVE")
    end

    return result
end

function SWFOC_Trainer_Toggle_Respawn(active, operation_token)
    local applied = pcall(function()
        if active then
            Set_Global_Variable("ROE_RESPAWN_ACTIVE", true)
        else
            Set_Global_Variable("ROE_RESPAWN_ACTIVE", false)
        end
    end)

    return Complete_Helper_Operation(applied, operation_token)
end
