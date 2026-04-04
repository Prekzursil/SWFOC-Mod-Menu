using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchPackServiceBranchTests
{
    private static readonly SaveNode EmptyRoot = new("/", "Root", "root", null, Array.Empty<SaveNode>());
    private static readonly JsonSerializerOptions PatchJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Constructor_ShouldThrow_WhenOptionsIsNull()
    {
        var act = () => new SavePatchPackService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenOriginalDocIsNull()
    {
        var service = CreateService();
        var act = () => service.ExportAsync(null!, MakeDoc(), "profile");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenEditedDocIsNull()
    {
        var service = CreateService();
        var act = () => service.ExportAsync(MakeDoc(), null!, "profile");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var service = CreateService();
        var act = () => service.ExportAsync(MakeDoc(), MakeDoc(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenProfileIdIsWhitespace()
    {
        var service = CreateService();
        var act = () => service.ExportAsync(MakeDoc(), MakeDoc(), "   ");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenSchemaIdsDiffer()
    {
        var service = CreateService();
        var original = MakeDoc("base_swfoc_steam_v1");
        var edited = MakeDoc("base_sweaw_steam_v1");
        var act = () => service.ExportAsync(original, edited, "profile");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldExcludeChecksumOverlapFields()
    {
        var service = CreateService();
        var originalBytes = new byte[300_000];
        var editedBytes = originalBytes.ToArray();
        editedBytes[508] = 0xFF;
        editedBytes[509] = 0xFF;
        editedBytes[510] = 0xFF;
        editedBytes[511] = 0xFF;

        var original = new SaveDocument("mem://o.sav", "base_swfoc_steam_v1", originalBytes, EmptyRoot);
        var edited = new SaveDocument("mem://e.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);

        var pack = await service.ExportAsync(original, edited, "base_swfoc");
        pack.Operations.Should().NotContain(x => x.FieldId == "header_crc32");
    }

    [Fact]
    public async Task ExportAsync_ShouldSkipUnchangedFields()
    {
        var service = CreateService();
        var bytes = new byte[300_000];
        var original = new SaveDocument("mem://o.sav", "base_swfoc_steam_v1", bytes, EmptyRoot);
        var edited = new SaveDocument("mem://e.sav", "base_swfoc_steam_v1", bytes.ToArray(), EmptyRoot);

        var pack = await service.ExportAsync(original, edited, "base_swfoc");
        pack.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_Overload_ShouldDelegate()
    {
        var service = CreateService();
        var originalBytes = new byte[300_000];
        var editedBytes = originalBytes.ToArray();
        WriteInt32LE(editedBytes, 6144, 111);
        var original = new SaveDocument("mem://o.sav", "base_swfoc_steam_v1", originalBytes, EmptyRoot);
        var edited = new SaveDocument("mem://e.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);

        var pack = await service.ExportAsync(original, edited, "base_swfoc");
        pack.Should().NotBeNull();
        pack.Metadata.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenPathIsNull()
    {
        var service = CreateService();
        var act = () => service.LoadPackAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenFileNotFound()
    {
        var service = CreateService();
        var fakePath = Path.Join(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.json");
        var act = () => service.LoadPackAsync(fakePath);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingMetadata()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync("{}");
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMetadataNotObject()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync("""{"metadata": "not_object", "operations": []}""");
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingSchemaVersion()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": { "createdAtUtc": "2026-01-01T00:00:00Z" },
          "operations": []
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            var ex = await act.Should().ThrowAsync<InvalidDataException>();
            ex.Which.Message.Should().Contain("schemaVersion");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingCreatedAtUtc()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": { "schemaVersion": "1.0" },
          "operations": []
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            var ex = await act.Should().ThrowAsync<InvalidDataException>();
            ex.Which.Message.Should().Contain("createdAtUtc");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingOperations()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": { "schemaVersion": "1.0", "createdAtUtc": "2026-01-01T00:00:00Z" }
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            var ex = await act.Should().ThrowAsync<InvalidDataException>();
            ex.Which.Message.Should().Contain("operations");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectOperationNotObject()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": { "schemaVersion": "1.0", "createdAtUtc": "2026-01-01T00:00:00Z" },
          "operations": [ "not_an_object" ]
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            var ex = await act.Should().ThrowAsync<InvalidDataException>();
            ex.Which.Message.Should().Contain("operations[0]");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectOperationMissingFields()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": { "schemaVersion": "1.0", "createdAtUtc": "2026-01-01T00:00:00Z" },
          "operations": [ {} ]
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            var ex = await act.Should().ThrowAsync<InvalidDataException>();
            ex.Which.Message.Should().Contain("kind");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectWrongSchemaVersion()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(schemaVersion: "2.0"));
        try
        {
            var act = () => service.LoadPackAsync(path);
            var ex = await act.Should().ThrowAsync<InvalidDataException>();
            ex.Which.Message.Should().Contain("schemaVersion");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingProfileId()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(profileId: ""));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingSchemaId()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(schemaId: ""));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingSourceHash()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(sourceHash: ""));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectDefaultCreatedAtUtc()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(createdAtUtc: "0001-01-01T00:00:00+00:00"));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectNullCompatibility()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": {
            "schemaVersion": "1.0",
            "profileId": "p",
            "schemaId": "s",
            "sourceHash": "h",
            "createdAtUtc": "2026-01-01T00:00:00Z"
          },
          "operations": [
            { "kind": "SetValue", "fieldPath": "/a", "fieldId": "a", "valueType": "int32", "newValue": 1, "offset": 0 }
          ]
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectEmptyAllowedProfileIds()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": {
            "schemaVersion": "1.0",
            "profileId": "p",
            "schemaId": "s",
            "sourceHash": "h",
            "createdAtUtc": "2026-01-01T00:00:00Z"
          },
          "compatibility": {
            "allowedProfileIds": [],
            "requiredSchemaId": "s"
          },
          "operations": [
            { "kind": "SetValue", "fieldPath": "/a", "fieldId": "a", "valueType": "int32", "newValue": 1, "offset": 0 }
          ]
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectMissingRequiredSchemaId()
    {
        var service = CreateService();
        var json = """
        {
          "metadata": {
            "schemaVersion": "1.0",
            "profileId": "p",
            "schemaId": "s",
            "sourceHash": "h",
            "createdAtUtc": "2026-01-01T00:00:00Z"
          },
          "compatibility": {
            "allowedProfileIds": ["p"],
            "requiredSchemaId": ""
          },
          "operations": [
            { "kind": "SetValue", "fieldPath": "/a", "fieldId": "a", "valueType": "int32", "newValue": 1, "offset": 0 }
          ]
        }
        """;
        var path = await WriteTempJsonAsync(json);
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectOperationWithEmptyFieldPath()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(opFieldPath: ""));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectOperationWithEmptyFieldId()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(opFieldId: ""));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectOperationWithEmptyValueType()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(opValueType: ""));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldRejectOperationWithNegativeOffset()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson(opOffset: -1));
        try
        {
            var act = () => service.LoadPackAsync(path);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadPackAsync_Overload_ShouldDelegate()
    {
        var service = CreateService();
        var path = await WriteTempJsonAsync(MakeFullPatchJson());
        try
        {
            var pack = await service.LoadPackAsync(path);
            pack.Should().NotBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldThrow_WhenPackIsNull()
    {
        var service = CreateService();
        var act = () => service.ValidateCompatibilityAsync(null!, MakeDoc(), "p");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldThrow_WhenTargetDocIsNull()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var act = () => service.ValidateCompatibilityAsync(pack, null!, "p");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldThrow_WhenTargetProfileIdIsNull()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var act = () => service.ValidateCompatibilityAsync(pack, MakeDoc(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldRejectUnsupportedSchemaVersion()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var badPack = pack with { Metadata = pack.Metadata with { SchemaVersion = "99.0" } };

        var result = await service.ValidateCompatibilityAsync(badPack, MakeDoc(), "base_swfoc");
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("schemaVersion"));
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldAllowWildcardProfile()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var wildcardPack = pack with
        {
            Compatibility = new SavePatchCompatibility(
                AllowedProfileIds: ["*"],
                RequiredSchemaId: pack.Compatibility.RequiredSchemaId)
        };

        var result = await service.ValidateCompatibilityAsync(wildcardPack, MakeDoc(), "any_profile");
        result.Errors.Should().NotContain(x => x.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldRejectProfileNotInList()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var restrictedPack = pack with
        {
            Compatibility = new SavePatchCompatibility(
                AllowedProfileIds: ["only_this"],
                RequiredSchemaId: pack.Compatibility.RequiredSchemaId)
        };

        var result = await service.ValidateCompatibilityAsync(restrictedPack, MakeDoc(), "other_profile");
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldHandleNullAllowedProfileIds()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var nullProfilesPack = pack with
        {
            Compatibility = new SavePatchCompatibility(
                AllowedProfileIds: null!,
                RequiredSchemaId: pack.Compatibility.RequiredSchemaId)
        };

        var result = await service.ValidateCompatibilityAsync(nullProfilesPack, MakeDoc(), "any");
        result.IsCompatible.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldWarnOnSourceHashMismatch()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var differentBytes = new byte[300_000];
        differentBytes[0] = 0xFF;
        var differentTarget = new SaveDocument("mem://t.sav", "base_swfoc_steam_v1", differentBytes, EmptyRoot);

        var result = await service.ValidateCompatibilityAsync(pack, differentTarget, "base_swfoc");
        result.Warnings.Should().Contain(x => x.Contains("hash", StringComparison.OrdinalIgnoreCase));
        result.SourceHashMatches.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldReportMatchingSourceHash()
    {
        var service = CreateService();
        var bytes = new byte[300_000];
        var original = new SaveDocument("mem://o.sav", "base_swfoc_steam_v1", bytes, EmptyRoot);
        var editedBytes = bytes.ToArray();
        WriteInt32LE(editedBytes, 6144, 42);
        var edited = new SaveDocument("mem://e.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);
        var pack = await service.ExportAsync(original, edited, "base_swfoc");

        var result = await service.ValidateCompatibilityAsync(pack, original, "base_swfoc");
        result.SourceHashMatches.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldThrowOnCancellation()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = () => service.ValidateCompatibilityAsync(pack, MakeDoc(), "p", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_Overload_ShouldDelegate()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var result = await service.ValidateCompatibilityAsync(pack, MakeDoc(), "base_swfoc");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldThrow_WhenPackIsNull()
    {
        var service = CreateService();
        var act = () => service.PreviewApplyAsync(null!, MakeDoc(), "p");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldThrow_WhenTargetDocIsNull()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var act = () => service.PreviewApplyAsync(pack, null!, "p");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldThrow_WhenTargetProfileIdIsNull()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var act = () => service.PreviewApplyAsync(pack, MakeDoc(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldRejectUnsupportedOperationKind()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var badKindPack = pack with
        {
            Operations = [new SavePatchOperation(
                (SavePatchOperationKind)999,
                "/economy/credits_empire",
                "credits_empire",
                "int32",
                0,
                42,
                6144)]
        };

        var preview = await service.PreviewApplyAsync(badKindPack, MakeDoc(), "base_swfoc");
        preview.IsCompatible.Should().BeFalse();
        preview.Errors.Should().Contain(x => x.Contains("Unsupported operation kind"));
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldRejectNullNewValue()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var nullValuePack = pack with
        {
            Operations = [new SavePatchOperation(
                SavePatchOperationKind.SetValue,
                "/economy/credits_empire",
                "credits_empire",
                "int32",
                0,
                null,
                6144)]
        };

        var preview = await service.PreviewApplyAsync(nullValuePack, MakeDoc(), "base_swfoc");
        preview.IsCompatible.Should().BeFalse();
        preview.Errors.Should().Contain(x => x.Contains("missing required newValue"));
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldErrorWhenFieldNotFound()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var noFieldPack = pack with
        {
            Operations = [new SavePatchOperation(
                SavePatchOperationKind.SetValue,
                "/nonexistent/path",
                "nonexistent_id",
                "int32",
                0,
                42,
                0)]
        };

        var preview = await service.PreviewApplyAsync(noFieldPack, MakeDoc(), "base_swfoc");
        preview.IsCompatible.Should().BeFalse();
        preview.Errors.Should().Contain(x => x.Contains("Field not found"));
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldFallbackToFieldPath_WhenFieldIdMissing()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var fallbackPack = pack with
        {
            Operations = [new SavePatchOperation(
                SavePatchOperationKind.SetValue,
                "/economy/credits_empire",
                "nonexistent_id",
                "int32",
                0,
                42,
                6144)]
        };

        var preview = await service.PreviewApplyAsync(fallbackPack, MakeDoc(), "base_swfoc");
        preview.Warnings.Should().Contain(x => x.Contains("Falling back to fieldPath"));
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldSkipOperation_WhenValueAlreadyMatches()
    {
        var service = CreateService();
        var bytes = new byte[300_000];
        WriteInt32LE(bytes, 6144, 42);
        var target = new SaveDocument("mem://t.sav", "base_swfoc_steam_v1", bytes, EmptyRoot);
        var pack = await CreateMinimalPackAsync(service);
        var sameValuePack = pack with
        {
            Operations = [new SavePatchOperation(
                SavePatchOperationKind.SetValue,
                "/economy/credits_empire",
                "credits_empire",
                "int32",
                0,
                42,
                6144)]
        };

        var preview = await service.PreviewApplyAsync(sameValuePack, target, "base_swfoc");
        preview.OperationsToApply.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewApplyAsync_Overload_ShouldDelegate()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var preview = await service.PreviewApplyAsync(pack, MakeDoc(), "base_swfoc");
        preview.Should().NotBeNull();
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldWarnWhenFieldPathDiffersFromCanonical()
    {
        var service = CreateService();
        var pack = await CreateMinimalPackAsync(service);
        var wrongPathPack = pack with
        {
            Operations = pack.Operations.Select(op =>
                op with { FieldPath = "/wrong/canonical/path" }).ToArray()
        };

        var preview = await service.PreviewApplyAsync(wrongPathPack, MakeDoc(), "base_swfoc");
        preview.Warnings.Should().Contain(x => x.Contains("Field path mismatch"));
    }

    private static SavePatchPackService CreateService()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new SaveOptions
        {
            SchemaRootPath = Path.Join(root, "profiles", "default", "schemas")
        };
        return new SavePatchPackService(options);
    }

    private static SaveDocument MakeDoc(string schemaId = "base_swfoc_steam_v1")
        => new("mem://test.sav", schemaId, new byte[300_000], EmptyRoot);

    private static async Task<SavePatchPack> CreateMinimalPackAsync(SavePatchPackService service)
    {
        var bytes = new byte[300_000];
        var editedBytes = bytes.ToArray();
        WriteInt32LE(editedBytes, 6144, 42);
        var original = new SaveDocument("mem://o.sav", "base_swfoc_steam_v1", bytes, EmptyRoot);
        var edited = new SaveDocument("mem://e.sav", "base_swfoc_steam_v1", editedBytes, EmptyRoot);
        return await service.ExportAsync(original, edited, "base_swfoc");
    }

    private static void WriteInt32LE(byte[] bytes, int offset, int value)
    {
        var buf = BitConverter.GetBytes(value);
        Array.Copy(buf, 0, bytes, offset, 4);
    }

    private static async Task<string> WriteTempJsonAsync(string json)
    {
        var path = Path.Join(Path.GetTempPath(), $"swfoc-pack-test-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private static string MakeFullPatchJson(
        string schemaVersion = "1.0",
        string profileId = "base_swfoc",
        string schemaId = "base_swfoc_steam_v1",
        string sourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string createdAtUtc = "2026-02-17T00:00:00Z",
        string opFieldPath = "/economy/credits_empire",
        string opFieldId = "credits_empire",
        string opValueType = "int32",
        int opOffset = 6144)
    {
        return $$"""
        {
          "metadata": {
            "schemaVersion": "{{schemaVersion}}",
            "profileId": "{{profileId}}",
            "schemaId": "{{schemaId}}",
            "sourceHash": "{{sourceHash}}",
            "createdAtUtc": "{{createdAtUtc}}"
          },
          "compatibility": {
            "allowedProfileIds": ["{{profileId}}"],
            "requiredSchemaId": "{{schemaId}}"
          },
          "operations": [
            {
              "kind": "SetValue",
              "fieldPath": "{{opFieldPath}}",
              "fieldId": "{{opFieldId}}",
              "valueType": "{{opValueType}}",
              "oldValue": 0,
              "newValue": 42,
              "offset": {{opOffset}}
            }
          ]
        }
        """;
    }
}
