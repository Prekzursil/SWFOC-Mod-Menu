# FOWPlayerObject


Per-player fog of war state, stored in GameModeClass+0x198 array


## Fields

| Offset | Type | Name | Status / Confidence | Notes |
|--------|------|------|---------------------|-------|
| `0x00` | `pointer` | visibility_grid |  | byte per cell: 0x00=fogged, 0xFF=visible |
| `0x08` | `pointer` | reveal_timer_grid |  | short per cell |
| `0x10` | `pointer` | output_visibility_grid |  |  |
| `0x20` | `int64` | grid_cell_count |  |  |
| `0x50` | `uint8` | dirty_flag |  |  |
