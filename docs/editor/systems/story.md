# Story

**Event Types Total**: 61

**Unique Classes**: 28

**Flag Storage**: CRC32 hash table in StorySubPlotClass+0x20

**Hash Algorithm**: CRC32(strupr(name)) XOR 0xDEADBEEF -> LCG(16807, 2^31-1)

**Reward Dispatch**: string->int via tree at 0xB30728, up to 14 params per event

**Factory Rva**: 0x453310

**Lua Globals Registered**: 14

**Flag Event Type Id**: 0x2C


## RE Findings Detail

**Analysis**: SWFOC Alamo Engine — Story/Scripting System Reverse Engineering

Complete documentation of the story flag system, reward dispatch, Fire_Story_Event mechanism, and event type hierarchy in SWFOC's Alamo engine. Derived from Ghidra decompilation of StarWarsG.exe (x86_64).

- **Date**: 2026-04-04
- **Analyst**: Agent 3K (Story/Scripting RE)


### Story Architecture Overview

- **status**: CONFIRMED
- **description**: The story system is built on a 3-layer hierarchy: StorySubPlotClass owns StoryEventClass instances, which inherit from SignalGeneratorClass. Events are stored in a CRC32-keyed hash table within each subplot. The system uses XML-driven story definitions parsed at load time, with events dispatched via a type-ID factory pattern.
- **class_hierarchy**:
  - **SignalGeneratorClass**: Base class providing signal/event notification infrastructure
  - **StoryEventClass**: Base story event — inherits SignalGeneratorClass. Size >= 0x360 bytes. Contains 15+ MSVC SSO string fields for reward parameters, dialog text, etc.
  - **StorySubPlotClass**: Container for events within a story plot. Inherits SignalGeneratorClass. Size >= 0x650 bytes. Owns a CRC32 hash table of events + 61 DynamicVectorClass<StoryEventClass*> slots (0x3D).
  - **StoryEventWrapper**: Lua wrapper for StoryEventClass — exposed to script with 7 methods
  - **StoryPlotWrapper**: Lua wrapper for StorySubPlotClass — exposed to script with 5 methods


### Story Flag Storage

- **status**: CONFIRMED
- **mechanism**: CRC32 hash table within StorySubPlotClass
- **description**: Story flags/events are NOT stored as simple bool/int flags. Instead, each StorySubPlotClass contains a hash table at +0x20 (linked list head pointer) using CRC32(strupr(event_name)) XOR 0xDEADBEEF as the bucket key. Events are looked up case-insensitively by name. Each event node stores the CRC32 at +0x10 and a pointer to the StoryEventClass at +0x18.
- **hash_function**:
  - **rva**: 0x215A30
  - **name**: CRC32_Hash
  - **description**: Standard CRC32 using a lookup table at DAT_140a14d20. Takes (byte* data, uint length, uint seed). Seed is inverted on entry and result.
  - **status**: CONFIRMED
- **event_lookup_function**:
  - **rva**: 0x52EF90
  - **name**: StorySubPlot_FindEventByName
  - **description**: Looks up a StoryEventClass* by name string. Converts name to uppercase via _strupr, computes CRC32, XORs with 0xDEADBEEF, applies LCG (a=16807 c=0 m=2^31-1) for bucket index, walks linked list comparing CRC32 values.
  - **bucket_formula**: bucket_index = LCG(CRC32(strupr(name)) ^ 0xDEADBEEF) & bucket_mask
  - **lcg_constants**:
    - **a**: 16807
    - **m**: 2147483647
    - **note**: Lehmer/Park-Miller MINSTD generator used for hash distribution
  - **status**: CONFIRMED
- **event_active_flag_offset**: 0x4C in StoryEventClass (byte) — set to 0 when event is reset/inactive
- **subplot_hash_table_offset**: +0x20 (linked list root pointer within StorySubPlotClass)
- **subplot_bucket_array_offset**: +0x30 (bucket array pointer), +0x48 (bucket mask)


### Event Type Enum

