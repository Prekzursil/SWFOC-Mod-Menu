# SwfocTrainerTelemetry Mod Template

Drop this mod into `MODPATH` during live validation runs to emit runtime mode markers into the game log.

The Lua script emits lines in this format:

`SWFOC_TRAINER_TELEMETRY timestamp=<utc-iso-or-unknown> mode=<mode>`

`TelemetryLogTailService` consumes these lines and promotes fresh telemetry mode as the primary runtime-mode signal.
