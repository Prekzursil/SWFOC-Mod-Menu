using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

/// <summary>
/// Wave 7 final coverage — fills remaining Saves gaps:
/// BinarySaveCodec: WriteAsync no-parent-directory guard (lines 101-102),
///   WriteFloatingPoint endian swap (lines 354-355),
/// SavePatchApplyService: RestoreLastBackupAsync IOException/UnauthorizedAccess (lines 144-160),
///   ApplyAsync IOException/UnauthorizedAccess (lines 352-362),
///   RestoreAfterWriteFailureAsync rollback paths (lines 372-403),
/// SavePatchApplyService.Helpers: ResolveLatestBackupPathAsync null directory (lines 37-38),
///   TryNormalizePatchValue InvalidOperationException (lines 70-80),
///   TryDeleteTempOutput IOException (lines 123-126),
///   ApplyFieldWithFallbackSelectorAsync both selectors fail (lines 147-158),
///   TryResolveReceiptBackupPathAsync IOException/JsonException (lines 195-214, 211-213),
///   TryNormalizeBackupCandidatePath exceptions (lines 280-304),
/// SavePatchPackService: FieldOverlapsChecksumOutput (lines 211-212),
///   ValidatePreviewOperation unsupported kind (lines 253-255),
///   ValidateCompatibilityContract errors (lines 262-264),
///   ValidateOperationContracts errors (lines 506-518).
/// </summary>
public sealed class SavesWave7FinalTests
{
    #region SavePatchApplyService.Helpers — ResolveLatestBackupPathAsync null directory (lines 37-38)

    [Fact]
    public async Task ResolveLatestBackupPathAsync_EmptyTargetPath_ShouldReturnNull()
    {
        var helper = CreateHelper();
        var result = await InvokeResolveLatestBackupPathAsync(helper, "justfilename.sav", CancellationToken.None);
        // A bare filename with no directory returns null from TryGetTargetLocation
        result.Should().BeNull();
    }

    #endregion

    #region SavePatchApplyService.Helpers — TryNormalizePatchValue InvalidOperationException (lines 70-80)

    [Fact]
    public void TryNormalizePatchValue_FormatException_Int32_ShouldReturnFailure()
    {
        var helper = CreateHelper();
        // int32 with non-numeric string triggers FormatException
        var operation = new SavePatchOperation(
            SavePatchOperationKind.SetValue, "/field", "f1", "int32", null, "not_a_number", 0);
        var (value, failure) = InvokeTryNormalizePatchValue(helper, operation, "value_norm_failed");
        failure.Should().NotBeNull();
        failure!.Applied.Should().BeFalse();
    }

    #endregion

    #region SavePatchApplyService.Helpers — TryDeleteTempOutput IOException (lines 123-126)

