using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class BinaryFingerprintServiceTests
{
    [Fact]
    public async Task CaptureFromPathAsync_ShouldProduceStableFingerprint()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-fingerprint-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempPath, Encoding.UTF8.GetBytes("swfoc-fingerprint-test"));

        try
        {
            var service = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);

            var result = await service.CaptureFromPathAsync(tempPath);

            result.SourcePath.Should().Be(Path.GetFullPath(tempPath));
            result.ModuleName.Should().Be(Path.GetFileName(tempPath));
            result.FileSha256.Should().Be(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(tempPath))).ToLowerInvariant());
            result.FingerprintId.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
