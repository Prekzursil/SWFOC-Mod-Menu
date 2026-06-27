# GameObjectType


Type definition struct for game objects. Shared across all instances of a given unit/building type.


## Key Offsets

| Offset | Type | Name |
|--------|------|------|
| `0xF8` | `SSO_string` | type_name |
| `0x880` | `int32` | build_limit_global |
| `0x888` | `int32` | build_limit_per_player |
| `0x890` | `int32` | base_build_time |
| `0x894` | `int32` | tech_level_requirement |
| `0x89C` | `int32` | min_tech_level |
| `0xDCC` | `float32` | base_max_hp |
| `0xDD0` | `float32` | base_max_front_shield |
| `0xDD4` | `float32` | base_max_rear_shield |
| `0xEB0` | `pointer` | damage_threshold_array |
| `0xF0C` | `int32` | build_pad_requirement |
| `0x1648` | `bitmask` | weapon_target_type_bitmask |
| `0x1F78` | `int32` | unit_cap_contribution |
| `0x1FF4` | `int32` | damage_type_flags |