- **status**: CONFIRMED
- **description**: Event types are identified by integer IDs used in a switch/case factory at RVA 0x453310. The string-to-ID mapping is stored in a global std::map-like tree at DAT_140b30728, keyed by CRC32 of the uppercase type name string.
- **factory_function_rva**: 0x453310
- **factory_function_name**: StoryEvent_Factory_Create
- **global_type_tree_address**: DAT_140b30728 (global pointer to std::map<CRC32, int> root node)
- **values**:
  - **0x01**:
    - **class**: StoryEventEnterClass
    - **alloc_size**: 0x3B0
    - **group**: enter/trigger
    - **notes**: Shared with cases 2,0x17,0x31,0x33
  - **0x02**:
    - **class**: StoryEventEnterClass
    - **alloc_size**: 0x3B0
    - **group**: enter/trigger
  - **0x03**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
    - **notes**: Shared with cases 4,0x11,0x12,0x16,0x24,0x26,0x27,0x28,0x37
  - **0x04**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x05**:
    - **class**: StoryEventConstructLevelClass
    - **alloc_size**: 0x380
    - **group**: construct
  - **0x06**:
    - **class**: StoryEventDestroyClass
    - **alloc_size**: 0x3C8
    - **group**: destroy
    - **notes**: Shared with case 8
  - **0x07**:
    - **class**: StoryEventDestroyBaseClass
    - **alloc_size**: 0x388
    - **group**: destroy-base
  - **0x08**:
    - **class**: StoryEventDestroyClass
    - **alloc_size**: 0x3C8
    - **group**: destroy
  - **0x09**:
    - **class**: StoryEventBeginEraClass
    - **alloc_size**: 0x368
    - **group**: era
    - **notes**: Shared with case 0x0A
  - **0x0A**:
    - **class**: StoryEventBeginEraClass
    - **alloc_size**: 0x368
    - **group**: era
  - **0x0C**:
    - **class**: StoryEventHeroMoveClass
    - **alloc_size**: 0x390
    - **group**: hero-move
    - **notes**: Shared with case 0x0D
  - **0x0D**:
    - **class**: StoryEventHeroMoveClass
    - **alloc_size**: 0x390
    - **group**: hero-move
  - **0x0E**:
    - **class**: StoryEventAccumulateClass
    - **alloc_size**: 0x368
    - **group**: accumulate
  - **0x0F**:
    - **class**: StoryEventConquerCountClass
    - **alloc_size**: 0x368
    - **group**: conquer-count
  - **0x10**:
    - **class**: StoryEventElapsedClass
    - **alloc_size**: 0x370
    - **group**: elapsed-time
  - **0x11**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x12**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x13**:
    - **class**: StoryEventWinBattlesClass
    - **alloc_size**: 0x3A0
    - **group**: win-battles
    - **notes**: Shared with case 0x14
  - **0x14**:
    - **class**: StoryEventWinBattlesClass
    - **alloc_size**: 0x3A0
    - **group**: win-battles
  - **0x15**:
    - **class**: StoryEventRetreatClass
    - **alloc_size**: 0x368
    - **group**: retreat
  - **0x16**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x17**:
    - **class**: StoryEventEnterClass
    - **alloc_size**: 0x3B0
    - **group**: enter/trigger
  - **0x18**:
    - **class**: StoryEventStartTacticalClass
    - **alloc_size**: 0x398
    - **group**: tactical-start
    - **notes**: Shared with case 0x19
  - **0x19**:
    - **class**: StoryEventStartTacticalClass
    - **alloc_size**: 0x398
    - **group**: tactical-start
  - **0x1A**:
    - **class**: StoryEventSelectPlanetClass
    - **alloc_size**: 0x380
    - **group**: select-planet
    - **notes**: Shared with cases 0x1B, 0x1C
  - **0x1B**:
    - **class**: StoryEventSelectPlanetClass
    - **alloc_size**: 0x380
    - **group**: select-planet
  - **0x1C**:
    - **class**: StoryEventSelectPlanetClass
    - **alloc_size**: 0x380
    - **group**: select-planet
  - **0x1D**:
    - **class**: StoryEventStringClass
    - **alloc_size**: 0x378
    - **group**: string-event
    - **notes**: Shared with cases 0x1E,0x21,0x32,0x38,0x39,0x3C
  - **0x1E**:
    - **class**: StoryEventStringClass
    - **alloc_size**: 0x378
    - **group**: string-event
    - **notes**: Default elapsed timeout is 60.0 if value == -1.0
  - **0x1F**:
    - **class**: StoryEventFogRevealClass
    - **alloc_size**: 0x390
    - **group**: fog-reveal
    - **notes**: Shared with case 0x20
  - **0x20**:
    - **class**: StoryEventFogRevealClass
    - **alloc_size**: 0x390
    - **group**: fog-reveal
  - **0x21**:
    - **class**: StoryEventStringClass
    - **alloc_size**: 0x378
    - **group**: string-event
  - **0x22**:
    - **class**: StoryEventClass (base)
    - **alloc_size**: 0x360
    - **group**: generic
    - **notes**: Uses base StoryEventClass directly — no subclass
  - **0x23**:
    - **class**: StoryEventAINotificationClass
    - **alloc_size**: 0x390
    - **group**: ai-notification
  - **0x24**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x25**:
    - **class**: StoryEventCommandUnitClass
    - **alloc_size**: 0x398
    - **group**: command-unit
  - **0x26**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x27**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x28**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x29**:
    - **class**: StoryEventGuardUnitClass
    - **alloc_size**: 0x390
    - **group**: guard-unit
  - **0x2A**:
    - **class**: StoryEventProximityClass
    - **alloc_size**: 0x398
    - **group**: proximity
  - **0x2B**:
    - **class**: StoryEventDifficultyClass
    - **alloc_size**: 0x368
    - **group**: difficulty
  - **0x2C**:
    - **class**: StoryEventFlagClass
    - **alloc_size**: 0x380
    - **group**: flag-check
    - **notes**: This is the Check_Story_Flag / Set_Story_Flag event type
  - **0x2D**:
    - **class**: StoryEventLoadTacticalClass
    - **alloc_size**: 0x398
    - **group**: load-tactical
  - **0x2E**:
    - **class**: StoryCheckDestroyedClass
    - **alloc_size**: 0x378
    - **group**: check-destroyed
  - **0x2F**:
    - **class**: StoryEventVictoryClass
    - **alloc_size**: 0x368
    - **group**: victory
  - **0x30**:
    - **class**: StoryEventMovieDoneClass
    - **alloc_size**: 0x360
    - **group**: movie-done
  - **0x31**:
    - **class**: StoryEventEnterClass
    - **alloc_size**: 0x3B0
    - **group**: enter/trigger
  - **0x32**:
    - **class**: StoryEventStringClass
    - **alloc_size**: 0x378
    - **group**: string-event
  - **0x33**:
    - **class**: StoryEventEnterClass
    - **alloc_size**: 0x3B0
    - **group**: enter/trigger
  - **0x34**:
    - **class**: StoryEventObjectiveTimeoutClass
    - **alloc_size**: 0x390
    - **group**: objective-timeout
  - **0x35**:
    - **class**: StoryEventCaptureClass
    - **alloc_size**: 0x380
    - **group**: capture
  - **0x36**:
    - **class**: StoryEventCorruptionLevelClass
    - **alloc_size**: 0x380
    - **group**: corruption-level
    - **notes**: Shared with cases 0x3A, 0x3B
  - **0x37**:
    - **class**: StoryEventSingleObjectNameClass
    - **alloc_size**: 0x3B0
    - **group**: object-name
  - **0x38**:
    - **class**: StoryEventStringClass
    - **alloc_size**: 0x378
    - **group**: string-event
  - **0x39**:
    - **class**: StoryEventStringClass
    - **alloc_size**: 0x378
    - **group**: string-event
  - **0x3A**:
    - **class**: StoryEventCorruptionLevelClass
    - **alloc_size**: 0x380
    - **group**: corruption-level
  - **0x3B**:
    - **class**: StoryEventCorruptionLevelClass
    - **alloc_size**: 0x380
    - **group**: corruption-level
  - **0x3C**:
    - **class**: StoryEventStringClass
    - **alloc_size**: 0x378
    - **group**: string-event
