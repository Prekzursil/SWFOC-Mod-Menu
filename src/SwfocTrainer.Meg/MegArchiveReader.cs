using System.Buffers.Binary;
using System.Text;

namespace SwfocTrainer.Meg;

public sealed class MegArchiveReader : IMegArchiveReader
{
    private const uint Format2Or3MagicA = 0xFFFFFFFF;
    private const uint Format3EncryptedMagicA = 0x8FFFFFFF;
    private const uint Format2Or3MagicB = 0x3F7D70A4;
    private const int MaxReasonableTableEntries = 250000;

    public MegOpenResult Open(string megPath)
    {
        if (string.IsNullOrWhiteSpace(megPath))
        {
            return MegOpenResult.Fail("invalid_path", "MEG path is required.");
        }

        if (!File.Exists(megPath))
        {
            return MegOpenResult.Fail("missing_file", $"MEG file not found: {megPath}");
        }

        try
        {
            var bytes = File.ReadAllBytes(megPath);
            return Open(bytes, megPath);
        }
        catch (Exception ex)
        {
            return MegOpenResult.Fail("io_error", $"Failed reading MEG file '{megPath}': {ex.Message}");
        }
    }

    public MegOpenResult Open(ReadOnlyMemory<byte> payload)
    {
        return Open(payload, "<memory>");
    }

