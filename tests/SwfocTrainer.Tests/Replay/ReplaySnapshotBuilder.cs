using System.Buffers.Binary;
using System.Text;
using SwfocTrainer.Saves.Checksum;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Fluent builder that emits synthetic <c>.swfocsnap</c> files matching the
/// byte layout in <c>swfoc_lua_bridge/SNAPSHOT_FORMAT.md</c>. Used by Phase 9
/// replay tests to spin up a deterministic <c>swfoc_replay.exe</c> instance
/// without needing a captured game session.
/// </summary>
/// <remarks>
/// Mirrors <c>swfoc_lua_bridge/make_test_snapshot.py</c>: little-endian, fixed
/// 16-byte magic, 68-byte header, sections in ascending ID order, end marker
/// with the same CRC32 variant the live writer uses (zlib polynomial
/// 0xEDB88320, init/xor 0xFFFFFFFF). Reuses the existing
/// <see cref="Crc32"/> helper from <c>SwfocTrainer.Saves</c> so the test
/// fixture and the production save layer share a single CRC implementation.
/// </remarks>
public sealed class ReplaySnapshotBuilder
{
    private readonly List<PlayerEntry> _players = new();
    private readonly Dictionary<string, uint> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal)
    {
        // SNAPSHOT_FORMAT v1 requires these four metadata keys; default them so
        // tests don't have to remember.
        ["capture_method"] = "replay_test_builder",
        ["mod_name"] = "test_fixture",
        ["mod_version"] = "0.0.0",
        ["swfoc_bridge_version"] = "1.0",
    };

    private byte _gameMode = 1; // galactic by default
    private ulong _captureTimestampMs = 0x1234567890ABCDEFUL;
    private int? _localPlayerSlot;

    private ReplaySnapshotBuilder() { }

    public static ReplaySnapshotBuilder Create() => new();

    /// <summary>Adds a player slot to the snapshot.</summary>
    public ReplaySnapshotBuilder WithPlayer(string faction, double credits, int techLevel)
    {
        ArgumentNullException.ThrowIfNull(faction);
        var slot = (uint)_players.Count;
        _players.Add(new PlayerEntry(slot, faction, credits, techLevel, string.Empty));
        return this;
    }

    /// <summary>
    /// Marks which player slot should be considered the local player. The
    /// replay harness places this slot at index 0 of <c>player_array</c> so
    /// <c>SWFOC_GetCredits</c> and <c>SWFOC_GetLocalPlayer</c> resolve to it.
    /// </summary>
    public ReplaySnapshotBuilder WithLocalPlayerSlot(int slot)
    {
        if (slot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
        _localPlayerSlot = slot;
        return this;
    }

    /// <summary>Adds (or replaces) a single object-type entry.</summary>
    public ReplaySnapshotBuilder WithObject(string typeName, uint count)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        _objects[typeName] = count;
        return this;
    }

    /// <summary>Convenience overload for the common case where the test
    /// supplies an integer count. Equivalent to <see cref="WithObject(string, uint)"/>.</summary>
    public ReplaySnapshotBuilder WithObjects(string typeName, int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        return WithObject(typeName, (uint)count);
    }

    /// <summary>Adds (or replaces) a metadata key/value pair.</summary>
    public ReplaySnapshotBuilder WithMetadata(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _metadata[key] = value;
        return this;
    }

    /// <summary>Sets the snapshot's <c>game_mode</c> byte (0=unknown, 1=galactic,
    /// 2=tactical_space, 3=tactical_land, 4=menu).</summary>
    public ReplaySnapshotBuilder WithGameMode(byte mode)
    {
        _gameMode = mode;
        return this;
    }

    /// <summary>Overrides the default <c>capture_timestamp_ms</c> value.</summary>
    public ReplaySnapshotBuilder WithCaptureTimestamp(ulong epochMs)
    {
        _captureTimestampMs = epochMs;
        return this;
    }

    /// <summary>
    /// Encodes the snapshot to disk and returns the absolute path. The output
    /// file is created with a unique GUID-based name inside
    /// <paramref name="outputDir"/> so concurrent builders cannot collide.
    /// </summary>
    /// <remarks>
    /// <paramref name="outputDir"/> must be a non-empty, fully-qualified path
    /// to a directory the test process owns (typically the xUnit
    /// <see cref="Path.GetTempPath"/> tree). The method canonicalizes the
    /// directory via <see cref="Path.GetFullPath(string)"/> and constructs the
    /// final filename from a server-side GUID — there is no user-supplied path
    /// component, so there is no path-traversal surface. The explicit
    /// validation below documents that contract for static analysis (Semgrep
    /// CWE-22) and for any future caller.
    /// </remarks>
    public string Build(string outputDir)
    {
        ArgumentNullException.ThrowIfNull(outputDir);
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            throw new ArgumentException("outputDir must not be empty or whitespace.", nameof(outputDir));
        }

        // Canonicalize the destination so any '..' segments are resolved before
        // the directory is created. This is defensive: the only call site is
        // ReplayHarnessFixture, which passes a process-local temp path, but the
        // canonicalization keeps the contract robust.
        var canonicalDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(canonicalDir);

        var bytes = EncodeBytes();
        // Filename is composed entirely from a server-generated GUID — there
        // is no user-controlled path component, so Path.GetFileName-style
        // sanitization is unnecessary.
        var fileName = $"replay_fixture_{Guid.NewGuid():N}.swfocsnap";
        var path = Path.Combine(canonicalDir, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Returns the in-memory byte buffer for the snapshot without touching the
    /// filesystem. Useful for round-trip unit tests of the builder itself.
    /// </summary>
    internal byte[] EncodeBytes()
    {
        // Reorder _players so the requested local player ends up at index 0.
        // The replay harness uses player_array[0] as the local player.
        var orderedPlayers = OrderPlayersForLocalSlot();

        using var ms = new MemoryStream();

        // ---- Header (68 bytes) ----
        // Magic: 11 ASCII chars "SWFOCSNAPv1" + 5 null bytes = 16 bytes total.
        var magic = new byte[16];
        Encoding.ASCII.GetBytes("SWFOCSNAPv1", 0, 11, magic, 0);
        ms.Write(magic, 0, 16);

        // format_version (uint32 LE)
        WriteUInt32(ms, 1);

        // capture_timestamp_ms (uint64 LE)
        WriteUInt64(ms, _captureTimestampMs);

        // engine_build_hash (32 zero bytes)
        ms.Write(new byte[32], 0, 32);

        // game_mode (uint8) + 7 reserved bytes
        ms.WriteByte(_gameMode);
        ms.Write(new byte[7], 0, 7);

        if (ms.Length != 68)
        {
            throw new InvalidOperationException(
                $"Header size mismatch: wrote {ms.Length} bytes, expected 68.");
        }

        // ---- Section 1: player_array ----
        WriteSection(ms, sectionId: 1, payload: BuildPlayerArrayPayload(orderedPlayers));

        // ---- Section 2: lua_state_registry (always emit, may be empty) ----
        WriteSection(ms, sectionId: 2, payload: BuildLuaStateRegistryPayload());

        // ---- Section 3: object_catalog ----
        WriteSection(ms, sectionId: 3, payload: BuildObjectCatalogPayload());

        // ---- Section 4: global_registry (empty by default; replay does not require it) ----
        WriteSection(ms, sectionId: 4, payload: BuildEmptyGlobalRegistryPayload());

        // ---- Section 5: metadata ----
        WriteSection(ms, sectionId: 5, payload: BuildMetadataPayload());

        // ---- End marker (12 bytes total: id + len + crc32) ----
        // The end marker header (8 bytes: section_id 0xFFFFFFFF + length 4) is
        // included in the CRC; the 4-byte CRC field itself is not.
        WriteUInt32(ms, 0xFFFFFFFFu);
        WriteUInt32(ms, 4u);

        var bodyForCrc = ms.ToArray();
        var crc = Crc32.Compute(bodyForCrc);
        WriteUInt32(ms, crc);

        return ms.ToArray();
    }

    private List<PlayerEntry> OrderPlayersForLocalSlot()
    {
        var ordered = new List<PlayerEntry>(_players);
        if (!_localPlayerSlot.HasValue || ordered.Count <= 1)
        {
            return ordered;
        }

        var idx = ordered.FindIndex(p => p.Slot == (uint)_localPlayerSlot.Value);
        if (idx <= 0)
        {
            return ordered;
        }

        var local = ordered[idx];
        ordered.RemoveAt(idx);
        ordered.Insert(0, local);
        return ordered;
    }

    private static byte[] BuildPlayerArrayPayload(IReadOnlyList<PlayerEntry> players)
    {
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)players.Count);
        foreach (var p in players)
        {
            WriteUInt32(ms, p.Slot);
            WriteFixedString(ms, p.Faction, 64);
            WriteDouble(ms, p.Credits);
            WriteInt32(ms, p.TechLevel);
            WriteFixedString(ms, p.Name, 64);
        }
        return ms.ToArray();
    }

    private static byte[] BuildLuaStateRegistryPayload()
    {
        // We do not simulate Lua state pointers in the test fixture.
        // section_length = 4 (just the 0 count uint32).
        using var ms = new MemoryStream();
        WriteUInt32(ms, 0u);
        return ms.ToArray();
    }

    private byte[] BuildObjectCatalogPayload()
    {
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)_objects.Count);
        foreach (var (name, count) in _objects)
        {
            WriteFixedString(ms, name, 64);
            WriteUInt32(ms, count);
        }
        return ms.ToArray();
    }

    private static byte[] BuildEmptyGlobalRegistryPayload()
    {
        using var ms = new MemoryStream();
        WriteUInt32(ms, 0u);
        return ms.ToArray();
    }

    private byte[] BuildMetadataPayload()
    {
        using var ms = new MemoryStream();
        WriteUInt32(ms, (uint)_metadata.Count);
        foreach (var (key, value) in _metadata)
        {
            var kb = Encoding.ASCII.GetBytes(key);
            var vb = Encoding.ASCII.GetBytes(value);
            WriteUInt16(ms, (ushort)kb.Length);
            ms.Write(kb, 0, kb.Length);
            WriteUInt16(ms, (ushort)vb.Length);
            ms.Write(vb, 0, vb.Length);
        }
        return ms.ToArray();
    }

    private static void WriteSection(Stream s, uint sectionId, byte[] payload)
    {
        WriteUInt32(s, sectionId);
        WriteUInt32(s, (uint)payload.Length);
        s.Write(payload, 0, payload.Length);
    }

    private static void WriteUInt16(Stream s, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        s.Write(buf);
    }

    private static void WriteUInt32(Stream s, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        s.Write(buf);
    }

    private static void WriteInt32(Stream s, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        s.Write(buf);
    }

    private static void WriteUInt64(Stream s, ulong value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        s.Write(buf);
    }

    private static void WriteDouble(Stream s, double value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buf, value);
        s.Write(buf);
    }

    private static void WriteFixedString(Stream s, string value, int width)
    {
        var buffer = new byte[width];
        var raw = Encoding.ASCII.GetBytes(value ?? string.Empty);
        var copyLen = Math.Min(raw.Length, width);
        Array.Copy(raw, 0, buffer, 0, copyLen);
        // Remaining bytes are already zero from the new[] allocation -> null padded.
        s.Write(buffer, 0, width);
    }

    private readonly record struct PlayerEntry(
        uint Slot,
        string Faction,
        double Credits,
        int TechLevel,
        string Name);
}