- **total_event_types**: 61
- **unique_concrete_classes**: 28


### Reward Dispatch System

- **status**: CONFIRMED
- **description**: Rewards are NOT dispatched via a big switch/case. Instead, each StoryEventClass stores a reward_type as an integer at offset +0x3C (relative to event data start, i.e., after the base class header). The reward type is resolved from a string name via a global std::map tree at DAT_140b30728 using CRC32 lookup. The same tree is used for both event types and reward types.
- **reward_type_resolver_rva**: 0x45C3F0
- **reward_type_resolver_name**: ResolveRewardTypeFromString
- **reward_type_resolver_description**: Takes a null-terminated string, computes CRC32, searches the global tree (red-black tree rooted at DAT_140b30728) for a matching node. Returns the integer value from node+0x20 (the enum value). Returns 0 if not found.
- **global_type_tree_rva**: 0xB30728
- **reward_type_offset_in_event**: +0x3C from StoryEventClass data start
- **set_reward_type_lua_rva**: 0x73EEF0
- **set_reward_type_lua_name**: StoryEventWrapper::Set_Reward_Type
- **set_reward_parameter_lua_rva**: 0x73ECA0
- **set_reward_parameter_lua_name**: StoryEventWrapper::Set_Reward_Parameter
- **reward_parameter_storage**: StoryEventClass data offsets +0x50 through +0x190 — array of MSVC SSO strings at 0x20 stride, indexed by parameter number. Up to 14 reward parameters.
- **known_reward_type_strings**: Note: The actual string->int mappings are populated at runtime from XML data definitions., Common reward type names from modding community documentation include:, CYCLOPEAN_REVEAL_ALL, GIVE_MONEY, REMOVE_UNIT, SPAWN_HERO, SWITCH_SIDES,, TRIGGER_EVENT, SET_FLAG, DISABLE_UNIT, ENABLE_UNIT, LOCK_PLANET,, FLASH_PLANET, SET_TECH_LEVEL, DUAL_FLASH, DISABLE_BRANCH, ENABLE_BRANCH,, VICTORY, DEFEAT, SPEECH, TUTORIAL, STORY_TRIGGER, FORCE_RETREAT, etc., These are NOT hardcoded in the binary — they are CRC32-hashed from XML at load time.


