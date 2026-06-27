# Network

**Architecture**: peer-to-peer deterministic lockstep


### Transport

- SteamAsyncSocket(0x6B370)
- WinsockAsyncSocket(0x227110)


### Sync Events

| Key | Value |
|-----|-------|
| FrameInfo | 0 |
| FrameSync | 17 |
| PerformanceMetrics | 18 |

**Total Event Types**: 58

**Serialization**: BitStreamClass (bit-packed, not byte-aligned)


### Sentinel Values

| Key | Value |
|-----|-------|
| 0x3FFFFF | null object ref |
| 0xFFFFFFFF | unscheduled/invalid |


## RE Findings Detail

**Analysis**: SWFOC Alamo Engine — Network/Multiplayer Protocol Documentation

Reverse-engineered documentation of the multiplayer architecture, command event format, synchronization mechanism, and desync-relevant memory areas in Star Wars: Forces of Corruption (Alamo engine, x86_64 Steam build).

- **Date**: 2026-04-04
- **Analyst**: Agent 3G (Network/Multiplayer Protocol RE)


### Architecture

- **type**: peer_to_peer_lockstep
- **summary**: The Alamo engine uses a deterministic lockstep architecture. All peers execute the same simulation independently, synchronizing only player commands (events). There is no authoritative server — every peer runs the full game simulation. The FrameSyncEventClass (event ID 17) acts as the lockstep barrier, and FrameInfoEventClass (event ID 0) exchanges per-frame metadata. PerformanceMetricsEventClass (event ID 18) likely carries timing/checksum data for desync detection.
- **evidence**: FrameSyncEventClass inherits ScheduledEventClass -> EventClass, sets event_type_id = 0x11 (17). It is registered via EventFactoryClass<FrameSyncEventClass,17>. The name 'FrameSync' directly indicates lockstep frame synchronization., FrameInfoEventClass (event ID 0) is the lowest-numbered event and inherits directly from EventClass (not ScheduledEventClass), suggesting it is a meta/control event exchanged every frame., PerformanceMetricsEventClass (event ID 18 = 0x12) also inherits directly from EventClass (not ScheduledEventClass), indicating it is a non-gameplay control message — likely carrying frame timing and/or checksum data., All gameplay-affecting events (Attack, Move, Production, etc.) inherit from ScheduledEventClass, meaning they are queued and executed at a deterministic frame. This is the hallmark of lockstep: commands are scheduled for a future frame, then all peers execute them simultaneously., No 'Replicate', 'NetState', 'AuthoritativeServer', or state-replication classes were found. All discovered event classes are player commands, not state snapshots — confirming command-based lockstep rather than state replication., The GameModeClass constructor allocates a DynamicVectorClass<struct_DelayedEventStruct*> — a queue for events that must wait until their scheduled frame arrives.


### Transport Layer

- **summary**: Dual transport: Winsock (direct IP/LAN) and Steam Networking (online via Steam). Both implement an AsyncSocket interface, abstracted behind PacketHandlerClass which runs on its own thread.
- **socket_implementations**:
  - **SteamAsyncSocketImpl**:
    - **constructor_rva**: 0x6b370
    - **description**: Steam-based socket using Valve's ISteamNetworking relay. Wraps Steam P2P networking API. Initialized with linked list node (0x30 bytes) and 64-bit SteamID handle at offset 0x20.
    - **inherits**: RefCountClass
  - **WinsockAsyncSocketImpl**:
    - **constructor_rva**: 0x227110
    - **description**: Traditional Winsock socket for LAN/direct-IP games. Constructor takes a 16-byte param (likely sockaddr_in: IP + port). Calls WSAGetLastError (imported at 0x227400) for error handling.
    - **inherits**: RefCountClass
