using System.Security.Cryptography;
using System.Text.Json;
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

/// <summary>
/// Wave 2 coverage: fills remaining branches in SavePatchApplyService and Helpers.
/// </summary>
public sealed class SavesWave2CoverageTests
{
    [Fact]
    public async Task ApplyAsync_TwoParamOverload_ShouldDelegate()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 5000, "base_swfoc");
        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc");

        result.Applied.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_ThreeParamOverload_ShouldDelegate()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 6000, "base_swfoc");
        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: false);

        result.Applied.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreLastBackupAsync_ParameterlessOverload_ShouldDelegate()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var result = await fixture.ApplyService.RestoreLastBackupAsync(targetPath);

        result.Restored.Should().BeFalse();
        result.Message.Should().Contain("No backup");
    }

    [Fact]
    public async Task ApplyAsync_ShouldRejectUnsupportedOperationKind()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 4444, "base_swfoc");
        var badKindPack = patch with
        {
            Operations = patch.Operations.Select(op => op with { Kind = (SavePatchOperationKind)999 }).ToArray()
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, badKindPack, "base_swfoc", strict: true);

        result.Applied.Should().BeFalse();
        result.Classification.Should().Be(SavePatchApplyClassification.ValidationFailed);
        result.Failure!.ReasonCode.Should().Be("unsupported_operation_kind");
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnValueNormalizationFailed_ForBadValueType()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 1234, "base_swfoc");
        var badValuePack = patch with
        {
            Operations = patch.Operations.Select(op => op with { NewValue = "not_a_number", ValueType = "Int32" }).ToArray()
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, badValuePack, "base_swfoc", strict: true);

        result.Applied.Should().BeFalse();
        result.Failure!.ReasonCode.Should().Be("value_normalization_failed");
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnFieldApplyFailed_WhenBothSelectorsAreInvalid()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 3333, "base_swfoc");
        var badSelectorPack = patch with
        {
            Operations = patch.Operations.Select(op => op with
            {
                FieldId = "completely_unknown_field_12345",
                FieldPath = "/completely/unknown/path/12345"
            }).ToArray()
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, badSelectorPack, "base_swfoc", strict: true);

        result.Applied.Should().BeFalse();
        result.Failure!.ReasonCode.Should().Be("field_apply_failed_all_selectors");
    }

    [Fact]
    public async Task ApplyAsync_ShouldReturnFieldApplyFailed_WhenBothSelectorsAreBlank()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 2222, "base_swfoc");
        var blankSelectorPack = patch with
        {
            Operations = patch.Operations.Select(op => op with
            {
                FieldId = "  ",
                FieldPath = "  "
            }).ToArray()
        };

        var result = await fixture.ApplyService.ApplyAsync(targetPath, blankSelectorPack, "base_swfoc", strict: true);

        result.Applied.Should().BeFalse();
        result.Failure!.ReasonCode.Should().Be("field_apply_failed_all_selectors");
    }

    [Fact]
    public async Task RestoreLastBackupAsync_ShouldReturnNotRestored_WhenTargetSavePathHasNoDir()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        // With no backup files present, should return not restored
        var result = await fixture.ApplyService.RestoreLastBackupAsync(targetPath, CancellationToken.None);
        result.Restored.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreLastBackupAsync_ShouldUseReceiptBackupPath_WhenReceiptIsValid()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");

        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var preHash = ComputeFileHash(targetPath);

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 8000, "base_swfoc");
        var apply = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: true);
        apply.Applied.Should().BeTrue();

        var rollback = await fixture.ApplyService.RestoreLastBackupAsync(targetPath, CancellationToken.None);
        rollback.Restored.Should().BeTrue();
        rollback.RestoredHash.Should().NotBeNullOrWhiteSpace();
        ComputeFileHash(targetPath).Should().Be(preHash);
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrow_WhenArgumentsAreNull()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var targetPath = Path.Join(tempDir, "campaign.sav");
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());
        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, targetPath, "/economy/credits_empire", 1000, "base_swfoc");

        var act1 = async () => await fixture.ApplyService.ApplyAsync(null!, patch, "base_swfoc", true, CancellationToken.None);
        var act2 = async () => await fixture.ApplyService.ApplyAsync(targetPath, null!, "base_swfoc", true, CancellationToken.None);
        var act3 = async () => await fixture.ApplyService.ApplyAsync(targetPath, patch, null!, true, CancellationToken.None);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
        await act3.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RestoreLastBackupAsync_ShouldThrow_WhenTargetPathIsNull()
    {
        var fixture = CreateFixture();
        var act = async () => await fixture.ApplyService.RestoreLastBackupAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDependenciesAreNull()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new SaveOptions { SchemaRootPath = Path.Join(root, "profiles", "default", "schemas") };
        var codec = new BinarySaveCodec(options, NullLogger<BinarySaveCodec>.Instance);
        var pack = new SavePatchPackService(options);

        var act1 = () => new SavePatchApplyService(null!, pack, NullLogger<SavePatchApplyService>.Instance);
        var act2 = () => new SavePatchApplyService(codec, null!, NullLogger<SavePatchApplyService>.Instance);
        var act3 = () => new SavePatchApplyService(codec, pack, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ApplyAsync_ShouldHandleCompatibilityNotStrict_WhenHashDoesNotMatch()
    {
        var fixture = CreateFixture();
        var tempDir = CreateTempDirectory();
        var sourcePath = Path.Join(tempDir, "source.sav");
        var targetPath = Path.Join(tempDir, "target.sav");
        await File.WriteAllBytesAsync(sourcePath, CreateSyntheticBytes());
        await File.WriteAllBytesAsync(targetPath, CreateSyntheticBytes());

        var patch = await BuildPatchAsync(fixture.Codec, fixture.PatchPackService, sourcePath, "/economy/credits_empire", 7777, "base_swfoc");
        var drifted = await fixture.Codec.LoadAsync(targetPath, "base_swfoc_steam_v1");
        await fixture.Codec.EditAsync(drifted, "/economy/credits_empire", 111);
        await fixture.Codec.WriteAsync(drifted, targetPath);

        // Non-strict should still succeed
        var result = await fixture.ApplyService.ApplyAsync(targetPath, patch, "base_swfoc", strict: false, CancellationToken.None);
        result.Applied.Should().BeTrue();
    }

    private static async Task<SavePatchPack> BuildPatchAsync(
        ISaveCodec codec, ISavePatchPackService patchPackService, string sourcePath,
        string fieldPath, object newValue, string profileId)
    {
        var original = await codec.LoadAsync(sourcePath, "base_swfoc_steam_v1");
        var edited = new SaveDocument(sourcePath, original.SchemaId, original.Raw.ToArray(), original.Root);
        await codec.EditAsync(edited, fieldPath, newValue);
        return await patchPackService.ExportAsync(original, edited, profileId);
    }

    private static (ISaveCodec Codec, ISavePatchPackService PatchPackService, SavePatchApplyService ApplyService) CreateFixture()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new SaveOptions { SchemaRootPath = Path.Join(root, "profiles", "default", "schemas") };
        var codec = new BinarySaveCodec(options, NullLogger<BinarySaveCodec>.Instance);
        var packService = new SavePatchPackService(options);
        var applyService = new SavePatchApplyService(codec, packService, NullLogger<SavePatchApplyService>.Instance);
        return (codec, packService, applyService);
    }

    private static byte[] CreateSyntheticBytes() => new byte[300_000];

    private static string CreateTempDirectory()
    {
        var path = Path.Join(Path.GetTempPath(), $"swfoc-savepatch-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeFileHash(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
