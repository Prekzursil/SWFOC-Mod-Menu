using System.Diagnostics;
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CaptureFromPathAsync_ShouldThrowArgumentException_WhenModulePathMissing(string modulePath)
    {
        var service = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);

        var action = async () => await service.CaptureFromPathAsync(modulePath);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CaptureFromPathAsync_ShouldThrowFileNotFound_WhenModuleMissing()
    {
        var service = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.bin");

        var action = async () => await service.CaptureFromPathAsync(missingPath);

        var ex = await action.Should().ThrowAsync<FileNotFoundException>();
        ex.Which.FileName.Should().Be(Path.GetFullPath(missingPath));
    }

    [Fact]
    public async Task CaptureFromPathAsync_WithInvalidProcessId_ShouldFallbackToEmptyModuleList()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-fingerprint-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempPath, Encoding.UTF8.GetBytes("swfoc-fingerprint-invalid-pid"));

        try
        {
            var service = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);

            var result = await service.CaptureFromPathAsync(tempPath, processId: int.MaxValue);

            result.ModuleList.Should().BeEmpty();
            result.FingerprintId.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task CaptureFromPathAsync_WithCurrentProcessId_ShouldReturnFingerprintFromOverload()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-fingerprint-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempPath, Encoding.UTF8.GetBytes("swfoc-fingerprint-current-pid"));

        try
        {
            var service = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
            using var cts = new CancellationTokenSource();

            var result = await service.CaptureFromPathAsync(tempPath, Process.GetCurrentProcess().Id, cts.Token);

            result.ModuleName.Should().Be(Path.GetFileName(tempPath));
            result.ModuleList.Should().NotBeNull();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