- **packet_handler**:
  - **class**: PacketHandlerClass
  - **destructor_rva**: 0x2054c0
  - **description**: Thread-based packet processing engine. Inherits from ThreadClass. Uses Windows CriticalSection for thread safety, a mutex with 10-second timeout for shutdown synchronization, and a linked-list packet queue. Dispatches received packets to registered callback functions typed as: void(*)(PacketHandlerClass*, void*, IPAddressClass&). Also supports raw byte callbacks: bool(*)(unsigned char*, int, IPAddressClass*, void*).
  - **threading_note**: PacketHandlerClass::~PacketHandlerClass logs 'ThreadLockMutexClass -- %s failed to obtain mutex within 10 seconds' — confirming it runs on a dedicated network thread separate from the game thread.
- **packet_class**:
  - **class**: PacketClass
  - **constructor_rva**: 0x23bc40
  - **description**: Network packet. Inherits from BitStreamClass (bit-level serialization) and MultiLinkedListMember (for queue management). Constructor takes (int size, longlong buffer). If buffer is null and size > 0, allocates its own buffer. The BitStreamClass base provides bit-granularity read/write with separate read and write cursors.
  - **inherits**: BitStreamClass, MultiLinkedListMember, RefCountClass
  - **serialization_format**: bit-packed via BitStreamClass — fields are written at arbitrary bit widths, not byte-aligned. This is typical for lockstep RTS games to minimize bandwidth.
- **ip_address_class**:
  - **class**: IPAddressClass
  - **description**: Wraps an IP address. Used in packet dispatch callbacks and maintained in DynamicVectorClass<IPAddressClass> collections.
- **broadcaster_class**:
  - **class**: BroadcasterClass
  - **description**: Manages packet distribution to connected peers. Contains a DynamicVectorClass<PacketIdentifierStruct> for tracking sent packets and a DynamicVectorClass<ConnectionClass*> for the peer list. Also referenced as BroadcasterClass::Get_Local_Player in the knowledge base, indicating it tracks which peer is the local player.
  - **inner_types**: BroadcasterClass::PacketIdentifierStruct
- **connection_class**:
  - **class**: ConnectionClass
  - **description**: Represents a single peer connection. Stored in DynamicVectorClass<ConnectionClass*> managed by BroadcasterClass.
- **packet_type_class**:
  - **class**: PacketTypeClass
  - **description**: Packet type registry with magic number validation. Contains DynamicVectorClass<PacketTypeClass::tPacketMagicStruct> — each packet type has a 'magic' identifier for protocol-level validation and dispatch.
  - **inner_types**: PacketTypeClass::tPacketMagicStruct


### Steam Integration

- **summary**: Online multiplayer uses Steam lobbies via SteamPeerLobbyClass (singleton). Matchmaking, chat, mod sync, and player management all flow through Steam callbacks.
- **steam_api_imports**: SteamInternal_CreateInterface, SteamInternal_ContextInit, SteamAPI_GetHSteamUser, SteamAPI_GetHSteamPipe, SteamAPI_Init, SteamAPI_RegisterCallback, SteamAPI_UnregisterCallback, SteamAPI_RegisterCallResult, SteamAPI_UnregisterCallResult, SteamAPI_RunCallbacks, SteamAPI_Shutdown
- **steam_peer_lobby_class**:
  - **class**: SteamPeerLobbyClass
  - **constructor_rva**: 0x6ca10
  - **singleton**: True
  - **description**: Central Steam multiplayer manager. Registers 6 persistent callbacks and 3 call-result handlers with the Steam API during construction.
  - **steam_callbacks_registered**: {'type': 'LobbyInvite_t', 'callback_id': '0x1F7 (503)', 'handler_rva': '0x72540', 'description': 'Fires when the local user receives a lobby invite from a friend.'}, {'type': 'LobbyKicked_t', 'callback_id': '0x200 (512)', 'handler_rva': '0x72550', 'description': 'Fires when the local user is kicked from a lobby.'}, {'type': 'LobbyChatMsg_t', 'callback_id': '0x1FB (507)', 'handler_rva': '0x72570', 'description': 'Fires when a chat message is received in the lobby.'}, {'type': 'LobbyDataUpdate_t', 'callback_id': '0x1F9 (505)', 'handler_rva': '0x726C0', 'description': 'Fires when lobby metadata changes (player ready state, game settings, etc.).'}, {'type': 'LobbyChatUpdate_t', 'callback_id': '0x1FA (506)', 'handler_rva': '0x726D0', 'description': 'Fires when a user joins, leaves, or disconnects from the lobby.'}, {'type': 'ItemInstalled_t', 'callback_id': '0xD4D (3405)', 'handler_rva': '0x72730', 'description': 'Fires when a Steam Workshop item (mod) is installed.'}
  - **steam_call_results**: {'type': 'LobbyMatchList_t', 'callback_id': '0x1FE (510)', 'description': 'Result of ISteamMatchmaking::RequestLobbyList — returns available lobbies.'}, {'type': 'LobbyCreated_t', 'callback_id': '0x201 (513)', 'description': 'Result of ISteamMatchmaking::CreateLobby — returns new lobby ID.'}, {'type': 'LobbyEnter_t', 'callback_id': '0x1F8 (504)', 'description': 'Result of ISteamMatchmaking::JoinLobby — confirms lobby entry.'}
  - **ugc_integration**: CCallResult<SteamPeerLobbyClass, SteamUGCQueryCompleted_t> at 0x6ca10 shows SteamPeerLobbyClass also handles Workshop (UGC) query results, likely for mod compatibility checking in the lobby.