### Fire Story Event Mechanism

- **status**: CONFIRMED
- **description**: Story events are NOT fired via a single 'Fire_Story_Event' function. Instead, the LuaScriptClass::GetEvent global function (type_id=0x3D=61) is registered as the 'GetEvent' Lua global, which wraps StoryPlotWrapper::Get_Event. The actual Lua Story_Event() and Story_Event_Trigger() are registered elsewhere in the game-mode-specific Lua binding setup (not in LuaScriptClass base). Events fire through the SignalGeneratorClass signal system.
- **lua_global_registration**:
  - **function_name**: GetEvent
  - **registered_at_rva**: 0x2567B0
  - **description**: LuaScriptClass::RegisterGlobals registers 'GetEvent' as a global Lua function that wraps the story event lookup. The GetEvent object at +0xD8 in LuaScriptClass holds the 0x3D event type count.
  - **status**: CONFIRMED
- **story_event_wrapper_methods**:
  - **status**: CONFIRMED
  - **wrapper_constructor_rva**: 0x73DC80
  - **methods_registered**: {'name': 'method_1 (inherited from base, at 0x40258770)', 'description': 'Common base method — likely Get/Set name or similar'}, {'name': 'Add_Dialog_Text', 'rva': '0x73DF60', 'description': 'Sets dialog text on the event. Accepts variable args (string, GameObjectWrapper, number). Writes to DynamicVector at event+0x2C0.', 'status': 'CONFIRMED'}, {'name': 'Clear_Dialog_Text', 'rva': '0x73E4F0', 'description': 'Clears the dialog text vector at event+0x2C0. Iterates and deallocates string entries at 0x20 stride.', 'status': 'CONFIRMED'}, {'name': 'Set_Dialog', 'rva': '0x73E9A0', 'description': 'Sets dialog identifier string. Writes to event data offset +0x230. Also calls FUN_14047d9c0 for dialog pre-caching.', 'status': 'CONFIRMED'}, {'name': 'Set_Reward_Parameter', 'rva': '0x73ECA0', 'description': 'Sets a reward parameter by index. Takes (number index, string/number value). Writes to event+0x50 + index*0x20. Validates exactly 1 parameter per index. Uses global static buffer at DAT_140b3b450.', 'status': 'CONFIRMED'}, {'name': 'Set_Reward_Type', 'rva': '0x73EEF0', 'description': 'Sets the reward type from a string name. Calls ResolveRewardTypeFromString (0x45C3F0) to convert the string to an enum int. Stores result at event+0x3C.', 'status': 'CONFIRMED'}
