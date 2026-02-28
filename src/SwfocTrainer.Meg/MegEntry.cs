namespace SwfocTrainer.Meg;

public sealed record MegEntry(
    string Path,
    uint Crc32,
    int Index,
    int SizeBytes,
    int StartOffset,
    ushort Flags = 0);