- **steam_lobby_dialog_class**:
  - **class**: SteamLobbyDialogClass
  - **singleton**: True
  - **description**: UI dialog for the Steam lobby browser/creation screen.
- **steam_class**:
  - **class**: SteamClass
  - **constructor_rva**: 0x6a000
  - **singleton**: True
  - **description**: Top-level Steam integration singleton. Wraps initialization/shutdown.
- **internet_player_struct**:
  - **class**: InternetPlayerStruct
  - **description**: Represents a player in an online (Internet/Steam) game. Stored in DynamicVectorClass<InternetPlayerStruct> collections (multiple instances at 0x17260, 0x332c30 etc.). Contains a ClanStruct sub-struct for Steam clan/group information.
  - **inner_types**: InternetPlayerStruct::ClanStruct


### Event System

- **summary**: The command synchronization protocol is built on an event system. All player actions are encoded as Event objects with a numeric type ID, serialized via BitStreamClass into PacketClass packets, broadcast to all peers, and executed deterministically at a scheduled frame. The EventFactoryClass pattern uses template parameters <EventClass, TypeID> for registration.
- **class_hierarchy**:
  - **EventClass**:
    - **constructor_rva**: 0x5126c0
    - **description**: Base event class. Has a type ID at offset 0x8 (set in each subclass constructor), a linked-list entry for queue management, and a frame counter from a global source.
    - **key_fields**: {'offset': '0x00', 'name': 'linked_list_entry', 'type': 'LinkedListEntryClass<EventClass>'}, {'offset': '0x08', 'name': 'event_type_id', 'type': 'int32', 'description': 'Numeric event type. Set in each subclass constructor. Matches the factory template parameter.'}
  - **ScheduledEventClass**:
    - **constructor_rva**: 0x512c50
    - **inherits**: EventClass
    - **description**: Event scheduled for execution at a specific future frame. Most gameplay events inherit from this, not raw EventClass. Contains a scheduled frame number at offset 0x0 (initialized to 0xFFFFFFFF = unscheduled).
    - **key_fields**: {'offset': '0x00', 'name': 'scheduled_frame', 'type': 'int32', 'description': "Frame number at which this event should execute. 0xFFFFFFFF = not yet scheduled. 0x80000000 bit at offset 0x1c may be a 'processed' flag."}
  - **EventQueueClass**:
    - **constructor_rva**: 0x4b39e0
    - **description**: Queue that holds events awaiting execution. Allocates a slot array sized from a global (DAT_140a16fb0 = likely max_players or max_events_per_frame). Events are dequeued and executed when their scheduled frame arrives.
  - **BaseEventFactoryClass**:
    - **constructor_rva**: 0x5960f0
    - **description**: Base factory that registers event types. Constructor adds itself to a global DynamicVectorClass<BaseEventFactoryClass*> at DAT_140b36bc1, building the event type registry at static initialization time.
