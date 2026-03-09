using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchPackServiceGapCoverageTests
{
    private static readonly SaveNode EmptyRoot = new("/", "Root", "root", null, Array.Empty<SaveNode>());

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldAllowWildcardProfiles_ButRejectUnsupportedSchemaVersion()
    {
        var service = CreateService();
        var bytes = CreateSyntheticBytes();
        var sourceHash = ComputeSha256Hex(bytes);
        var target = new SaveDocument("mem://target.sav", "base_swfoc_steam_v1", bytes, EmptyRoot);
        var pack = new SavePatchPack(
            new SavePatchMetadata("2.0", "base_swfoc", "base_swfoc_steam_v1", sourceHash, DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["*"], "base_swfoc_steam_v1"),
            Array.Empty<SavePatchOperation>());

        var result = await service.ValidateCompatibilityAsync(pack, target, "custom_profile", CancellationToken.None);

        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.Contains("Unsupported patch-pack schemaVersion", StringComparison.OrdinalIgnoreCase));
        result.SourceHashMatches.Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_ShouldRejectBlankProfileId()
    {
        var service = CreateService();
        var original = new SaveDocument("mem://original.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var edited = new SaveDocument("mem://edited.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);

        var act = () => service.ExportAsync(original, edited, "   ", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Profile ID is required*");
    }

    [Fact]
    public async Task ExportAsync_ShouldRejectMismatchedSchemaIds()
    {
        var service = CreateService();
        var original = new SaveDocument("mem://original.sav", "schema_a", CreateSyntheticBytes(), EmptyRoot);
        var edited = new SaveDocument("mem://edited.sav", "schema_b", CreateSyntheticBytes(), EmptyRoot);

        var act = () => service.ExportAsync(original, edited, "base_swfoc", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*same schema ID*");
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldFallbackToFieldPath_AndSkipNoOpOperations()
    {
        var service = CreateService();
        var target = new SaveDocument("mem://target.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var pack = new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "base_swfoc_steam_v1", "0".PadLeft(64, 'a'), DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["base_swfoc"], "base_swfoc_steam_v1"),
            [
                new SavePatchOperation(SavePatchOperationKind.SetValue, "/economy/credits_empire", "missing_empire_id", "int32", 0, 0, 6144),
                new SavePatchOperation(SavePatchOperationKind.SetValue, "/economy/credits_rebel", "missing_rebel_id", "int32", 0, 4321, 6148)
            ]);

        var preview = await service.PreviewApplyAsync(pack, target, "base_swfoc", CancellationToken.None);

        preview.Errors.Should().BeEmpty();
        preview.Warnings.Should().Contain(x => x.Contains("Falling back to fieldPath", StringComparison.OrdinalIgnoreCase));
        preview.OperationsToApply.Should().ContainSingle();
        preview.OperationsToApply[0].FieldPath.Should().Be("/economy/credits_rebel");
        preview.OperationsToApply[0].NewValue.Should().Be(4321);
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldReportUnsupportedKinds_AndMissingFields()
    {
        var service = CreateService();
        var target = new SaveDocument("mem://target.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var pack = new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "base_swfoc_steam_v1", "0".PadLeft(64, 'b'), DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["base_swfoc"], "base_swfoc_steam_v1"),
            [
                new SavePatchOperation((SavePatchOperationKind)99, "/economy/credits_empire", "credits_empire", "int32", 0, 99, 6144),
                new SavePatchOperation(SavePatchOperationKind.SetValue, "/economy/ghost", "ghost_field", "int32", 0, 12, 7000)
            ]);

        var preview = await service.PreviewApplyAsync(pack, target, "base_swfoc", CancellationToken.None);

        preview.IsCompatible.Should().BeFalse();
        preview.Errors.Should().Contain(x => x.Contains("Unsupported operation kind", StringComparison.OrdinalIgnoreCase));
        preview.Errors.Should().Contain(x => x.Contains("Field not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadPackAsync_ShouldAcceptCaseInsensitiveContractProperties()
    {
        var service = CreateService();
        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-case-pack-{Guid.NewGuid():N}.json");

        try
        {
            var json = """
            {
              "MetaData": {
                "SchemaVersion": "1.0",
                "ProfileId": "base_swfoc",
                "SchemaId": "base_swfoc_steam_v1",
                "SourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "CreatedAtUtc": "2026-03-01T00:00:00Z"
              },
              "Compatibility": {
                "AllowedProfileIds": ["base_swfoc"],
                "RequiredSchemaId": "base_swfoc_steam_v1"
              },
              "Operations": [
                {
                  "Kind": "SetValue",
                  "FieldPath": "/economy/credits_empire",
                  "FieldId": "credits_empire",
                  "ValueType": "int32",
                  "OldValue": 0,
                  "NewValue": 2222,
                  "Offset": 6144
                }
              ]
            }
            """;

            await File.WriteAllTextAsync(tempPath, json);
            var pack = await service.LoadPackAsync(tempPath, CancellationToken.None);

            pack.Metadata.ProfileId.Should().Be("base_swfoc");
            pack.Operations.Should().ContainSingle();
            pack.Operations[0].NewValue.Should().Be(2222);
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
    public async Task LoadPackAsync_ShouldRejectNullNewValueInRawContract()
    {
        var service = CreateService();
        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-invalid-pack-{Guid.NewGuid():N}.json");

        try
        {
            var json = """
            {
              "metadata": {
                "schemaVersion": "1.0",
                "profileId": "base_swfoc",
                "schemaId": "base_swfoc_steam_v1",
                "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "createdAtUtc": "2026-03-01T00:00:00Z"
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
                  "oldValue": 0,
                  "newValue": null,
                  "offset": 6144
                }
              ]
            }
            """;

            await File.WriteAllTextAsync(tempPath, json);
            var act = () => service.LoadPackAsync(tempPath, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*operations[0].newValue cannot be null*");
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
    public async Task ValidateCompatibilityAsync_ShouldReportSchemaAndProfileMismatch_WithHashWarning()
    {
        var service = CreateService();
        var target = new SaveDocument("mem://target.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var pack = new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "other_schema", "0".PadLeft(64, 'c'), DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["other_profile"], "other_schema"),
            Array.Empty<SavePatchOperation>());

        var result = await service.ValidateCompatibilityAsync(pack, target, "base_swfoc", CancellationToken.None);

        result.IsCompatible.Should().BeFalse();
        result.SourceHashMatches.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("Schema mismatch", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(x => x.Contains("Profile mismatch", StringComparison.OrdinalIgnoreCase));
        result.Warnings.Should().ContainSingle(x => x.Contains("Source hash mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldHonorCancelledToken()
    {
        var service = CreateService();
        var target = new SaveDocument("mem://target.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var pack = new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "base_swfoc_steam_v1", "0".PadLeft(64, 'f'), DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["base_swfoc"], "base_swfoc_steam_v1"),
            Array.Empty<SavePatchOperation>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.ValidateCompatibilityAsync(pack, target, "base_swfoc", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PreviewApplyAsync_ShouldReportMissingNewValue()
    {
        var service = CreateService();
        var target = new SaveDocument("mem://target.sav", "base_swfoc_steam_v1", CreateSyntheticBytes(), EmptyRoot);
        var pack = new SavePatchPack(
            new SavePatchMetadata("1.0", "base_swfoc", "base_swfoc_steam_v1", "0".PadLeft(64, 'd'), DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["base_swfoc"], "base_swfoc_steam_v1"),
            [
                new SavePatchOperation(SavePatchOperationKind.SetValue, "/economy/credits_empire", "credits_empire", "int32", 0, null, 6144)
            ]);

        var preview = await service.PreviewApplyAsync(pack, target, "base_swfoc", CancellationToken.None);

        preview.IsCompatible.Should().BeFalse();
        preview.Errors.Should().ContainSingle(x => x.Contains("missing required newValue", StringComparison.OrdinalIgnoreCase));
        preview.OperationsToApply.Should().BeEmpty();
    }

    [Fact]
    public void PrivateContractHelpers_ShouldHandleCaseInsensitiveProperties_AndOperationValidation()
    {
        using var doc = JsonDocument.Parse("""
        {
          "FIELDID": "credits_empire",
          "fieldPath": "/economy/credits_empire",
          "valueType": "int32"
        }
        """);
        var errors = new List<string>();

        object?[] tryGetArgs = [doc.RootElement, "fieldid", default(JsonElement)];
        var found = InvokePrivateStatic("TryGetPropertyIgnoreCase", tryGetArgs);
        found.Should().BeOfType<bool>().Which.Should().BeTrue();
        ((JsonElement)tryGetArgs[2]!).GetString().Should().Be("credits_empire");

        var operations = new[]
        {
            new SavePatchOperation((SavePatchOperationKind)7, "", "", "", null, null, -1)
        };
        InvokePrivateStatic("ValidateOperationContracts", operations, errors);

        errors.Should().Contain(x => x.Contains("fieldPath is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("fieldId is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("valueType is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("kind must be SetValue", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("offset must be >= 0", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("newValue is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrivateContractHelpers_ShouldValidateMetadataCompatibilityAndRawPackShapes()
    {
        var errors = new List<string>();

        InvokePrivateStatic(
            "ValidateMetadataContract",
            new SavePatchMetadata(string.Empty, string.Empty, string.Empty, string.Empty, default),
            errors);

        errors.Should().Contain(x => x.Contains("metadata.schemaVersion is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("metadata.profileId is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("metadata.schemaId is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("metadata.sourceHash is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("metadata.createdAtUtc is required", StringComparison.OrdinalIgnoreCase));

        errors.Clear();
        InvokePrivateStatic("ValidateCompatibilityContract", null, errors);
        InvokePrivateStatic("ValidateCompatibilityContract", new SavePatchCompatibility(Array.Empty<string>(), string.Empty), errors);

        errors.Should().Contain(x => x.Contains("compatibility is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("compatibility.requiredSchemaId is required", StringComparison.OrdinalIgnoreCase));
        errors.Should().Contain(x => x.Contains("compatibility.allowedProfileIds must contain at least one value", StringComparison.OrdinalIgnoreCase));

        using var nonArrayDoc = JsonDocument.Parse("""
        {
          "metadata": {
            "schemaVersion": "1.0",
            "createdAtUtc": "2026-03-01T00:00:00Z"
          },
          "operations": {}
        }
        """);
        var nonArrayErrors = (IReadOnlyList<string>)InvokePrivateStatic("ValidateRawPackContract", nonArrayDoc.RootElement)!;
        nonArrayErrors.Should().Contain("operations is required");

        using var mixedDoc = JsonDocument.Parse("""
        {
          "metadata": {
            "schemaVersion": "1.0",
            "createdAtUtc": "2026-03-01T00:00:00Z"
          },
          "operations": [1]
        }
        """);
        var mixedErrors = (IReadOnlyList<string>)InvokePrivateStatic("ValidateRawPackContract", mixedDoc.RootElement)!;
        mixedErrors.Should().Contain("operations[0] must be an object");
    }

    [Fact]
    public void PrivateExportHelpers_ShouldSkipChecksumOverlapFields()
    {
        var schema = new SaveSchema(
            "schema",
            "build",
            "little",
            new[] { new SaveBlockDefinition("root", "Root", 0, 16, "struct", new[] { "credits", "checksum" }) },
            new[]
            {
                new SaveFieldDefinition("credits", "Credits", "int32", 0, 4, Path: "/economy/credits"),
                new SaveFieldDefinition("checksum", "Checksum", "int32", 4, 4, Path: "/meta/checksum")
            },
            Array.Empty<SaveArrayDefinition>(),
            Array.Empty<ValidationRule>(),
            new[] { new ChecksumRule("crc", "crc32", 0, 4, 4, 4) });
        var original = new byte[8];
        var edited = new byte[8];
        BitConverter.GetBytes(9000).CopyTo(edited, 0);
        BitConverter.GetBytes(1234).CopyTo(edited, 4);

        var operations = (IReadOnlyList<SavePatchOperation>)InvokePrivateStatic("BuildExportOperations", schema, original, edited)!;

        operations.Should().ContainSingle();
        operations[0].FieldId.Should().Be("credits");
        operations[0].Offset.Should().Be(0);
    }

    private static SavePatchPackService CreateService()
    {
        var root = TestPaths.FindRepoRoot();
        return new SavePatchPackService(new SaveOptions
        {
            SchemaRootPath = Path.Combine(root, "profiles", "default", "schemas")
        });
    }

    private static byte[] CreateSyntheticBytes() => new byte[300_000];

    private static string ComputeSha256Hex(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(SavePatchPackService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected SavePatchPackService private static method {methodName}");
        return method!.Invoke(null, args);
    }
}
