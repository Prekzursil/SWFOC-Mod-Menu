using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchPackServiceTests
{
    private static readonly SaveNode EmptyRoot = new("/", "Root", "root", null, Array.Empty<SaveNode>());
    private static readonly JsonSerializerOptions PatchJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ExportAsync_ShouldGenerateDeterministicOperationsForChangedFields()
    {
        var service = CreateService();

        var originalBytes = CreateSyntheticBytes();
        var editedBytes = originalBytes.ToArray();
        WriteInt32LittleEndian(editedBytes, 6144, 5000);     // credits_empire
        WriteInt32LittleEndian(editedBytes, 20484, 120);     // hero_vader_respawn

        var original = new SaveDocument("mem://original.sav", "base_swfoc_steam_v1", originalBytes, EmptyRoot);
        var edited = new SaveDocument("mem://edited.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);

        var pack = await service.ExportAsync(original, edited, "base_swfoc");

        pack.Metadata.SchemaVersion.Should().Be("1.0");
        pack.Metadata.ProfileId.Should().Be("base_swfoc");
        pack.Metadata.SchemaId.Should().Be("base_swfoc_steam_v1");
        pack.Metadata.SourceHash.Should().Be(ComputeSha256Hex(originalBytes));

        pack.Operations.Should().HaveCount(2);
        pack.Operations.Select(x => x.FieldId).Should().ContainInOrder("credits_empire", "hero_vader_respawn");
        pack.Operations[0].Kind.Should().Be(SavePatchOperationKind.SetValue);
        pack.Operations[0].ValueType.Should().Be("int32");
        pack.Operations[0].NewValue.Should().Be(5000);
        pack.Operations[1].NewValue.Should().Be(120);
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldRejectSchemaAndProfileMismatch()
    {
        var service = CreateService();

        var original = new SaveDocument("mem://original.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var editedBytes = original.Raw.ToArray();
        WriteInt32LittleEndian(editedBytes, 6144, 2000);
        var edited = new SaveDocument("mem://edited.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);

        var pack = await service.ExportAsync(original, edited, "base_swfoc");
        var mismatchedTarget = new SaveDocument("mem://target.sav", "base_sweaw_steam_v1", CreateSyntheticBytes(), EmptyRoot);

        var result = await service.ValidateCompatibilityAsync(pack, mismatchedTarget, "base_sweaw");

        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("schema", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(x => x.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectInvalidContract()
    {
        var service = CreateService();

        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-invalid-patch-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempPath, "{ \"metadata\": { \"schemaVersion\": \"1.0\" }, \"operations\": [] }");
            var act = () => service.LoadPackAsync(tempPath);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Theory]
    [InlineData("base_sweaw", "base_sweaw_steam_v1", 4096)]
    [InlineData("base_swfoc", "base_swfoc_steam_v1", 6144)]
    [InlineData("aotr_1397421866_swfoc", "aotr_1397421866_swfoc_v1", 8192)]
    [InlineData("roe_3447786229_swfoc", "roe_3447786229_swfoc_v1", 9216)]
    public async Task ValidateCompatibilityAsync_ShouldPassForAllShippedProfiles(string profileId, string schemaId, int creditsOffset)
    {
        var service = CreateService();

        var originalBytes = CreateSyntheticBytes();
        var editedBytes = originalBytes.ToArray();
        WriteInt32LittleEndian(editedBytes, creditsOffset, 7777);

        var original = new SaveDocument("mem://original.sav", schemaId, originalBytes, EmptyRoot);
        var edited = new SaveDocument("mem://edited.sav", schemaId, editedBytes, EmptyRoot);

        var pack = await service.ExportAsync(original, edited, profileId);
        var compatibility = await service.ValidateCompatibilityAsync(pack, original, profileId);

        compatibility.IsCompatible.Should().BeTrue();
        compatibility.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPackAsync_JsonRoundtrip_ShouldPreviewAndNormalizeTypedValues()
    {
        var patchPackService = CreateService();
        var tempDir = CreateTempDirectory();
        var patchPath = Path.Combine(tempDir, "roundtrip.patch.json");

        var originalBytes = CreateSyntheticBytes();
        var editedBytes = originalBytes.ToArray();
        WriteInt32LittleEndian(editedBytes, 6144, 4321);

        var original = new SaveDocument("mem://original.sav", "base_swfoc_steam_v1", originalBytes, EmptyRoot);
        var edited = new SaveDocument("mem://edited.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);
        var exported = await patchPackService.ExportAsync(original, edited, "base_swfoc");
        await File.WriteAllTextAsync(patchPath, JsonSerializer.Serialize(exported, PatchJson));

        var loaded = await patchPackService.LoadPackAsync(patchPath);
        var preview = await patchPackService.PreviewApplyAsync(loaded, original, "base_swfoc");

        preview.Errors.Should().BeEmpty();
        preview.OperationsToApply.Should().HaveCount(1);
        preview.OperationsToApply[0].NewValue.Should().Be(4321);
    }

    [Fact]
    public async Task LoadPackAsync_Fixture_ShouldPreviewWithoutErrors()
    {
        var patchPackService = CreateService();
        var root = TestPaths.FindRepoRoot();
        var fixturePath = Path.Combine(root, "tools", "fixtures", "save_patch_pack_sample.json");

        var pack = await patchPackService.LoadPackAsync(fixturePath);
        var target = new SaveDocument("mem://target.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var preview = await patchPackService.PreviewApplyAsync(pack, target, "base_swfoc");

        preview.Errors.Should().BeEmpty();
        preview.OperationsToApply.Should().HaveCount(2);
        preview.Warnings.Should().Contain(x => x.Contains("Source hash mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingNewValueWithIndexedError()
    {
        var service = CreateService();
        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-invalid-newvalue-{Guid.NewGuid():N}.json");

        try
        {
            var invalidJson = """
            {
              "metadata": {
                "schemaVersion": "1.0",
                "profileId": "base_swfoc",
                "schemaId": "base_swfoc_steam_v1",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "createdAtUtc": "2026-02-17T00:00:00Z"
              },
              "compatibility": {
                "allowedProfileIds": ["base_swfoc"],
                "requiredSchemaId": "base_swfoc_steam_v1"
              },
              "operations": [
                {
                  "kind": "SetValue",
                  "fieldPath": "/economy/credits_empire",
                  "fieldId": "credits_empire",
                  "valueType": "int32",
                  "offset": 6144
                }
              ]
            }
            """;

            await File.WriteAllTextAsync(tempPath, invalidJson);
            var act = () => service.LoadPackAsync(tempPath);
            var ex = await act.Should().ThrowAsync<InvalidDataException>();
            ex.Which.Message.Should().Contain("operations[0].newValue");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldWarnWhenFieldPathDiffersFromCanonicalPath()
    {
        var patchPackService = CreateService();

        var originalBytes = CreateSyntheticBytes();
        var editedBytes = originalBytes.ToArray();
        WriteInt32LittleEndian(editedBytes, 6144, 7777);

        var original = new SaveDocument("mem://original.sav", "base_swfoc_steam_v1", originalBytes, EmptyRoot);
        var edited = new SaveDocument("mem://edited.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);
        var pack = await patchPackService.ExportAsync(original, edited, "base_swfoc");

        var wrongPathPack = pack with
        {
            Operations = pack.Operations.Select(op => op.FieldId == "credits_empire"
                ? op with { FieldPath = "/economy/wrong_path" }
                : op).ToArray()
        };

        var preview = await patchPackService.PreviewApplyAsync(wrongPathPack, original, "base_swfoc");

        preview.Errors.Should().BeEmpty();
        preview.Warnings.Should().Contain(x => x.Contains("Field path mismatch", StringComparison.OrdinalIgnoreCase));
        preview.OperationsToApply.Should().HaveCount(1);
    }

    private static SavePatchPackService CreateService()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new SaveOptions
        {
            SchemaRootPath = Path.Combine(root, "profiles", "default", "schemas")
        };

        return new SavePatchPackService(options);
    }

    private static byte[] CreateSyntheticBytes() => new byte[300_000];

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swfoc-savepatch-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeSha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteInt32LittleEndian(byte[] bytes, int offset, int value)
    {
        var valueBytes = BitConverter.GetBytes(value);
        Array.Copy(valueBytes, 0, bytes, offset, 4);
    }
}