- **event_catalog**:
  - **_note**: Complete catalog of all 50+ event types. The type_id matches the EventFactoryClass template second parameter AND the value written to EventClass.offset_0x8 in each constructor. Events marked 'scheduled' inherit from ScheduledEventClass; 'immediate' inherit directly from EventClass.
  - **control_events**: {'id': 0, 'hex': '0x00', 'class': 'FrameInfoEventClass', 'rva': '0x4c1b00', 'scheduled': False, 'category': 'sync', 'description': 'Per-frame metadata exchange. Immediate (not scheduled). Lowest ID = processed first. Likely carries frame number and timing data.'}, {'id': 17, 'hex': '0x11', 'class': 'FrameSyncEventClass', 'rva': '0x4c1d30', 'scheduled': True, 'category': 'sync', 'description': 'Lockstep frame synchronization barrier. All peers must receive this before advancing to the next simulation frame.'}, {'id': 18, 'hex': '0x12', 'class': 'PerformanceMetricsEventClass', 'rva': '0x4c1eb0', 'scheduled': False, 'category': 'sync', 'description': 'Performance/checksum data exchange. Immediate (not scheduled). Likely carries frame timing metrics and state checksums for desync detection.'}, {'id': 15, 'hex': '0x0F', 'class': 'QuitGameEventClass', 'rva': '0x4993f0', 'scheduled': True, 'category': 'session', 'description': 'Player quit/disconnect notification.'}, {'id': 16, 'hex': '0x10', 'class': 'ChatEventClass', 'rva': '0x44d1d0', 'scheduled': False, 'category': 'social', 'description': 'In-game chat message. Immediate (not frame-synced). Has string buffer fields.'}, {'id': 39, 'hex': '0x27', 'class': 'GameOptionsEventClass', 'rva': '0x44d530', 'scheduled': True, 'category': 'session', 'description': 'Game options/settings change (speed, difficulty, etc.).'}, {'id': 51, 'hex': '0x33', 'class': 'ResumeGameEventClass', 'rva': '0x4adfd0', 'scheduled': True, 'category': 'session', 'description': 'Resume game from pause.'}, {'id': 36, 'hex': '0x24', 'class': 'SaveGameEventClass', 'rva': '0x48fa80', 'scheduled': True, 'category': 'session', 'description': 'Trigger synchronized save. Has string field (save name).'}, {'id': 11, 'hex': '0x0B', 'class': 'DebugEventClass', 'rva': '0x4ad0f0', 'scheduled': True, 'category': 'debug', 'description': 'Debug/diagnostic command. Has string buffer.'}, {'id': 50, 'hex': '0x32', 'class': 'TauntEventClass', 'rva': '0x4d6a60', 'scheduled': True, 'category': 'social', 'description': 'Player taunt/emote in multiplayer.'}
  - **movement_events**: {'id': 1, 'hex': '0x01', 'class': 'MoveToPositionEventClass', 'rva': '0x3acee0', 'scheduled': True, 'category': 'movement', 'description': 'Move units to a map position.'}, {'id': 2, 'hex': '0x02', 'class': 'MoveToObjectEventClass', 'rva': '0x3aee50', 'scheduled': True, 'category': 'movement', 'description': 'Move units to a target object.'}, {'id': 3, 'hex': '0x03', 'class': 'MoveObjectToObjectEventClass', 'rva': '0x3af290', 'scheduled': True, 'category': 'movement', 'description': 'Move an object to another object (e.g., docking).'}, {'id': 4, 'hex': '0x04', 'class': 'LookEventClass', 'rva': '0x689670', 'scheduled': True, 'category': 'movement', 'description': 'Camera look command.'}, {'id': 10, 'hex': '0x0A', 'class': 'MoveThroughObjectsEventClass', 'rva': '0x3c41c0', 'scheduled': True, 'category': 'movement', 'description': 'Move through/past objects (waypoint movement).'}, {'id': 14, 'hex': '0x0E', 'class': 'MoveToRayEventClass', 'rva': '0x3ae4a0', 'scheduled': True, 'category': 'movement', 'description': 'Move to a ray-cast position (terrain click).'}, {'id': 19, 'hex': '0x13', 'class': 'MoveToRayFacingEventClass', 'rva': '0x3ae950', 'scheduled': True, 'category': 'movement', 'description': 'Move to ray position with facing direction.'}, {'id': 20, 'hex': '0x14', 'class': 'FacingEventClass', 'rva': '0x409b40', 'scheduled': True, 'category': 'movement', 'description': 'Change unit facing direction. Has a 0x200-byte unit list.'}, {'id': 29, 'hex': '0x1D', 'class': 'StopMovementEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'movement', 'description': 'Halt unit movement.'}, {'id': 47, 'hex': '0x2F', 'class': 'MoveToPositionFacingEventClass', 'rva': '0x3add90', 'scheduled': True, 'category': 'movement', 'description': 'Move to position with facing.'}, {'id': 55, 'hex': '0x37', 'class': 'MoveToGarrisonEventClass', 'rva': '0x3ad3b0', 'scheduled': True, 'category': 'movement', 'description': 'Move units into a garrison structure.'}
  - **combat_events**: {'id': 6, 'hex': '0x06', 'class': 'AttackEventClass', 'rva': '0x3af4c0', 'scheduled': True, 'category': 'combat', 'description': 'Attack command. Has a 0x208-byte payload (likely unit selection list + target ID).'}, {'id': 13, 'hex': '0x0D', 'class': 'SpecialAbilityEventClass', 'rva': '0x4b4210', 'scheduled': True, 'category': 'combat', 'description': 'Galactic-level special ability activation. Large struct (0x108+ bytes) with object type references, coordinates (0x3fffff sentinel for unset).'}, {'id': 25, 'hex': '0x19', 'class': 'SpecialWeaponFireEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'combat', 'description': 'Fire special weapon.'}, {'id': 28, 'hex': '0x1C', 'class': 'BombingRunEventClass', 'rva': '0x524560', 'scheduled': True, 'category': 'combat', 'description': 'Call in a bombing run.'}, {'id': 40, 'hex': '0x28', 'class': 'TacticalSpecialAbilityEventClass', 'rva': '0x4292e0', 'scheduled': True, 'category': 'combat', 'description': 'Tactical battle special ability. 0x20C+ byte struct with coordinate sentinels.'}, {'id': 54, 'hex': '0x36', 'class': 'PlanetaryBombardEventClass', 'rva': '0x52afc0', 'scheduled': True, 'category': 'combat', 'description': 'Initiate orbital bombardment.'}, {'id': 57, 'hex': '0x39', 'class': 'TacticalSpecialAbilityWithDummyTargetEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'combat', 'description': 'Special ability with a dummy/placeholder target.'}, {'id': 38, 'hex': '0x26', 'class': 'TacticalSuperWeaponEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'combat', 'description': 'Activate a super weapon in tactical mode.'}
  - **production_economy_events**: {'id': 7, 'hex': '0x07', 'class': 'ProductionEventClass', 'rva': '0x523f50', 'scheduled': True, 'category': 'economy', 'description': 'Queue/cancel unit or building production.'}, {'id': 8, 'hex': '0x08', 'class': 'FleetManagementEventClass', 'rva': '0x5af020', 'scheduled': True, 'category': 'economy', 'description': 'Fleet assembly/management. Has 4 object ID fields (0x3fffff sentinel) and a signed offset at 0x14.'}, {'id': 9, 'hex': '0x09', 'class': 'InvadeEventClass', 'rva': '0x48fce0', 'scheduled': True, 'category': 'galactic', 'description': 'Invade a planet. 0x200+ byte selection list with planet sentinel.'}, {'id': 12, 'hex': '0x0C', 'class': 'ReinforceEventClass', 'rva': '0x403ab0', 'scheduled': True, 'category': 'economy', 'description': 'Bring in reinforcements. Uses 0x3fffff sentinel for target location.'}, {'id': 33, 'hex': '0x21', 'class': 'TacticalBuildEventClass', 'rva': '0x5d7740', 'scheduled': True, 'category': 'economy', 'description': 'Build a structure in tactical mode.'}, {'id': 34, 'hex': '0x22', 'class': 'TacticalSellEventClass', 'rva': '0x689810', 'scheduled': True, 'category': 'economy', 'description': 'Sell a tactical structure.'}, {'id': 42, 'hex': '0x2A', 'class': 'DistributeMoneyEventClass', 'rva': '0x498ca0', 'scheduled': True, 'category': 'economy', 'description': 'Transfer credits to an ally.'}, {'id': 46, 'hex': '0x2E', 'class': 'GalacticSellEventClass', 'rva': '0x532690', 'scheduled': True, 'category': 'economy', 'description': 'Sell unit/structure at galactic level. 0x3fffff object sentinel.'}, {'id': 45, 'hex': '0x2D', 'class': 'RepairHardpointEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'economy', 'description': 'Repair a hardpoint on a space station or capital ship.'}
  - **selection_ui_events**: {'id': 5, 'hex': '0x05', 'class': 'SelectEventClass', 'rva': '0x3ac9d0', 'scheduled': True, 'category': 'selection', 'description': 'Unit selection. Has a 0x1FC-byte selection buffer.'}, {'id': 30, 'hex': '0x1E', 'class': 'ControlGroupEventClass', 'rva': '0x436060', 'scheduled': True, 'category': 'selection', 'description': 'Assign/recall control group (Ctrl+1..9).'}, {'id': 37, 'hex': '0x25', 'class': 'SelectAllEventClass', 'rva': '0x437f40', 'scheduled': True, 'category': 'selection', 'description': 'Select all units.'}
  - **galactic_events**: {'id': 21, 'hex': '0x15', 'class': 'EscortEventClass', 'rva': '0x3b0010', 'scheduled': True, 'category': 'galactic', 'description': 'Escort fleet action.'}, {'id': 22, 'hex': '0x16', 'class': 'RetreatEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'galactic', 'description': 'Retreat from battle.'}, {'id': 26, 'hex': '0x1A', 'class': 'CinematicAnimationEventClass', 'rva': '0x6899c0', 'scheduled': True, 'category': 'galactic', 'description': 'Trigger cinematic animation.'}, {'id': 35, 'hex': '0x23', 'class': 'AllyEventClass', 'rva': '0x689ad0', 'scheduled': True, 'category': 'diplomacy', 'description': 'Alliance/diplomacy action. Has player ID field (init -1).'}, {'id': 49, 'hex': '0x31', 'class': 'WithdrawlEventClass', 'rva': '0x4aded0', 'scheduled': True, 'category': 'galactic', 'description': "Withdraw from engagement (note: engine typo 'Withdrawl')."}, {'id': 52, 'hex': '0x34', 'class': 'GarrisonEventClass', 'rva': '0x440ad0', 'scheduled': True, 'category': 'galactic', 'description': 'Garrison troops in a structure.'}, {'id': 48, 'hex': '0x30', 'class': 'PlaceBeaconEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'galactic', 'description': 'Place a marker/beacon on the map.'}, {'id': 53, 'hex': '0x35', 'class': 'SetMarkerIDEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'galactic', 'description': 'Set marker identifier.'}, {'id': 56, 'hex': '0x38', 'class': 'SetGUIIndexEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'ui', 'description': 'Set GUI tab index.'}, {'id': 58, 'hex': '0x3A', 'class': 'SetAbilityAutofireEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'galactic', 'description': 'Toggle ability autofire mode.'}, {'id': 43, 'hex': '0x2B', 'class': 'SetUnitAbilityModeEventClass', 'rva': 'n/a', 'scheduled': True, 'category': 'galactic', 'description': 'Set unit ability mode (stance/behavior).'}
  - **setup_phase_events**: {'id': 31, 'hex': '0x1F', 'class': 'SetupPhaseMoveEventClass', 'rva': '0x5aea90', 'scheduled': True, 'category': 'setup', 'description': 'Pre-battle setup phase unit placement.'}, {'id': 32, 'hex': '0x20', 'class': 'SetupPhaseTriggerEndEventClass', 'rva': '0x5aef30', 'scheduled': True, 'category': 'setup', 'description': 'Signal end of setup phase (player ready).'}


### Synchronization Mechanism

- **summary**: Lockstep with deterministic execution. All peers process the same events at the same frame. Three control events (IDs 0, 17, 18) form the sync protocol.
- **frame_sync_protocol**:
  - **description**: Each simulation frame follows this sequence: (1) Exchange FrameInfoEventClass (ID 0) with frame metadata; (2) Wait for FrameSyncEventClass (ID 17) from all peers — this is the lockstep barrier; (3) Execute all ScheduledEvents whose scheduled_frame matches the current frame; (4) Exchange PerformanceMetricsEventClass (ID 18) with timing/checksum data.
  - **lockstep_barrier**: FrameSyncEventClass (ID 17). The simulation cannot advance until all peers have confirmed their events for this frame. This is the classic lockstep 'ready' signal.
  - **delayed_event_queue**: GameModeClass maintains a DynamicVectorClass<DelayedEventStruct*> — events that arrived but whose scheduled_frame has not yet been reached. They are held in this queue and executed when the frame counter reaches their scheduled_frame value.
- **determinism_requirements**:
  - **description**: For lockstep to work, all peers must produce identical simulation results from identical inputs. This means the engine must be fully deterministic: same floating-point math, same RNG sequences, same execution order.
  - **implications_for_modding**: Any memory modification that changes gameplay state (HP, damage, position, unit counts, production queues) without going through the event system will cause a desync in multiplayer., Single-player mods that directly write to GameObjectClass fields (e.g., HP at offset 0x5C) are safe in SP but will immediately desync in MP., Visual-only modifications (camera position, UI elements, selection highlights) do NOT cause desyncs because they are not part of the deterministic simulation.
- **sentinel_values**:
  - **0x3FFFFF**: Unset/null object reference sentinel. Used extensively in event constructors (FleetManagement, Reinforce, Invade, SpecialAbility, etc.) to indicate 'no target selected'. This is likely an index into a 22-bit object ID space.
  - **0xFFFFFFFF**: Unscheduled frame (ScheduledEventClass.scheduled_frame default) or invalid/null reference.
  - **0xFFFFFFFE**: Special sentinel in FleetManagementEventClass offset 0x14 — possibly 'auto-assign' or 'any available'.


### Desync Detection

- **summary**: Desync detection is inferred from PerformanceMetricsEventClass (ID 18) and the deterministic lockstep architecture. The engine likely checksums key simulation state each frame and compares across peers.
- **known_mechanisms**:
  - **performance_metrics_event**:
    - **event_id**: 18
    - **class**: PerformanceMetricsEventClass
    - **rva**: 0x4c1eb0
    - **description**: Non-scheduled event (inherits EventClass directly) exchanged every frame. Despite the name 'PerformanceMetrics', in lockstep RTS engines this class typically carries: (1) frame timing data (for adaptive game speed), (2) state checksums (for desync detection). The fact that it is exchanged every frame and is NOT a ScheduledEvent confirms it is sync infrastructure, not gameplay.
  - **frame_info_event**:
    - **event_id**: 0
    - **class**: FrameInfoEventClass
    - **rva**: 0x4c1b00
    - **description**: Frame metadata. Likely carries the current frame number and possibly a hash/checksum of the game state.
- **what_would_be_checksummed**:
  - **description**: Based on the event system and Alamo engine structure, the following simulation state is most likely included in per-frame checksums:
  - **high_confidence**: Global game frame counter, RNG state (random number generator seed/state), Total unit count per player, Total credits/income per player, Production queue state
  - **medium_confidence**: Unit positions (at least checksum of all positions), Unit HP values, Unit ownership (player IDs), Active ability timers, Planet control state (galactic mode)
  - **low_confidence_but_possible**: Individual unit state checksums, AI decision state, Pathfinding state
- **what_is_safe_to_modify_in_multiplayer**:
  - **safe**: Camera position and zoom (not synchronized), UI state (selection highlights, tooltips, HUD layout), Sound/music settings, Visual effects (particle counts, texture quality), Fog of war reveal (local rendering only — but may still be detected by FoW state checksumming), ChatEventClass content (ID 16 — non-scheduled, social only)
  - **unsafe_will_desync**: GameObjectClass.hp (offset 0x5C), GameObjectClass.owner_player_id (offset 0x58), GameObjectClass.object_id (offset 0x50), Any production queue modification, Any unit spawn/destroy outside the event system, Any credit/resource value change, Any RNG state modification, Any movement/position override that bypasses ScheduledEventClass


### Game Mode System

- **summary**: GameModeClass is the battle/map controller. LandModeClass extends it for ground battles. Both manage event processing and the delayed event queue.
- **game_mode_class**:
  - **class**: GameModeClass
  - **constructor_rva**: 0x35a5e0
  - **description**: Large struct (~0x60 * 8 bytes). Manages the tactical/galactic game state, event queue (DelayedEventStruct vector), objectives (ObjectiveStruct vector), game speed (float at 0x10+4 = 0x3F800000 = 1.0f default), and player-specific state arrays.
  - **key_fields_observed**: Offset 0x10+4: game_speed float (default 1.0 = 0x3F800000), Offset 0x28*8: DynamicVectorClass<DelayedEventStruct*> — delayed event queue, Offset 0x2B*8: DynamicVectorClass<ObjectiveStruct>, Offset 0x37*8: DynamicVectorClass<int> — possibly player scores or frame counters, Offset 0x3B*8: DynamicVectorClass<DynamicVectorClass<GameObjectClass*>> — per-player object lists
- **land_mode_class**:
  - **class**: LandModeClass
  - **constructor_rva**: 0x3b5210
  - **inherits**: GameModeClass
  - **description**: Ground battle mode. Extends GameModeClass with additional linked lists for ground-specific state and a HeroClashManagerClass at offset 0x3F0.


### Key Addresses

- **_note**: All addresses are absolute (image base 0x140000000). Subtract image base for RVA.
- **event_factory_registry**: DAT_140b36bc1 — global DynamicVectorClass<BaseEventFactoryClass*> where all event factories register at static init
- **max_event_slots**: DAT_140a16fb0 — global int used to size the EventQueueClass slot array and GameModeClass player arrays
- **frame_counter_source**: FUN_140294a70 with DAT_140a16fd0 — called in EventClass constructor to get current frame number
- **steam_peer_lobby_singleton**: SingletonInstance<SteamPeerLobbyClass> at static init 0x7e6450
- **steam_lobby_dialog_singleton**: SingletonInstance<SteamLobbyDialogClass> at static init 0x7e6590
- **steam_class_singleton**: SingletonInstance<SteamClass> at static init 0x7e6580


### Open Questions

- Exact checksum algorithm used for desync detection (could not locate a CRC/hash function directly referenced by PerformanceMetricsEventClass — needs runtime tracing)
- Exact format of PerformanceMetricsEventClass payload (needs decompilation of its virtual Pack/Unpack methods, which are in the vtable)
- Whether the engine supports 'late join' or only 'lobby start' (likely lobby-only given pure lockstep)
- Maximum player count (the sentinel 0x3FFFFF = 22 bits suggests up to ~4M object IDs, but player count is likely 2-8 based on the 'max_event_slots' global)
- Whether DebugEventClass (ID 11) is stripped in release builds or can be used to inject commands
- The exact PacketTypeClass::tPacketMagicStruct format — likely a 4-byte magic + version for protocol handshake
- Whether the game uses NAT punchthrough (likely via Steam relay) or requires port forwarding for Winsock mode

