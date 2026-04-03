namespace SwfocTrainer.Meg;

public sealed record MegEntry(
    string Path,
    long Crc32,
    int Index,
    int SizeBytes,
    int StartOffset,
    int Flags = 0);