    public MegOpenResult Open(ReadOnlyMemory<byte> payload, string sourceName)
    {
        if (payload.Length < 8)
        {
            return MegOpenResult.Fail("invalid_header", "MEG payload is too small to contain a valid header.");
        }

        var diagnostics = new List<string>();
        var bytes = payload.ToArray();
        try
        {
            var header = ParseHeader(bytes, diagnostics, out var headerCursor);
            if (!header.IsValid)
            {
                return MegOpenResult.Fail("invalid_header", header.ErrorMessage ?? "MEG header validation failed.", diagnostics);
            }

            if (header.IsEncrypted)
            {
                return MegOpenResult.Fail("encrypted_archive_unsupported", "Encrypted MEG archives are not supported by this reader.", diagnostics);
            }

            var names = ParseNames(bytes, header, ref headerCursor, diagnostics);
            if (names is null &&
                header.Format == "format3" &&
                TryParseFormat2Fallback(bytes, diagnostics, out header, out headerCursor, out names))
            {
                diagnostics.Add("format3 parse fallback succeeded as format2.");
            }

            if (names is null)
            {
                return MegOpenResult.Fail("invalid_name_table", "Failed parsing MEG filename table.", diagnostics);
            }

            var entries = ParseEntries(bytes, header, names, ref headerCursor, diagnostics);
            if (entries is null)
            {
                return MegOpenResult.Fail("invalid_file_table", "Failed parsing MEG file table.", diagnostics);
            }

            var ordered = entries
                .OrderBy(x => x.Index)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var archive = new MegArchive(
                source: sourceName,
                format: header.Format,
                entries: ordered,
                payload: bytes,
                diagnostics: diagnostics);
            return MegOpenResult.Success(archive, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Unhandled parse exception: {ex.Message}");
            return MegOpenResult.Fail("parse_exception", $"MEG parse failed: {ex.Message}", diagnostics);
        }
    }

    private static bool TryParseFormat2Fallback(
        byte[] bytes,
        ICollection<string> diagnostics,
        out ParsedHeader header,
        out int cursor,
        out IReadOnlyList<string>? names)
    {
        header = ParseFormat2Header(bytes, diagnostics);
        cursor = 20;
        names = null;
        if (!header.IsValid)
        {
            return false;
        }

        names = ParseNames(bytes, header, ref cursor, diagnostics);
        return names is not null;
    }

    private static ParsedHeader ParseHeader(byte[] bytes, ICollection<string> diagnostics, out int cursor)
    {
        cursor = 0;
        var firstWord = ReadUInt32(bytes, 0);
        var secondWord = ReadUInt32(bytes, 4);

        if (firstWord == Format3EncryptedMagicA && secondWord == Format2Or3MagicB)
        {
            if (bytes.Length < 24)
            {
                return ParsedHeader.Fail("format3 header is truncated.");
            }

            var format3 = TryParseFormat3Header(bytes, firstWord, diagnostics);
            if (!format3.IsValid)
            {
                return ParsedHeader.Fail(format3.ErrorMessage ?? "unable to parse format3 header.");
            }

            cursor = 24;
            diagnostics.Add($"Detected {format3.Format} header with {format3.NameCount} names and {format3.FileCount} files.");
            return format3;
        }

        if (firstWord == Format2Or3MagicA && secondWord == Format2Or3MagicB)
        {
            if (bytes.Length < 20)
            {
                return ParsedHeader.Fail("format2 header is truncated.");
            }

            var format2 = ParseFormat2Header(bytes, diagnostics);
            if (format2.IsValid)
            {
                cursor = 20;
                diagnostics.Add($"Detected {format2.Format} header with {format2.NameCount} names and {format2.FileCount} files.");
                return format2;
            }

            return ParsedHeader.Fail(format2.ErrorMessage ?? "unable to parse format2 header.");
        }

        var format1 = ParseFormat1Header(bytes, diagnostics);
        if (format1.IsValid)
        {
            cursor = 8;
            diagnostics.Add($"Detected {format1.Format} header with {format1.NameCount} names and {format1.FileCount} files.");
            return format1;
        }

        return ParsedHeader.Fail("unable to identify supported MEG header variant.");
    }

    private static ParsedHeader ParseFormat1Header(byte[] bytes, ICollection<string> diagnostics)
    {
        var nameCount = ReadUInt32(bytes, 0);
        var fileCount = ReadUInt32(bytes, 4);
        if (!ValidateCounts(nameCount, fileCount, diagnostics))
        {
            return ParsedHeader.Fail("format1 name/file counts are outside supported bounds.");
        }

        return new ParsedHeader(
            IsValid: true,
            ErrorMessage: null,
            Format: "format1",
            NameCount: nameCount,
            FileCount: fileCount,
            DataStartOffset: 0,
            NameTableSize: null,
            IsEncrypted: false,
            SupportsEntryFlags: false);
    }

    private static ParsedHeader ParseFormat2Header(byte[] bytes, ICollection<string> diagnostics)
    {
        var dataStart = ReadUInt32(bytes, 8);
        var nameCount = ReadUInt32(bytes, 12);
        var fileCount = ReadUInt32(bytes, 16);
        if (!ValidateCounts(nameCount, fileCount, diagnostics))
        {
            return ParsedHeader.Fail("format2 name/file counts are outside supported bounds.");
        }

        if (dataStart > bytes.Length)
        {
            diagnostics.Add($"format2 dataStart={dataStart} exceeds archive length={bytes.Length}.");
            return ParsedHeader.Fail("format2 dataStart exceeds payload length.");
        }

        return new ParsedHeader(
            IsValid: true,
            ErrorMessage: null,
            Format: "format2",
            NameCount: nameCount,
            FileCount: fileCount,
            DataStartOffset: dataStart,
            NameTableSize: null,
            IsEncrypted: false,
            SupportsEntryFlags: false);
    }

    private static ParsedHeader TryParseFormat3Header(byte[] bytes, uint firstWord, ICollection<string> diagnostics)
    {
        var dataStart = ReadUInt32(bytes, 8);
        var nameCount = ReadUInt32(bytes, 12);
        var fileCount = ReadUInt32(bytes, 16);
        var nameTableSize = ReadUInt32(bytes, 20);
        if (!ValidateCounts(nameCount, fileCount, diagnostics))
        {
            return ParsedHeader.Fail("format3 name/file counts are outside supported bounds.");
        }

        if (nameTableSize > bytes.Length - 24)
        {
            return ParsedHeader.Fail("format3 name table size exceeds payload length.");
        }

        if (dataStart > bytes.Length)
        {
            return ParsedHeader.Fail("format3 dataStart exceeds payload length.");
        }

        var minimumDataStart = 24UL + nameTableSize + (20UL * fileCount);
        if (dataStart < minimumDataStart)
        {
            return ParsedHeader.Fail("format3 dataStart is smaller than header+table footprint.");
        }

        return new ParsedHeader(
            IsValid: true,
            ErrorMessage: null,
            Format: "format3",
            NameCount: nameCount,
            FileCount: fileCount,
            DataStartOffset: dataStart,
            NameTableSize: nameTableSize,
            IsEncrypted: firstWord == Format3EncryptedMagicA,
            SupportsEntryFlags: true);
    }

    private static bool ValidateCounts(uint nameCount, uint fileCount, ICollection<string> diagnostics)
    {
        if (nameCount > MaxReasonableTableEntries || fileCount > MaxReasonableTableEntries)
        {
            diagnostics.Add($"Unreasonable MEG counts detected names={nameCount} files={fileCount}.");
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string>? ParseNames(
        byte[] bytes,
        ParsedHeader header,
        ref int cursor,
        ICollection<string> diagnostics)
    {
        var names = new List<string>((int)header.NameCount);
        var nameTableEnd = header.NameTableSize.HasValue
            ? checked(cursor + (int)header.NameTableSize.Value)
            : bytes.Length;

        for (var i = 0; i < header.NameCount; i++)
        {
            if (!TryEnsureRange(bytes.Length, cursor, 4, out var rangeError))
            {
                diagnostics.Add($"Name[{i}] header truncated: {rangeError}");
                return null;
            }

            var nameLength = ReadUInt16(bytes, cursor);
            cursor += 4;
            if (!TryEnsureRange(bytes.Length, cursor, nameLength, out rangeError))
            {
                diagnostics.Add($"Name[{i}] bytes truncated: {rangeError}");
                return null;
            }

            if (cursor + nameLength > nameTableEnd)
            {
                diagnostics.Add($"Name[{i}] length spills past format3 name table boundary.");
                return null;
            }

            var name = Encoding.ASCII.GetString(bytes, cursor, nameLength).Trim();
            cursor += nameLength;
            names.Add(name);
        }

        if (header.NameTableSize.HasValue && cursor != nameTableEnd)
        {
            diagnostics.Add($"Name table parsed with trailing bytes: consumed={cursor} expectedEnd={nameTableEnd}.");
        }

        return names;
    }

    private static IReadOnlyList<MegEntry>? ParseEntries(
        byte[] bytes,
        ParsedHeader header,
        IReadOnlyList<string> names,
        ref int cursor,
        ICollection<string> diagnostics)
    {
        var entries = new List<MegEntry>((int)header.FileCount);
        for (var i = 0; i < header.FileCount; i++)
        {
            if (!TryParseEntryRecord(bytes, header, ref cursor, diagnostics, i, out var parsedEntry))
            {
                return null;
            }

            if (parsedEntry.NameIndex >= names.Count)
            {
                diagnostics.Add($"File[{i}] points to missing nameIndex={parsedEntry.NameIndex} while names={names.Count}.");
                return null;
            }

            if (!IsEntryRangeValid(parsedEntry, bytes.Length, header.DataStartOffset, diagnostics, i))
            {
                return null;
            }

            entries.Add(new MegEntry(
                Path: names[(int)parsedEntry.NameIndex],
                Crc32: parsedEntry.Crc,
                Index: checked((int)parsedEntry.Index),
                SizeBytes: checked((int)parsedEntry.Size),
                StartOffset: checked((int)parsedEntry.Start),
                Flags: parsedEntry.EntryFlags));
        }

        return entries;
    }

    private static bool TryParseEntryRecord(
        byte[] bytes,
        ParsedHeader header,
        ref int cursor,
        ICollection<string> diagnostics,
        int entryIndex,
        out ParsedEntry entry)
    {
        entry = default;
        if (!TryEnsureRange(bytes.Length, cursor, 20, out var rangeError))
        {
            diagnostics.Add($"File[{entryIndex}] record truncated: {rangeError}");
            return false;
        }

        if (header.SupportsEntryFlags)
        {
            var entryFlags = ReadUInt16(bytes, cursor);
            if (entryFlags != 0)
            {
                diagnostics.Add($"File[{entryIndex}] has unsupported encrypted/compressed flags={entryFlags}.");
                return false;
            }

            entry = new ParsedEntry(
                EntryFlags: entryFlags,
                Crc: ReadUInt32(bytes, cursor + 2),
                Index: ReadUInt32(bytes, cursor + 6),
                Size: ReadUInt32(bytes, cursor + 10),
                Start: ReadUInt32(bytes, cursor + 14),
                NameIndex: ReadUInt16(bytes, cursor + 18));
        }
        else
        {
            entry = new ParsedEntry(
                EntryFlags: 0,
                Crc: ReadUInt32(bytes, cursor),
                Index: ReadUInt32(bytes, cursor + 4),
                Size: ReadUInt32(bytes, cursor + 8),
                Start: ReadUInt32(bytes, cursor + 12),
                NameIndex: ReadUInt32(bytes, cursor + 16));
        }

        cursor += 20;
        return true;
    }

    private static bool IsEntryRangeValid(
        ParsedEntry entry,
        int payloadLength,
        uint dataStartOffset,
        ICollection<string> diagnostics,
        int entryIndex)
    {
        if (entry.Start > payloadLength || entry.Start + entry.Size > payloadLength)
        {
            diagnostics.Add($"File[{entryIndex}] has invalid content span start={entry.Start} size={entry.Size} length={payloadLength}.");
            return false;
        }

        if (dataStartOffset > 0 && entry.Start < dataStartOffset)
        {
            diagnostics.Add($"File[{entryIndex}] starts before header dataStart offset ({entry.Start} < {dataStartOffset}).");
            return false;
        }

        return true;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }

    private static bool TryEnsureRange(int length, int offset, uint bytesNeeded, out string error)
    {
        if (offset < 0 || offset > length)
        {
            error = $"offset {offset} is outside length {length}.";
            return false;
        }

        var available = length - offset;
        if (bytesNeeded > available)
        {
            error = $"need {bytesNeeded} bytes at offset {offset}, available {available}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed record ParsedHeader(
        bool IsValid,
        string? ErrorMessage,
        string Format,
        uint NameCount,
        uint FileCount,
        uint DataStartOffset,
        uint? NameTableSize,
        bool IsEncrypted,
        bool SupportsEntryFlags)
    {
        public static ParsedHeader Fail(string message) =>
            new(
                IsValid: false,
                ErrorMessage: message,
                Format: "unknown",
                NameCount: 0,
                FileCount: 0,
                DataStartOffset: 0,
                NameTableSize: null,
                IsEncrypted: false,
            SupportsEntryFlags: false);
    }

    private readonly record struct ParsedEntry(
        ushort EntryFlags,
        uint Crc,
        uint Index,
        uint Size,
        uint Start,
        uint NameIndex);
}