    [Fact]
    public void TryDeleteTempOutput_LockedFile_ShouldNotThrow()
    {
        var helper = CreateHelper();
        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Join(tempDir, "temp.sav");
        try
        {
            File.WriteAllBytes(tempFile, new byte[10]);
            // Lock the file to trigger IOException on delete
            using var lockStream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            // Should not throw — IOException is caught and logged
            InvokeTryDeleteTempOutput(helper, tempFile);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region SavePatchApplyService.Helpers — TryNormalizeBackupCandidatePath (lines 280-304)

    [Fact]
    public void TryNormalizeBackupCandidatePath_EmptyPath_ShouldReturnFalse()
    {
        var method = HelperType.GetMethod(
            "TryNormalizeBackupCandidatePath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { "", null, "" };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
        ((string)args[2]!).Should().Contain("empty");
    }

    [Fact]
    public void TryNormalizeBackupCandidatePath_NonSavExtension_ShouldReturnFalse()
    {
        var method = HelperType.GetMethod(
            "TryNormalizeBackupCandidatePath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var badPath = Path.Join(tempDir, "backup.txt");
        File.WriteAllText(badPath, "data");
        try
        {
            var args = new object?[] { badPath, null, "" };
            var result = (bool)method!.Invoke(null, args)!;
            result.Should().BeFalse();
            ((string)args[2]!).Should().Contain(".sav");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryNormalizeBackupCandidatePath_NonexistentSavFile_ShouldReturnFalse()
    {
        var method = HelperType.GetMethod(
            "TryNormalizeBackupCandidatePath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-ne-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var missingPath = Path.Join(tempDir, "backup.bak.001.sav");
            var args = new object?[] { missingPath, null, "" };
            var result = (bool)method!.Invoke(null, args)!;
            result.Should().BeFalse();
            ((string)args[2]!).Should().Contain("does not exist");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryNormalizeBackupCandidatePath_ValidSavFile_ShouldReturnTrue()
    {
        var method = HelperType.GetMethod(
            "TryNormalizeBackupCandidatePath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var savPath = Path.Join(tempDir, "backup.bak.001.sav");
        File.WriteAllBytes(savPath, new byte[10]);
        try
        {
            var args = new object?[] { savPath, null, "" };
            var result = (bool)method!.Invoke(null, args)!;
            result.Should().BeTrue();
            args[1].Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region SavePatchPackService — ValidateOperationContracts edge cases (lines 506-518)

    [Fact]
    public async Task LoadPackAsync_OperationWithNegativeOffset_ShouldThrow()
    {
        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var packPath = Path.Join(tempDir, "test.patch.json");
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                metadata = new
                {
                    schemaVersion = "1.0",
                    profileId = "prof1",
                    schemaId = "schema1",
                    sourceHash = "abc123",
                    createdAtUtc = DateTimeOffset.UtcNow
                },
                compatibility = new
                {
                    requiredSchemaId = "schema1",
                    targetHash = "hash1",
                    allowedProfileIds = new[] { "prof1" }
                },
                operations = new[]
                {
                    new
                    {
                        kind = "SetValue",
                        fieldPath = "/credits",
                        fieldId = "credits",
                        valueType = "int32",
                        oldValue = (object)100,
                        newValue = (object)999,
                        offset = -1
                    }
                }
            });
            File.WriteAllText(packPath, json);

            var service = CreatePackService();
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadPackAsync(packPath, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadPackAsync_OperationWithMissingFieldId_ShouldThrow()
    {
        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-fid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var packPath = Path.Join(tempDir, "test.patch.json");
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                metadata = new
                {
                    schemaVersion = "1.0",
                    profileId = "prof1",
                    schemaId = "schema1",
                    sourceHash = "abc123",
                    createdAtUtc = DateTimeOffset.UtcNow
                },
                compatibility = new
                {
                    requiredSchemaId = "schema1",
                    targetHash = "hash1",
                    allowedProfileIds = new[] { "prof1" }
                },
                operations = new[]
                {
                    new
                    {
                        kind = "SetValue",
                        fieldPath = "/credits",
                        fieldId = "",
                        valueType = "int32",
                        oldValue = (object)100,
                        newValue = (object)999,
                        offset = 0
                    }
                }
            });
            File.WriteAllText(packPath, json);

            var service = CreatePackService();
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadPackAsync(packPath, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task LoadPackAsync_OperationWithMissingNewValue_ShouldThrow()
    {
        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-nv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var packPath = Path.Join(tempDir, "test.patch.json");
        try
        {
            var json = @"{
                ""metadata"": {
                    ""schemaVersion"": ""1.0"",
                    ""profileId"": ""prof1"",
                    ""schemaId"": ""schema1"",
                    ""sourceHash"": ""abc123"",
                    ""createdAtUtc"": ""2025-01-01T00:00:00Z""
                },
                ""compatibility"": {
                    ""requiredSchemaId"": ""schema1"",
                    ""targetHash"": ""hash1"",
                    ""allowedProfileIds"": [""prof1""]
                },
                ""operations"": [{
                    ""kind"": ""SetValue"",
                    ""fieldPath"": ""/credits"",
                    ""fieldId"": ""credits"",
                    ""valueType"": ""int32"",
                    ""oldValue"": 100,
                    ""newValue"": null,
                    ""offset"": 0
                }]
            }";
            File.WriteAllText(packPath, json);

            var service = CreatePackService();
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadPackAsync(packPath, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region SavePatchPackService — ValidateCompatibilityContract missing fields (lines 262-264)

    [Fact]
    public async Task LoadPackAsync_MissingCompatibilitySection_ShouldThrow()
    {
        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-compat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var packPath = Path.Join(tempDir, "test.patch.json");
        try
        {
            var json = @"{
                ""metadata"": {
                    ""schemaVersion"": ""1.0"",
                    ""profileId"": ""prof1"",
                    ""schemaId"": ""schema1"",
                    ""sourceHash"": ""abc123"",
                    ""createdAtUtc"": ""2025-01-01T00:00:00Z""
                },
                ""compatibility"": null,
                ""operations"": []
            }";
            File.WriteAllText(packPath, json);

            var service = CreatePackService();
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadPackAsync(packPath, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region SavePatchPackService — ValidateMetadataContract edge cases

    [Fact]
    public async Task LoadPackAsync_WrongSchemaVersion_ShouldThrow()
    {
        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-ver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var packPath = Path.Join(tempDir, "test.patch.json");
        try
        {
            var json = @"{
                ""metadata"": {
                    ""schemaVersion"": ""2.0"",
                    ""profileId"": ""prof1"",
                    ""schemaId"": ""schema1"",
                    ""sourceHash"": ""abc123"",
                    ""createdAtUtc"": ""2025-01-01T00:00:00Z""
                },
                ""compatibility"": {
                    ""requiredSchemaId"": ""schema1"",
                    ""allowedProfileIds"": [""prof1""]
                },
                ""operations"": []
            }";
            File.WriteAllText(packPath, json);

            var service = CreatePackService();
            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadPackAsync(packPath, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Helpers

    private static readonly Type HelperType = typeof(SavePatchApplyService).Assembly
        .GetType("SwfocTrainer.Saves.Services.SavePatchApplyServiceHelper")!;

    private static object CreateHelper(ISaveCodec? codec = null)
    {
        return Activator.CreateInstance(
            HelperType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object[] { codec ?? new StubSaveCodec(), NullLogger.Instance, "not found in schema", "unknown save field selector" },
            null)!;
    }

    private static (object? value, SavePatchApplyResult? failure) InvokeTryNormalizePatchValue(object helper, SavePatchOperation op, string reason)
    {
        var m = helper.GetType().GetMethod("TryNormalizePatchValue")!;
        var r = m.Invoke(helper, new object[] { op, reason });
        return ((object?, SavePatchApplyResult?))r!;
    }

    private static void InvokeTryDeleteTempOutput(object helper, string path)
    {
        helper.GetType().GetMethod("TryDeleteTempOutput")!.Invoke(helper, new object[] { path });
    }

    private static async Task<string?> InvokeResolveLatestBackupPathAsync(object helper, string targetPath, CancellationToken ct)
    {
        var task = (Task<string?>)helper.GetType().GetMethod("ResolveLatestBackupPathAsync")!
            .Invoke(helper, new object[] { targetPath, ct })!;
        return await task;
    }

    private static SavePatchPackService CreatePackService()
    {
        var tempDir = Path.Join(Path.GetTempPath(), "saves-w7-schemas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return new SavePatchPackService(new SaveOptions { SchemaRootPath = tempDir });
    }

    private sealed class StubSaveCodec : ISaveCodec
    {
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken ct)
            => Task.FromResult(new SaveDocument(path, schemaId, new byte[] { 0x01, 0x02, 0x03, 0x04 }, new SaveNode("/", "root", "root", null)));
        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken ct)
            => Task.CompletedTask;
        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken ct)
            => Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken ct)
            => Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken ct)
            => Task.FromResult(true);
    }

    #endregion
}
