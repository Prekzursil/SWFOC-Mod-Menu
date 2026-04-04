namespace SwfocTrainer.Tests.Common;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix = "swfoc-test")
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // ignore cleanup failures in test teardown — files may be locked
        }
        catch (UnauthorizedAccessException)
        {
            // ignore permission errors during cleanup
        }
    }
}
