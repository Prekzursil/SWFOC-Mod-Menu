# PlanetaryDataPackClass

**RTTI**: `.?AVPlanetaryDataPackClass@@`

**Size**: 0x350 bytes


## Fields

| Offset | Type | Name | Status / Confidence | Notes |
|--------|------|------|---------------------|-------|
| `0x10` | `DVec<PersistentTacticalBuiltObjectStruct>` | persistent_tactical_objects |  |  |
| `0x20` | `DVec<PersistentUpgradeObjectStruct>` | persistent_upgrades |  |  |
| `0x30` | `DVec<LineLinkStruct>` | line_links |  |  |
| `0x50` | `DVec<TradeRouteLinkEntryClass>` | trade_route_links |  |  |
| `0x68` | `float32` | capture_progress | high |  |
| `0x6C` | `int32` | owning_player_id | high |  |
| `0x70` | `float32` | previous_capture_progress | high |  |
| `0x74` | `int32` | previous_owning_player_id | high |  |
| `0x98` | `int32` | capture_initiator_player_id | high |  |
| `0x1C9` | `uint8` | initial_owner_set_flag | high |  |
| `0x2C8` | `uint8` | planet_destroyed_flag | medium |  |
| `0x2E3` | `uint8` | capture_timer_active | medium |  |
| `0x2F4` | `int32` | corruption_level | high |  Values: -1=none, 0-3=corruption tiers |
