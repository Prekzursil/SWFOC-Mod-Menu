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
