-- SWFOC telemetry emitter used by runbook/live-validation workflows.
function SwfocTrainer_Emit_Telemetry_Mode()
    local mode_value = "Unknown"
    if Get_Game_Mode ~= nil then
        local ok, mode_result = pcall(Get_Game_Mode)
        if ok and mode_result ~= nil then
            mode_value = tostring(mode_result)
        end
    end

    local timestamp_value = "unknown"
    if os ~= nil and os.date ~= nil then
        timestamp_value = tostring(os.date("!%Y-%m-%dT%H:%M:%SZ"))
    end

    if OutputDebug ~= nil then
        OutputDebug("SWFOC_TRAINER_TELEMETRY timestamp=" .. timestamp_value .. " mode=" .. mode_value)
    end

    return {
        marker = "SWFOC_TRAINER_TELEMETRY",
        timestamp = timestamp_value,
        mode = mode_value
    }
end