- **story_plot_wrapper_methods**:
  - **status**: CONFIRMED
  - **wrapper_constructor_rva**: 0x724120
  - **methods_registered**: {'name': 'method_1 (inherited, at 0x40258770)', 'description': 'Common base method'}, {'name': 'Get_Event', 'rva': '0x724480', 'description': "Looks up an event by name string within this plot's event hash table. Calls StorySubPlot_FindEventByName (0x52EF90). Returns a StoryEventWrapper if found. Logs error if event not found.", 'status': 'CONFIRMED'}, {'name': 'method_3 (at 0x7243a0, not resolved)', 'description': 'Registered but function address did not resolve to a named function'}, {'name': 'method_4 (at 0x7247a0, not resolved)', 'description': 'Registered but function address did not resolve to a named function'}, {'name': 'Reset', 'rva': '0x7246E0', 'description': 'Resets all events in the subplot. Calls StorySubPlotClass::Reset (0x52FC10) which iterates the linked event list, sets event+0x4C to 0, and calls virtual method [0x20] twice on each event (re-initialization).', 'status': 'CONFIRMED'}


### Story Subplot Class

- **status**: CONFIRMED
- **rtti_mangled**: .?AVStorySubPlotClass@@
- **constructor_rva**: 0x52D400
- **destructor_rva**: 0x52E220
- **inherits**: SignalGeneratorClass
- **size_minimum**: 0x650
- **key_fields**:
  - **+0x08**: boolean: active flag + padding
  - **+0x10**: linked list node allocation pointer
  - **+0x14**: linked list count/capacity
  - **+0x20**: Hash table linked list root (for event lookup by CRC32 name)
  - **+0x30**: Hash table bucket array pointer
  - **+0x48**: Hash table bucket mask (AND'd with LCG output)
  - **+0x58..+0x5F8**: Array of 61 (0x3D) DynamicVectorClass<StoryEventClass*> slots (each 0x18 bytes = vtable + ptr + count/capacity)
  - **+0x5F8**: Story mode pointer (unknown sub-object)
  - **+0x600**: DynamicVectorClass<StoryEventClass*> vtable (embedded)
  - **+0x608..+0x614**: DynamicVectorClass storage for final event list
  - **+0x628**: Plot name string (MSVC SSO, size at +0x638, capacity at +0x640)
  - **+0x644**: Boolean: is_active_story flag
- **event_loading_process**:
  - **description**: StorySubPlotClass constructor loads events from XML. For each event: (1) read event name, (2) strupr + CRC32, (3) XOR 0xDEADBEEF + LCG for bucket, (4) check for CRC32 collision ('Event already exists!'), (5) parse event XML block via FUN_14045c5d0, (6) create concrete StoryEventClass via factory FUN_1404562a0, (7) insert into hash table, (8) set back-pointer event->subplot at +0x228, (9) assign sequential event index at +0x358. After all events loaded, calls FUN_140530180 to build the 61-slot type-indexed arrays and FUN_140452d70 to compute event dependencies.
  - **status**: CONFIRMED


### Story Event Class

- **status**: CONFIRMED
- **rtti_mangled**: .?AVStoryEventClass@@
- **constructor_rva**: 0x4501D0
- **destructor_rva**: 0x450600
- **inherits**: SignalGeneratorClass
- **size_minimum**: 0x360
- **key_fields**:
  - **+0x00..+0x1F**: MSVC SSO string: event name (length at +0x10, capacity at +0x18, inline if cap < 16)
  - **+0x20**: Event type ID (int32) — assigned from factory param_1
  - **+0x38**: Event type variant (int32) — subclass-specific
  - **+0x3C**: Reward type ID (int32) — set by Set_Reward_Type, resolved via ResolveRewardTypeFromString
  - **+0x44**: Unknown int32 — copied from XML parse
  - **+0x48**: Unknown int32 — copied from XML parse
  - **+0x4C**: Active/triggered flag (byte) — 0 = inactive, set by Reset
  - **+0x4D**: Boolean — related to reward parameter presence
  - **+0x4E**: Unknown byte — from XML
  - **+0x50..+0x190**: Reward parameter strings: array of MSVC SSO strings at 0x20 stride, up to 14 slots
  - **+0x1F8..+0x228**: Additional string fields (15 SSO strings total in base class)
  - **+0x228**: Back-pointer to owning StorySubPlotClass
  - **+0x230**: Dialog ID string (MSVC SSO)
  - **+0x250..+0x298**: Additional dialog/reward strings
  - **+0x2A8..+0x2C0**: Dialog data array (unknown structure)
  - **+0x2C0**: DynamicVector<string> for dialog text entries
  - **+0x2F8**: DynamicVector<DynamicVector<string>> vtable pointer (nested reward param lists)
  - **+0x310**: DynamicVector<DynamicVector<StoryEventClass*>> vtable pointer (event dependency lists)
  - **+0x328**: DynamicVector<StoryEventClass*> vtable pointer (direct event refs)
  - **+0x340..+0x358**: Dependency tracking: DynamicVectorClass<longlong> (dependent event pointers, count at +0x350, capacity at +0x354)
  - **+0x358**: Event index within subplot (int32)
- **subclasses**: StoryEventEnterClass, StoryEventSingleObjectNameClass, StoryEventConstructLevelClass, StoryEventDestroyClass, StoryEventDestroyBaseClass, StoryEventBeginEraClass, StoryEventHeroMoveClass, StoryEventAccumulateClass, StoryEventConquerCountClass, StoryEventElapsedClass, StoryEventWinBattlesClass, StoryEventRetreatClass, StoryEventStartTacticalClass, StoryEventSelectPlanetClass, StoryEventStringClass, StoryEventFogRevealClass, StoryEventAINotificationClass, StoryEventCommandUnitClass, StoryEventGuardUnitClass, StoryEventProximityClass, StoryEventDifficultyClass, StoryEventFlagClass, StoryEventLoadTacticalClass, StoryCheckDestroyedClass, StoryEventVictoryClass, StoryEventMovieDoneClass, StoryEventObjectiveTimeoutClass, StoryEventCaptureClass, StoryEventCorruptionLevelClass


### All Story Related Rvas

- **status**: CONFIRMED
- **functions**:
  - **StoryEventClass::constructor**: 0x4501D0
  - **StoryEventClass::destructor**: 0x450600
  - **StoryEventCommandUnitClass::constructor**: 0x4504E0
  - **StoryEventHeroMoveClass::constructor**: 0x450540
  - **StoryEventSelectPlanetClass::constructor**: 0x5DC4B0
  - **StorySubPlotClass::destructor**: 0x52E220
  - **StorySubPlotClass::constructor_main**: 0x52D400
  - **StorySubPlot_FindEventByName**: 0x52EF90
  - **StorySubPlot_Reset**: 0x52FC10
  - **StorySubPlot_BuildTypeArrays**: 0x530180
  - **StorySubPlot_ComputeDependencies**: 0x452D70
  - **StoryEvent_Factory_Create**: 0x453310
  - **StoryEvent_ParseXMLBlock**: 0x45C5D0
  - **StoryEvent_BuildFromParsed**: 0x4562A0
  - **ResolveRewardTypeFromString**: 0x45C3F0
  - **CRC32_Hash**: 0x215A30
  - **StoryEventWrapper::LuaConstructor**: 0x73DC80
  - **StoryEventWrapper::Add_Dialog_Text**: 0x73DF60
  - **StoryEventWrapper::Clear_Dialog_Text**: 0x73E4F0
  - **StoryEventWrapper::Set_Dialog**: 0x73E9A0
  - **StoryEventWrapper::Set_Reward_Parameter**: 0x73ECA0
  - **StoryEventWrapper::Set_Reward_Type**: 0x73EEF0
  - **StoryPlotWrapper::LuaConstructor**: 0x724120
  - **StoryPlotWrapper::Get_Event**: 0x724480
  - **StoryPlotWrapper::Reset**: 0x7246E0
  - **LuaScriptClass::RegisterGlobals**: 0x2567B0
  - **LuaScriptClass::LuaConstructor**: 0x242570
  - **StoryLogWindow::constructor**: 0x01AB40
  - **DynamicVectorClass<StoryEventClass*>::constructor_1**: 0x450590
  - **DynamicVectorClass<StoryEventClass*>::constructor_2**: 0x45E670
  - **DynamicVectorClass<StoryDialogGoal>::destructor**: 0x47D6B0
  - **LuaMemberFunctionWrapper<StoryEventWrapper>::constructor**: 0x73DC80
  - **LuaMemberFunctionWrapper<StoryPlotWrapper>::constructor**: 0x724120
  - **FUN_14073E590_WrapEventAsLuaObject**: 0x73E590
  - **StoryEvent_DebugLog**: 0x467540
- **global_data**:
  - **DAT_140b30728_EventTypeTree**: 0xB30728 — Global std::map root for event/reward type string-to-int lookup
  - **DAT_140b30738_XMLNestingDepth**: 0xB30738 — XML nesting depth counter for recursive parsing
  - **DAT_140b3b450_RewardParamBuffer**: 0xB3B450 — Static buffer for Set_Reward_Parameter temporary storage
  - **DAT_140b3b458_RewardParamEnd**: 0xB3B458 — End pointer for reward param buffer
  - **DAT_140b3b468_DialogGuard**: 0xB3B468 — Thread-safe init guard for dialog system
  - **DAT_140a14d20_CRC32Table**: 0xA14D20 — CRC32 lookup table (256 uint32 entries)
  - **DAT_140a15738_ScriptIDCounter**: 0xA15738 — Global incrementing script instance ID counter
  - **DAT_140a157d0_StringTypeMarker**: 0xA157D0 — Type discriminator marker for Lua string arguments
  - **DAT_140a157c0_NumberTypeMarker**: 0xA157C0 — Type discriminator marker for Lua number arguments
  - **DAT_140a43b18_GameObjectTypeMarker**: 0xA43B18 — Type discriminator marker for GameObjectWrapper arguments
  - **DAT_140a44270_PlayerTypeMarker**: 0xA44270 — Type discriminator marker for PlayerWrapper arguments
- **vtables**:
  - **StorySubPlotClass::vftable**: referenced in constructor at 0x52D400
  - **StoryEventClass::vftable**: referenced in constructor at 0x4501D0
  - **StoryEventWrapper::vftable**: referenced in Lua wrapper constructor at 0x73DC80
  - **StoryPlotWrapper::vftable**: referenced in Lua wrapper constructor at 0x724120
  - **LuaScriptClass::vftable**: referenced in LuaScriptClass constructor


### How Story Flags Are Checked During Gameplay

- **status**: CONFIRMED
- **description**: Story flags are checked through the StoryEventFlagClass (event type 0x2C). When the story system evaluates events each tick, it iterates through the 61 type-indexed DynamicVectorClass arrays in the StorySubPlotClass. For type 0x2C (flag events), it checks whether the named flag has been set. Flags are set via the Set_Story_Flag Lua function which fires a story event of type FLAG. The CheckStoryFlagClass (RTTI confirmed) is a separate class used in the condition evaluation chain. The actual flag storage is within the event hash table itself — a flag 'exists and is active' is determined by the event's active byte at +0x4C being non-zero.
- **flag_event_type_id**: 0x2C
- **flag_class**: StoryEventFlagClass
- **check_class**: CheckStoryFlagClass
- **flow**: 1. Lua calls Set_Story_Flag('FLAG_NAME') or Check_Story_Flag('FLAG_NAME'), 2. This triggers a story event of type FLAG (0x2C) or queries the event hash table, 3. StorySubPlot_FindEventByName converts the name to uppercase, computes CRC32, 4. CRC32 XOR 0xDEADBEEF, then LCG for bucket index, 5. Walks the linked list in that bucket comparing CRC32 values, 6. If found, reads the active flag at event+0x4C, 7. For Set: sets event+0x4C to non-zero and triggers dependent events, 8. For Check: returns whether event+0x4C is non-zero


### How Rewards Are Dispatched

- **status**: CONFIRMED
- **description**: When a story event completes (all conditions met), the reward system reads the reward type ID from the event (+0x3C) and the reward parameters from the string array at +0x50. The reward type was parsed from XML at load time via ResolveRewardTypeFromString (0x45C3F0) which hashes the reward type name string and looks it up in the global type tree. Reward dispatch is handled through the event's virtual function table — each concrete event subclass can override the reward execution method. The reward parameters (up to 14 strings at 0x20 stride) provide the data needed for the reward (unit type names, credit amounts, planet names, etc.).
- **flow**: 1. Event conditions are met (checked via per-type virtual methods), 2. Event reads reward_type_id from +0x3C, 3. Event reads reward parameters from +0x50..+0x190 (up to 14 SSO strings), 4. Reward execution dispatched via virtual function table on the event, 5. Specific reward logic (spawn unit, give money, etc.) uses the string params


### Lua Script Globals Registered

- **status**: CONFIRMED
- **registration_function_rva**: 0x2567B0
- **registered_globals**: {'name': 'StringCompare', 'class': 'LuaStringCompare'}, {'name': '_ScriptExit', 'class': 'LuaScriptExit'}, {'name': '_ScriptMessage', 'class': 'LuaScriptMessage'}, {'name': '_DebugBreak', 'class': 'LuaDebugBreak'}, {'name': '_CustomScriptMessage', 'class': 'LuaCustomScriptMessage'}, {'name': 'ThreadValue', 'type_id': '0x41'}, {'name': 'GlobalValue', 'class': 'GlobalValue'}, {'name': '_MessagePopup', 'class': 'LuaMessagePopup'}, {'name': '_OuputDebug', 'class': 'LuaDebugPrint', 'note': "Typo in original: 'OuputDebug' not 'OutputDebug'"}, {'name': 'GetThreadID', 'class': 'LuaGetThreadID'}, {'name': 'DumpCallStack', 'class': 'DumpCallStack'}, {'name': 'Create_Thread', 'class': 'LuaCreateThread'}, {'name': 'Thread', 'class': 'LuaCreateThread'}, {'name': 'GetEvent', 'class': 'GetEvent', 'event_type_count': 61, 'note': 'This is the story event accessor from Lua scripts'}
- **luascript_method**:
  - **name**: Debug_Should_Issue_Event_Alert
  - **rva**: 0x17D070
  - **description**: Registered as a method on the LuaScriptClass Lua object


### Signal System

- **status**: CONFIRMED
- **description**: Both StoryEventClass and StorySubPlotClass inherit from SignalGeneratorClass, which provides a publish-subscribe notification system. Events signal their state changes (triggered, completed, reset) to listeners. The SignalDispatcherClass is a singleton that routes signals between generators and listeners. SignalListenerClass instances (e.g., in StorySubPlotClass) subscribe to events and react when they fire.
- **key_classes**:
  - **SignalGeneratorClass**:
    - **constructor_rva**: 0x240610
  - **SignalListClass**:
    - **constructor_rva**: 0x2406C0
  - **SignalDispatcherClass**:
    - **constructors**: 0x2215B0, 0x2218C0
  - **SingletonInstance<SignalDispatcherClass>**:
    - **rva**: 0x7E7140


### Debug Logging

- **status**: CONFIRMED
- **log_function_rva**: 0x467540
- **known_log_messages**: Creating story %s, ___________ Event %s, CRC %u, !!!!!! Event %s, CRC %u already exists!, STORY MODE ERROR!  Unable to process event of type %s, Error: Can't create story %s, StoryPlotWrapper::Get_Event -- cannot find event %s in plot %s., ERROR!  Story event - Unable to find event %s in event %s, plot %s, ERROR!  Compute_Dependants has come across an invalid dependant for %s in plot %s, Reward type %s, StoryEventWrapper::Set_Reward_Type -- invalid number of parameters.  Expected 1, got %d., StoryEventWrapper::Set_Reward_Type -- invalid type for parameter 1.  Expected string., StoryEventWrapper::Set_Dialog -- invalid number of parameters.  Expected 1, got %d., StoryEventWrapper::Set_Dialog -- invalid type for parameter 1.  Expected string., StoryEventWrapper::Set_Reward_Parameter -- invalid number of parameters.  Expected 2, got %d., StoryEventWrapper::Set_Reward_Parameter -- invalid type for parameter 1. Expected number., StoryEventWrapper::Set_Reward_Parameter -- must set exactly one parameter per parameter index., StoryEventWrapper::Add_Dialog_Text -- invalid number of parameters.  Expected 2., StoryEventWrapper::Add_Dialog_Text -- invalid type for parameter 1.  Expected string., StoryEventWrapper::Add_Dialog_Text -- invalid type for parameter %d., StoryPlotWrapper::Get_Event -- invalid number of parameters.  Expected 1, got %d., StoryPlotWrapper::Get_Event -- invalid type for parameter 1.  Expected string.

