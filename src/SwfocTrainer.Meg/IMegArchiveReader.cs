namespace SwfocTrainer.Meg;

public interface IMegArchiveReader
{
    MegOpenResult Open(string megPath);

    MegOpenResult Open(ReadOnlyMemory<byte> payload);

    MegOpenResult Open(ReadOnlyMemory<byte> payload, string sourceName);
}
