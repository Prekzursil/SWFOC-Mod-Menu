# CameraClass


## Fields

| Offset | Type | Name | Status / Confidence | Notes |
|--------|------|------|---------------------|-------|
| `outer` | `?` | ? |  |  |
| `sub_object` | `?` | ? |  |  |

### outer

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x00` | `pointer` | vtable_ptr |  |
| `0x04` | `int32` | ref_count |  |
| `0x10` | `float32[12]` | orientation_matrix_copy |  |
| `0x40` | `pointer` | camera_state_ptr |  |

### sub_object

| Offset | Type | Name | Notes |
|--------|------|------|-------|
| `0x00` | `float32[12]` | orientation_matrix |  |
| `0x0C` | `float32` | position_x |  |
| `0x1C` | `float32` | position_y |  |
| `0x2C` | `float32` | position_z |  |
| `0x30` | `float32` | near_clip | 1.0 |
| `0x34` | `float32` | far_clip | 1000.0 |
| `0x44` | `int32` | projection_mode |  |
| `0x48` | `float32` | fov_radians | 0.785 |
| `0x4C` | `float32` | aspect_ratio | 1.333 |
| `0x74` | `float32` | viewport_x |  |
| `0x78` | `float32` | viewport_y |  |
| `0x7C` | `float32` | viewport_width |  |
| `0x80` | `float32` | viewport_height |  |
| `0xB4` | `float32[16]` | projection_matrix |  |
