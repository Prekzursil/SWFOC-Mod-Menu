using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchApplyServiceFailureCoverageTests
{
    private static readonly SaveNode EmptyRoot = new("/", "Root", "root", null, Array.Empty<SaveNode>());

    [Fact]
    public async Task RestoreLastBackupAsync_ShouldReturnFalse_WhenNoBackupExists()
    {
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, new byte[32]);

        var service = new SavePatchApplyService(
            new StubCodec(),
            new StubPatchPackService(),
            NullLogger<SavePatchApplyService>.Instance);

        var result = await service.RestoreLastBackupAsync(targetPath, CancellationToken.None);

        result.Restored.Should().BeFalse();
        result.Message.Should().Contain("No backup file was found");
    }

    [Fact]
    public async Task ApplyAsync_ShouldRejectUnsupportedOperationKind()
    {
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, new byte[64]);

        var service = new SavePatchApplyService(
            new StubCodec(),
            new StubPatchPackService(),
            NullLogger<SavePatchApplyService>.Instance);

        var pack = CreatePack(
            new SavePatchOperation((SavePatchOperationKind)77, "/economy/credits_empire", "credits_empire", "int32", 0, 1234, 0));

        var result = await service.ApplyAsync(targetPath, pack, "base_swfoc", strict: true, CancellationToken.None);

        result.Classification.Should().Be(SavePatchApplyClassification.ValidationFailed);
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("unsupported_operation_kind");
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnRolledBack_WhenWriteFailsAfterBackup()
    {
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");
        var originalBytes = Enumerable.Range(0, 64).Select(static x => (byte)x).ToArray();
        await File.WriteAllBytesAsync(targetPath, originalBytes);

        var codec = new StubCodec
        {
            WriteFailure = new IOException("synthetic write failure")
        };
        var service = new SavePatchApplyService(codec, new StubPatchPackService(), NullLogger<SavePatchApplyService>.Instance);

        var pack = CreatePack(new SavePatchOperation(SavePatchOperationKind.SetValue, "/economy/credits_empire", "credits_empire", "int32", 0, 9000, 0));

        var result = await service.ApplyAsync(targetPath, pack, "base_swfoc", strict: true, CancellationToken.None);

        result.Classification.Should().Be(SavePatchApplyClassification.RolledBack);
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("write_failed_rolled_back");
        File.ReadAllBytes(targetPath).Should().Equal(originalBytes);
        Directory.EnumerateFiles(tempDir, "*.bak.*.sav").Should().NotBeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnWriteFailed_WhenRollbackCannotRestoreTarget()
    {
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, Enumerable.Repeat((byte)0x42, 64).ToArray());

        var codec = new StubCodec
        {
            WriteFailure = new IOException("synthetic write failure"),
            SabotageTargetOnWriteFailure = true
        };
        var service = new SavePatchApplyService(codec, new StubPatchPackService(), NullLogger<SavePatchApplyService>.Instance);

        var pack = CreatePack(new SavePatchOperation(SavePatchOperationKind.SetValue, "/economy/credits_empire", "credits_empire", "int32", 0, 9000, 0));

        var result = await service.ApplyAsync(targetPath, pack, "base_swfoc", strict: true, CancellationToken.None);

        result.Classification.Should().Be(SavePatchApplyClassification.WriteFailed);
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("write_failed");
        Directory.Exists(targetPath).Should().BeTrue();
    }

    private static SavePatchPack CreatePack(params SavePatchOperation[] operations)
    {
        return new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "base_swfoc_steam_v1", "a".PadLeft(64, 'a'), DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["base_swfoc"], "base_swfoc_steam_v1"),
            operations);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swfoc-apply-gap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubPatchPackService : ISavePatchPackService
    {
        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId) => throw new NotSupportedException();
        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SavePatchPack> LoadPackAsync(string path) => throw new NotSupportedException();
        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
            => Task.FromResult(new SavePatchCompatibilityResult(true, true, "target_hash", Array.Empty<string>(), Array.Empty<string>()));
        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId)
            => Task.FromResult(new SavePatchCompatibilityResult(true, true, "target_hash", Array.Empty<string>(), Array.Empty<string>()));
        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId) => throw new NotSupportedException();
    }

    private sealed class StubCodec : ISaveCodec
    {
        public Exception? WriteFailure { get; init; }
        public bool SabotageTargetOnWriteFailure { get; init; }
        private string? _loadedPath;

        public async Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken)
        {
            _loadedPath = path;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return new SaveDocument(path, schemaId, bytes, EmptyRoot);
        }

        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken)
        {
            _ = document;
            _ = nodePath;
            _ = cancellationToken;
            BitConverter.GetBytes(Convert.ToInt32(value)).CopyTo(document.Raw, 0);
            return Task.CompletedTask;
        }

        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            _ = document;
            _ = cancellationToken;
            return Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        }

        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken)
        {
            _ = document;
            _ = outputPath;
            _ = cancellationToken;
            if (SabotageTargetOnWriteFailure && !string.IsNullOrWhiteSpace(_loadedPath))
            {
                File.Delete(_loadedPath);
                Directory.CreateDirectory(_loadedPath);
            }

            throw WriteFailure ?? new IOException("synthetic write failure");
        }

        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            _ = document;
            _ = cancellationToken;
            return Task.FromResult(true);
        }
    }
}
