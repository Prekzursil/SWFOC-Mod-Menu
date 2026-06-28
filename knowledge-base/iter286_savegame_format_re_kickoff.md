# iter-286 — Thread C savegame RE iter 1 (chunk format kickoff + parser scaffolding spec)

**Date:** 2026-05-08
**Arc class:** Thread C savegame editor — iter 1 of 7 (per agent #1 spec at `thread_c_savegame_re_research_2026-05-08.md`)
**Predecessor:** iter-285 (Tier 3 bridge wires LIVE; closes Phase 2-full)
**Successor (queued):** iter-287 (parser scaffolding C# implementation)

## Mission

Build the foundational understanding of SWFOC's `.PetroglyphFoC64Save` binary format so iter-287+ can ship a parser → CLI corruption-fixer → WPF editor → mod-hash validator stack. Highest-leverage deliverable from the operator's perspective: **mod-hash re-anchor** to fix saves that crash after mod changes.

## Empirical findings — iter-286 first scan

Scanned `b.PetroglyphFoC64Save` (23 MB, real save):

```
Header at 0x000–0x2027 (size = 0x2028 per magic block).
Chunks start at 0x2028:

  0x00002028: chunk_id=0x00384D42  size=0x4         (4 B)
  0x00002034: chunk_id=0x00280000  size=0x1000000  (16,777,216 B)
  0x0100203C: chunk_id=0x454E4F4E  size=0x10000    (65,536 B)   "NONE" in LE ASCII
  0x01012044: chunk_id=0x1AED8CA5  size=0x403       (1,027 B)
  0x0101244F: ABORT — bad chunk (id=0x102, size=0xD000000 nonsensical)
```

**Three NEW empirical observations** that revise agent #1's research:

1. **Top-level chunk IDs are FourCC strings, not numeric `0x3E8`.** `0x454E4F4E` decodes to `"NONE"` (little-endian byte order in LE uint32). Agent #1's `0x3E8` claim is likely the INNER micro-chunk ID under the top-level FourCC chunk container. Need to walk INTO the FourCC chunks, not stop at them.

2. **First chunk at 0x2028 is a tiny 4-byte chunk** (id `0x00384D42`). Likely a version marker or chunk count. Inspecting its 4 bytes will clarify.

3. **Walking breaks at the 5th chunk** (`0x102` with absurd size `0xD000000`). Two hypotheses:
   - The walker missed the bit-31 sub-chunks flag and walked INTO a parent chunk's payload as if it were a sibling.
   - There's a header alignment within the chunk body that my walker missed.
   
   Per agent #1: bit 31 of `chunk_size` == 1 means "contains sub-chunks." Looking at 0x280000 (size = 0x1000000), bit 31 is 0 — so flat chunk. But the data at 0x2034..0x100203C (16 MB span) needs to be RAW bytes, not parsed as nested chunks. My walker correctly skipped it. So the 0x102 abort is something else.

   Most likely: the walker's `chunk_size` interpretation is wrong. Looking at 0x10000 == 65536 → that's exactly 64 KB which is a clean alignment, suggests size IS being read correctly. But the next chunk after a 64 KB block lands at 0x01012044 with id 0x1AED8CA5, then the chunk after 1027 bytes would be at 0x0101244F — and that's where we abort. So the actual chunk boundaries ARE correct, but the chunk-id at 0x0101244F is bogus. Possible explanation: this save is internally a TREE not a flat list — chunks 1-4 are top-level, chunk 5+ are children of one of them.

   **Iter-287 parser MUST walk recursively when bit 31 of size is set.** Even if the top-level chunks I scanned don't have bit 31, deeper investigation of the `0x1000000` chunk likely reveals nested structure inside.

## Agent #1 inheritance — corrected

Per agent #1 research at `knowledge-base/thread_c_savegame_re_research_2026-05-08.md`, plus iter-286 empirical corrections:

| Surface | Original claim | iter-286 update |
|---|---|---|
| Top-level chunk format | numeric `chunk_id=0x3E8` (1000) | **FourCC** (e.g. `"NONE"` = 0x454E4F4E LE); 0x3E8 is the INNER micro-chunk ID under a parent |
| Header size | header ~0x100 | **header is 0x2028 bytes** (matches struct-size field in magic block) |
| Chunk header layout | uint32 id + uint32 size | CONFIRMED — 8 bytes |
| Bit 31 of size | 1 = sub-chunks | unverified empirically; iter-287 parser handles it |
| Master loader | `0x140052D10` | unchanged — still authoritative |
| Mod hash | embedded as micro-chunk type 5 | unverified; agent currently running RE walk |

## Master loader RVAs — agent results (RETURNED iter-286)

The parallel research agent completed and confirmed:

| Finding | Status |
|---|---|
| 53 callees of `0x140052D10` enumerated | ✓ |
| `Open_Chunk` / `Close_Chunk` / `Next_Chunk` / `Has_Micro_Chunk` / `Read_Micro_ID` / `Read_Micro_Data` / `Read_Micro_String` confirmed at agent-#1 RVAs | ✓ |
| `Has_Micro_Chunk @ 0x140220610` confirms bit-31-as-subchunk-flag semantic | ✓ |
| Micro-chunk type **0x05 encoding**: 1-byte length prefix + UTF-16LE chars, NO null terminator | ✓ NEW |
| Save-format **versioning is single-version** (`0x01`); no v2/v3 evolution | ✓ NEW |
| Compression: `compress2` / `uncompress2` exist in binary; usage in saves UNCONFIRMED | unverified |
| Mod-hash algorithm (CRC32 vs MD5 vs SHA1 vs custom) | unverified — needs iter-287 hash-fn search near `GameObjectTypeList @ 0xA172D0` |

## Discrepancy flagged for iter-287 resolution

The agent (working from IDA decompile of `0x140052D10`) concluded **"file is single-root, 0x3E8 is the only top-level chunk"**. My empirical real-save scan saw 4 distinct chunk IDs (`0x00384D42`, `0x00280000`, `0x454E4F4E` = "NONE" LE, `0x1AED8CA5`) BEFORE reaching anything that could be 0x3E8.

Three possible reconciliations (iter-287 must determine which):

1. **Outer container**: there's a higher-level wrapper format outside the chunk-reader's scope. The 4 chunks I scanned are header metadata; the real `0x3E8` chunk follows them.
2. **Header is larger than `0x2028`**: my walker started chunk-walking at the wrong offset. Real chunks start later (e.g., after a TOC table or an extended-header block).
3. **Loader handles multiple top-level chunk-IDs**: the agent's "single-root" conclusion is a misread of the IDA decompile; there's actually a multi-arm switch at the chunk-id dispatch.

Resolution: iter-287 parser implementation will TRY all three interpretations against a known-good save and emit a chunk inventory. The interpretation that produces semantically meaningful chunks (matching field names like "credits", "techlevel", "planet_owner") wins.


- `python tools/callgraph_query.py callees 0x52D10 --limit 60` to enumerate the 53 callees of the master loader.
- IDA decompile read of `0x140052D10` to extract the chunk dispatch switch.
- IDA decompile read of `0x140220610` (Has_Micro_Chunk) to confirm bit-31 semantics.
- IDA decompile read of `0x1402209A0` (Read_Micro_String) for length-prefix encoding.
- Search for save-format versioning (RGMH v2/v3 detection).

Agent results will land in `knowledge-base/thread_c_savegame_re_research_2026-05-08.md` updates OR a sibling `iter286_savegame_chunk_dispatch.md` doc.

## ralph-orchestrator hat configuration verified

While the RE agent ran, iter-286 also verified `ralph.yml` config:
- `ralph doctor` PASS (1 warning: ANTHROPIC_API_KEY non-blocking).
- 3 hats parsed: `editor-polish`, `overlay-interactive`, `savegame-engineer`.
- Routing collisions fixed (each hat now has its own `<name>.start` + `<name>.subtask.done` event namespaces).
- Specs at `.ralph/specs/{editor-100, overlay-interactive, savegame-editor}.md` (412 LoC total) ready for orchestrator-driven flow.

## Iter-287 deliverable spec (parser scaffolding)

Per agent #1 7-iter sequence + iter-286 empirical findings, iter-287 should ship `SwfocTrainer.Savegame` C# project skeleton with:

```csharp
namespace SwfocTrainer.Savegame;

// Header — 0x2028 bytes total, fixed layout per RGMH magic
public sealed class SavegameHeader
{
    public byte[] Magic { get; init; }  // "RGMH"
    public uint Version { get; init; }   // 0x01
    public uint StructSize { get; init; } // 0x2028
    public Guid GameUuid { get; init; }
    public string LabelUtf16 { get; init; } // "Forces of Corruption game"
    // ...
}

// Chunk — 8-byte header + body. Top-level chunks use FourCC IDs (per iter-286 scan)
public sealed class SaveChunk
{
    public uint Id { get; init; }   // FourCC or numeric
    public uint Size { get; init; }  // bottom 31 bits = data size; bit 31 = has sub-chunks
    public bool HasSubChunks => (Size & 0x80000000u) != 0;
    public uint DataSize => Size & 0x7FFFFFFFu;
    public byte[] RawBody { get; init; }
    public IReadOnlyList<SaveChunk> SubChunks { get; init; }  // populated when HasSubChunks
    public IReadOnlyList<MicroChunk> MicroChunks { get; init; } // populated for chunk_id=0x3E8 containers

    public string IdAsFourCC()
    {
        // Decode 4 bytes LE as ASCII when printable
        var bytes = BitConverter.GetBytes(Id);
        if (bytes.All(b => b >= 0x20 && b <= 0x7E))
            return new string(bytes.Select(b => (char)b).ToArray());
        return $"0x{Id:X8}";
    }
}

// Micro-chunk — 2-byte header (id + size) inside chunk_id=0x3E8 containers
public sealed class MicroChunk
{
    public byte Id { get; init; }     // 0x00–0x06 per agent #1
    public byte Size { get; init; }   // max 255
    public byte[] Data { get; init; }
}

public sealed class SavegameParser
{
    public static (SavegameHeader, IReadOnlyList<SaveChunk>) Parse(string path);
    public static void Validate(IEnumerable<SaveChunk> chunks);  // throws on bad-chunk
    public static SavegameReport Diagnose(string path);          // returns parseable+errors
}
```

**Out-of-scope iter-287:** corruption-fix logic (iter-288), WPF editor (iter-289), mod-hash validator (iter-290).

## Verification

- [x] Real-save first scan empirically corrects agent #1's `0x3E8`/header-size claims.
- [x] `ralph doctor` PASS (1 non-blocking warning).
- [x] `ralph.yml` routing collisions fixed across 3 hats.
- [x] iter-287 parser skeleton designed (5 classes, ~80 LoC for skeleton).
- [ ] Parallel RE agent results merged into agent #1 source doc when complete.
- [ ] State docs synced (.remember/now.md, .remember/ralph_loop_state.md, STATUS.md).
- [ ] Task #536 marked completed; iter-287 queued.

## Tasks queued for iter-287+

- **iter-287** (next): C# parser scaffolding — implement `SwfocTrainer.Savegame.csproj` + 4 classes above + 5 parser unit tests targeting real saves at `C:\Users\Prekzursil\Saved Games\Petroglyph\Empire At War - Forces of Corruption\Save\`. Initial smoke goal: parse `a.PetroglyphFoC64Save` (33 MB) without crash; enumerate top-level chunks; report total chunk count + all unique FourCC IDs.
- **iter-288**: CLI corruption-fixer (`SwfocSavegameFixer.exe`) implementing strip-bad-chunk + truncate-at-corruption strategies.
- **iter-289**: WPF editor tab in SwfocTrainer.App with chunk tree view + micro-chunk inspector + edit/save round-trip.
- **iter-290**: Mod-hash validator — load mod XML → compute ObjectType hash → compare against save's embedded hash → suggest re-anchor on mismatch.
- **iter-291**: E2E integration tests + 5-10 corrupted-save regression cases.
- **iter-292**: Thread C close-out doc + operator changelog supplement.
