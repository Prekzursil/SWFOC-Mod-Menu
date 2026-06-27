# StoryEventClass

**RTTI**: `.?AVStoryEventClass@@`

**Inherits**: `SignalGeneratorClass`

**Size**: 0x360 bytes


## Key Offsets

| Offset | Type | Name |
|--------|------|------|
| `0x00-0x1F` | | Event name SSO string |
| `0x20` | | Event type ID (int32) |
| `0x3C` | | Reward type ID (int32) |
| `0x4C` | | Active/triggered flag (byte) |
| `0x50-0x190` | | Reward parameters (14 SSO strings, 0x20 stride) |
| `0x228` | | Back-pointer to StorySubPlotClass |
| `0x230` | | Dialog ID string |
| `0x2C0` | | Dialog text entries DVec |
| `0x358` | | Event index within subplot |
