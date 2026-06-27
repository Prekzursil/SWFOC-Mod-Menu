# SWFOC Reverse Engineering Knowledge Base

Documentation for the Alamo engine as used in Star Wars: Empire at War -- Forces of Corruption (64-bit Steam build).

## Binary Info

- **Module**: StarWarsG.exe
- **Architecture**: x86_64
- **Compiler**: MSVC
- **Ghidra Base**: `0x140000000`
- **Lua Version**: 5.0.2
- **RTTI Classes**: 1703

## Structs

- [CameraClass](structs/cameraclass.md)
- [FOWPlayerObject](structs/fowplayerobject.md)
- [GameObjectClass](structs/gameobjectclass.md)
- [GameObjectType](structs/gameobjecttype.md)
- [ObjectUnderConstructionClass](structs/objectunderconstructionclass.md)
- [PlanetaryDataPackClass](structs/planetarydatapackclass.md)
- [PlayerClass](structs/playerclass.md)
- [StoryEventClass](structs/storyeventclass.md)
- [StorySubPlotClass](structs/storysubplotclass.md)
- [lua_State](structs/lua-state.md)

## Game Systems

- [Ai](systems/ai.md)
- [Camera Selection](systems/camera-selection.md)
- [Combat](systems/combat.md)
- [Galactic Map](systems/galactic-map.md)
- [Network](systems/network.md)
- [Production](systems/production.md)
- [Save Format](systems/save-format.md)
- [Story](systems/story.md)

## Reference

- [Lua API (405 functions)](lua-api.md)
- [RVA Table (280+ addresses)](rvas.md)
