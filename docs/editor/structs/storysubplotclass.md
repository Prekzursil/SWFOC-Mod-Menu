# StorySubPlotClass

**RTTI**: `.?AVStorySubPlotClass@@`

**Inherits**: `SignalGeneratorClass`

**Size**: 0x650 bytes


## Key Offsets

| Offset | Type | Name |
|--------|------|------|
| `0x20` | | Hash table linked list root (event lookup) |
| `0x30` | | Hash table bucket array pointer |
| `0x48` | | Hash table bucket mask |
| `0x58-0x5F8` | | 61 DynamicVectorClass<StoryEventClass*> slots (0x18 each) |
| `0x628` | | Plot name string (MSVC SSO) |
| `0x644` | | is_active_story flag |
