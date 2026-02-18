using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchApplyServiceTests
{
    [Fact]
    public async Task ApplyAsync_ShouldApplyAtomically_AndWriteBackupAndReceipt()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var preHash = ComputeFileHash(targetPath);

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 7777, "base_swfoc");

        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: true);

        result.Classification.Should().Be(SavePatchApplyClassification.Applied);
        result.Applied.Should().BeTrue();
        result.BackupPath.Should().NotBeNullOrWhiteSpace();
        result.ReceiptPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(result.BackupPath!).Should().BeTrue();
        File.Exists(result.ReceiptPath!).Should().BeTrue();

        var reloaded = await fixture.Codec.LoadAsync(targetPath, "base_swfoc_steam_v1");
        BitConverter.ToInt32(reloaded.Raw, 6144).Should().Be(7777);

        var rollback = await fixture.ApplyService.RestoreLastBackupAsync(targetPath);
        rollback.Restored.Should().BeTrue();

        var postRollbackHash = ComputeFileHash(targetPath);
        postRollbackHash.Should().Be(preHash);
    }

    [Fact]
    public async Task ApplyAsync_ShouldFailCompatibility_OnProfileMismatch()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 5555, "base_swfoc");

        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_sweaw", strict: true);

        result.Classification.Should().Be(SavePatchApplyClassification.CompatibilityFailed);
        result.Applied.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("compatibility_failed");
    }

    [Fact]
    public async Task ApplyAsync_ShouldRollbackOnValidationFailure_AndKeepOriginalBytes()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var preHash = ComputeFileHash(targetPath);

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", -1, "base_swfoc");

        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: true);

        result.Classification.Should().Be(SavePatchApplyClassification.ValidationFailed);
        result.Applied.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("validation_failed");

        var postHash = ComputeFileHash(targetPath);
        postHash.Should().Be(preHash);
    }

    [Fact]
    public async Task ApplyAsync_ShouldBlockOnStrictSourceHashMismatch()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var sourcePath = Path.Combine(tempDir, "source.sav");
        var targetPath = Path.Combine(tempDir, "target.sav");

        await File.WriteAllBytesAsync(sourcePath, CreateSyntheticBytes());
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, sourcePath, "/economy/credits_empire", 8888, "base_swfoc");

        // Drift the target save after patch export to force source-hash mismatch.
        var drifted = await fixture.Codec.LoadAsync(targetPath, "base_swfoc_steam_v1");
        await fixture.Codec.EditAsync(drifted, "/economy/credits_empire", 333);
        await fixture.Codec.WriteAsync(drifted, targetPath);

        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: true);

        result.Classification.Should().Be(SavePatchApplyClassification.CompatibilityFailed);
        result.Applied.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("source_hash_mismatch");
    }

    [Fact]
    public async Task ApplyAsync_ShouldAllowSourceHashMismatch_WhenStrictOff()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var sourcePath = Path.Combine(tempDir, "source.sav");
        var targetPath = Path.Combine(tempDir, "target.sav");

        await File.WriteAllBytesAsync(sourcePath, CreateSyntheticBytes());
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, sourcePath, "/economy/credits_empire", 9999, "base_swfoc");

        var drifted = await fixture.Codec.LoadAsync(targetPath, "base_swfoc_steam_v1");
        await fixture.Codec.EditAsync(drifted, "/economy/credits_empire", 123);
        await fixture.Codec.WriteAsync(drifted, targetPath);

        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: false);

        result.Classification.Should().Be(SavePatchApplyClassification.Applied);
        result.Applied.Should().BeTrue();

        var reloaded = await fixture.Codec.LoadAsync(targetPath, "base_swfoc_steam_v1");
        BitConverter.ToInt32(reloaded.Raw, 6144).Should().Be(9999);
    }

    [Fact]
    public async Task ApplyAsync_ShouldFallbackToFieldId_WhenFieldPathIsStale()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 4321, "base_swfoc");

        var stalePathPack = patch with
        {
            Operations = patch.Operations.Select(op => op.FieldId == "credits_empire"
                ? op with { FieldPath = "/economy/not_real_path" }
                : op).ToArray()
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, stalePathPack, "base_swfoc", strict: true);

        result.Classification.Should().Be(SavePatchApplyClassification.Applied);
        result.Applied.Should().BeTrue();

        var reloaded = await fixture.Codec.LoadAsync(targetPath, "base_swfoc_steam_v1");
        BitConverter.ToInt32(reloaded.Raw, 6144).Should().Be(4321);
    }

    [Fact]
    public async Task ApplyAsync_ShouldPreferFieldId_WhenFieldPathPointsToDifferentValidField()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 2468, "base_swfoc");

        var wrongButValidPathPack = patch with
        {
            Operations = patch.Operations.Select(op => op.FieldId == "credits_empire"
                ? op with { FieldPath = "/economy/credits_rebel" }
                : op).ToArray()
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, wrongButValidPathPack, "base_swfoc", strict: true);

        result.Classification.Should().Be(SavePatchApplyClassification.Applied);
        result.Applied.Should().BeTrue();

        var reloaded = await fixture.Codec.LoadAsync(targetPath, "base_swfoc_steam_v1");
        BitConverter.ToInt32(reloaded.Raw, 6144).Should().Be(2468);
        BitConverter.ToInt32(reloaded.Raw, 6148).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_ShouldRejectNullNewValue_WithValidationFailed()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 4444, "base_swfoc");

        var nullValuePack = patch with
        {
            Operations = patch.Operations.Select(op => op.FieldId == "credits_empire"
                ? op with { NewValue = null }
                : op).ToArray()
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, nullValuePack, "base_swfoc", strict: true);

        result.Applied.Should().BeFalse();
        result.Classification.Should().Be(SavePatchApplyClassification.ValidationFailed);
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("new_value_missing");
    }

    [Fact]
    public async Task RestoreLastBackupAsync_ShouldFallback_WhenLatestReceiptIsMalformed()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var preHash = ComputeFileHash(targetPath);
        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 7777, "base_swfoc");
        var apply = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: true);
        apply.Applied.Should().BeTrue();

        var malformedReceipt = $"{targetPath}.apply-receipt.999999999999999.json";
        await File.WriteAllTextAsync(malformedReceipt, "{ this is not valid json");
        File.SetLastWriteTimeUtc(malformedReceipt, DateTime.UtcNow.AddMinutes(1));

        var rollback = await fixture.ApplyService.RestoreLastBackupAsync(targetPath);

        rollback.Restored.Should().BeTrue();
        ComputeFileHash(targetPath).Should().Be(preHash);
    }

    [Fact]
    public async Task RestoreLastBackupAsync_ShouldUseBackupScan_WhenReceiptBackupPathMissing()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var preHash = ComputeFileHash(targetPath);
        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 7000, "base_swfoc");
        var apply = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: true);
        apply.Applied.Should().BeTrue();
        apply.ReceiptPath.Should().NotBeNullOrWhiteSpace();

        var receiptJson = await File.ReadAllTextAsync(apply.ReceiptPath!);
        var receiptNode = JsonNode.Parse(receiptJson)!.AsObject();
        receiptNode["BackupPath"] = Path.Combine(tempDir, "missing-backup.sav");
        var newerReceiptPath = $"{targetPath}.apply-receipt.999999999999999.json";
        await File.WriteAllTextAsync(newerReceiptPath, receiptNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.SetLastWriteTimeUtc(newerReceiptPath, DateTime.UtcNow.AddMinutes(2));

        var rollback = await fixture.ApplyService.RestoreLastBackupAsync(targetPath);

        rollback.Restored.Should().BeTrue();
        ComputeFileHash(targetPath).Should().Be(preHash);
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnSanitizedFailureMessage_OnTargetLoadFailure()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 2000, "base_swfoc");
        var invalidSchemaPack = patch with
        {
            Metadata = patch.Metadata with { SchemaId = "schema_does_not_exist" }
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, invalidSchemaPack, "base_swfoc", strict: true);

        result.Applied.Should().BeFalse();
        result.Classification.Should().Be(SavePatchApplyClassification.CompatibilityFailed);
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("target_load_failed");
        result.Message.Should().NotContain("schema_does_not_exist");
        result.Message.Should().NotContain("\\");
        result.Failure.Message.Should().NotContain("\\");
    }

    private static async Task<SavePatchPack> BuildPatchAsync(
        ISaveCodec codec,
        ISavePatchPackService patchPackService,
        string sourcePath,
        string fieldPath,
        object newValue,
        string profileId)
    {
        var original = await codec.LoadAsync(sourcePath, "base_swfoc_steam_v1");
        var edited = new SaveDocument(
            sourcePath,
            original.SchemaId,
            original.Raw.ToArray(),
            original.Root);

        await codec.EditAsync(edited, fieldPath, newValue);
        return await patchPackService.ExportAsync(original, edited, profileId);
    }

    private static (ISaveCodec Codec, ISavePatchPackService PatchPackService, SavePatchApplyService ApplyService) CreateFixture()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new SaveOptions
        {
            SchemaRootPath = Path.Combine(root, "profiles", "default", "schemas")
        };

        var codec = new BinarySaveCodec(options, NullLogger<BinarySaveCodec>.Instance);
        var packService = new SavePatchPackService(options);
        var applyService = new SavePatchApplyService(codec, packService, NullLogger<SavePatchApplyService>.Instance);
        return (codec, packService, applyService);
    }

    private static byte[] CreateSyntheticBytes() => new byte[300_000];

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swfoc-savepatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeFileHash(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
