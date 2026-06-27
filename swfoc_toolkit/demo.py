"""
Demo script for the SWFOC Toolkit.

Connects to a running Forces of Corruption game via the CE Bridge and:
  1. Lists all units with their type, owner, and HP
  2. Shows the faction map (player slot -> faction name)
  3. Enables god mode for the human player
  4. Pauses, then disables god mode

Prerequisites:
  - Star Wars: Empire at War - Forces of Corruption is running
  - Cheat Engine is attached with the MCP Bridge script loaded
  - The CE Bridge named pipe is active (CE_MCP_Bridge_v99)
  - pywin32 is installed (pip install pywin32)

Usage:
  python -m swfoc_toolkit.demo
"""

import sys
import time


def main():
    # Imports are inside main() so import errors surface clearly
    from swfoc_toolkit.engine import CEBridge
    from swfoc_toolkit.players import PlayerList
    from swfoc_toolkit.units import UnitEnumerator
    from swfoc_toolkit.mods.god_mode import GodModeMod

    print("=" * 72)
    print("  SWFOC Toolkit Demo")
    print("=" * 72)

    # ------------------------------------------------------------------
    # Step 0: Connect to the game
    # ------------------------------------------------------------------
    print("\n[*] Connecting to CE Bridge...")
    bridge = CEBridge()
    try:
        bridge.connect()
    except ConnectionError as exc:
        print(f"[!] Connection failed: {exc}")
        print("    Make sure Cheat Engine is running with the MCP Bridge script.")
        sys.exit(1)

    info = bridge.get_process_info()
    print(f"    Connected to process: {info}")
    print(f"    Module base: 0x{bridge.get_module_base():X}")

    # ------------------------------------------------------------------
    # Step 1: Enumerate all units
    # ------------------------------------------------------------------
    print("\n[*] Enumerating game objects...")
    enumerator = UnitEnumerator(bridge)

    try:
        objects = enumerator.enumerate_all(max_objects=500)
    except Exception as exc:
        print(f"[!] Enumeration failed: {exc}")
        print("    Is a game session active (not in menus)?")
        bridge.close()
        sys.exit(1)

    if not objects:
        print("    No GameObjectClass instances found.")
        print("    Make sure you are in an active battle or galactic map.")
        bridge.close()
        sys.exit(1)

    # Show top-level objects only (skip hardpoint sub-objects)
    top_level = [o for o in objects if not o.is_subobject]
    sub_objects = [o for o in objects if o.is_subobject]

    print(f"    Found {len(objects)} total objects "
          f"({len(top_level)} top-level, {len(sub_objects)} sub-objects)")

    enumerator.print_summary(top_level[:50])  # Cap output for readability

    if len(top_level) > 50:
        print(f"    ... and {len(top_level) - 50} more top-level objects")

    # ------------------------------------------------------------------
    # Step 2: Show the faction map
    # ------------------------------------------------------------------
    print("\n[*] Reading faction map...")
    player_list = PlayerList(bridge)

    try:
        n_players = player_list.count
        print(f"    Player slots: {n_players}")

        faction_map = player_list.get_faction_map()
        print(f"\n    {'Slot':>4s}  {'Address':>18s}  Faction")
        print(f"    {'-'*4}  {'-'*18}  {'-'*30}")
        for slot, faction in faction_map.items():
            addr = player_list.get_player_address(slot)
            print(f"    {slot:4d}  0x{addr:016X}  {faction}")
    except Exception as exc:
        print(f"    [!] Player list read failed: {exc}")

    # Detect the human player
    print("\n[*] Detecting human player...")
    human = player_list.detect_human_player()
    if human:
        print(f"    Human player detected: slot {human.index} "
              f"(address 0x{human.address:X})")
    else:
        print("    Could not detect human player, defaulting to slot 1")

    human_id = human.index if human else 1

    # Show unit counts by owner
    owner_counts = {}
    for obj in top_level:
        oid = obj.owner_id
        owner_counts[oid] = owner_counts.get(oid, 0) + 1

    print(f"\n    Units per player:")
    for oid in sorted(owner_counts):
        marker = " <-- YOU" if oid == human_id else ""
        print(f"      Player {oid}: {owner_counts[oid]} units{marker}")

    # ------------------------------------------------------------------
    # Step 3: Enable god mode
    # ------------------------------------------------------------------
    print(f"\n[*] Enabling God Mode for player {human_id}...")
    god_mode = GodModeMod(bridge, owner_id=human_id)

    try:
        god_mode.enable()
        print(f"    {god_mode}")
        print("    Your units are now invulnerable to HP decreases.")
    except RuntimeError as exc:
        print(f"    [!] God Mode injection failed: {exc}")
        bridge.close()
        sys.exit(1)

    # ------------------------------------------------------------------
    # Step 4: Wait, then disable
    # ------------------------------------------------------------------
    wait_seconds = 10
    print(f"\n[*] God Mode active. Waiting {wait_seconds} seconds...")
    print(f"    (Your units cannot lose HP during this window)")

    for remaining in range(wait_seconds, 0, -1):
        print(f"    {remaining}...", end=" ", flush=True)
        time.sleep(1)
    print()

    print("\n[*] Disabling God Mode...")
    try:
        god_mode.disable()
        print(f"    {god_mode}")
        print("    Original game behavior restored.")
    except RuntimeError as exc:
        print(f"    [!] God Mode removal failed: {exc}")
        print("    You may need to restart the game to clear the hook.")

    # ------------------------------------------------------------------
    # Cleanup
    # ------------------------------------------------------------------
    bridge.close()
    print("\n[*] Done. Connection closed.")


if __name__ == "__main__":
    main()
