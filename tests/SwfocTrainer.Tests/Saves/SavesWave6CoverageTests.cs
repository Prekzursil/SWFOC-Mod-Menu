using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavesWave6CoverageTests
{
    [Fact]
    public void Constructor_NullSaveCodec_ShouldThrow()
    {
        var act = () => new SavePatchApplyService(null!, new StubSavePatchPackService(), NullLogger<SavePatchApplyService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("saveCodec");
    }

    [Fact]
    public void Constructor_NullPatchPackService_ShouldThrow()
    {
        var act = () => new SavePatchApplyService(new StubSaveCodec(), null!, NullLogger<SavePatchApplyService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("patchPackService");
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new SavePatchApplyService(new StubSaveCodec(), new StubSavePatchPackService(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void RestoreRawSnapshot_LengthMismatch_ShouldThrow()
    {
        var method = typeof(SavePatchApplyService).GetMethod("RestoreRawSnapshot", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var act = () => method!.Invoke(null, new object[] { new byte[5], new byte[3] });
        act.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>().WithMessage("*length changed*");
    }

    [Fact]
    public void RestoreRawSnapshot_SameLength_ShouldCopyBytes()
    {
        var method = typeof(SavePatchApplyService).GetMethod("RestoreRawSnapshot", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var target = new byte[] { 0xAA, 0xBB, 0xCC };
        var snapshot = new byte[] { 0x01, 0x02, 0x03 };
        method!.Invoke(null, new object[] { target, snapshot });
        target.Should().Equal(0x01, 0x02, 0x03);
    }

    [Fact]
    public void NormalizeTargetPath_NonExistentFile_ShouldThrow()
    {
        var method = typeof(SavePatchApplyService).GetMethod("NormalizeTargetPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakePath = Path.Join(tempDir, "nonexistent.sav");
            var act = () => method!.Invoke(null, new object[] { fakePath });
            act.Should().Throw<TargetInvocationException>().WithInnerException<FileNotFoundException>();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void TryNormalizePatchValue_FormatException_ShouldReturnFailure()
    {
        var helper = CreateHelper();
        var operation = new SavePatchOperation(SavePatchOperationKind.SetValue, "/field", "f1", "int32", null, "not_a_number", 0);
        var result = InvokeTryNormalizePatchValue(helper, operation, "value_normalization_failed");
        result.failure.Should().NotBeNull();
        result.failure!.Applied.Should().BeFalse();
        result.failure.Failure!.ReasonCode.Should().Be("value_normalization_failed");
    }

    [Fact]
    public void TryNormalizePatchValue_ValidInt_ShouldReturnValue()
    {
        var helper = CreateHelper();
        var operation = new SavePatchOperation(SavePatchOperationKind.SetValue, "/field", "f1", "int32", null, 42, 0);
        var result = InvokeTryNormalizePatchValue(helper, operation, "reason");
        result.failure.Should().BeNull();
        result.value.Should().Be(42);
    }

    [Fact]
    public void TryDeleteTempOutput_FileDoesNotExist_ShouldNotThrow()
    {
        var helper = CreateHelper();
        InvokeTryDeleteTempOutput(helper, Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sav"));
    }

    [Fact]
    public void TryDeleteTempOutput_FileExists_ShouldDelete()
    {
        var helper = CreateHelper();
        var tempPath = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sav");
        File.WriteAllBytes(tempPath, new byte[] { 0x01 });
        try { InvokeTryDeleteTempOutput(helper, tempPath); File.Exists(tempPath).Should().BeFalse(); }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    [Fact]
    public async Task TryApplyOperationValueAsync_InvalidOp_ShouldReturnFailure()
    {
        var helper = CreateHelper(new ThrowingEditSaveCodec());
        var doc = new SaveDocument(@"C:\test.sav", "test", new byte[] { 0x01, 0x02, 0x03, 0x04 }, new SaveNode("/", "root", "root", null));
        var op = new SavePatchOperation(SavePatchOperationKind.SetValue, "/field", "f1", "int32", null, 42, 0);
        var result = await InvokeTryApplyOperationValueAsync(helper, doc, op, 42, "field_apply_failed", CancellationToken.None);
        result.Should().NotBeNull();
        result!.Applied.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveLatestBackupPathAsync_EmptyDir_ShouldReturnNull()
    {
        var helper = CreateHelper();
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = await InvokeResolveLatestBackupPathAsync(helper, Path.Join(tempDir, "test.sav"), CancellationToken.None);
            result.Should().BeNull();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public async Task ResolveLatestBackupPathAsync_WithBackup_ShouldReturnPath()
    {
        var helper = CreateHelper();
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllBytes(Path.Join(tempDir, "test.sav"), new byte[] { 0x01 });
            File.WriteAllBytes(Path.Join(tempDir, "test.sav.bak.20260101120000000.sav"), new byte[] { 0x02 });
            var result = await InvokeResolveLatestBackupPathAsync(helper, Path.Join(tempDir, "test.sav"), CancellationToken.None);
            result.Should().NotBeNull();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public async Task ResolveLatestBackupPathAsync_WithReceipt_ShouldReturnPath()
    {
        var helper = CreateHelper();
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var savePath = Path.Join(tempDir, "test.sav");
            var backupPath = Path.Join(tempDir, "test.sav.bak.20260101120000000.sav");
            var receiptPath = Path.Join(tempDir, "test.sav.apply-receipt.20260101120000000.json");
            File.WriteAllBytes(savePath, new byte[] { 0x01 });
            File.WriteAllBytes(backupPath, new byte[] { 0x02 });
            var json = JsonSerializer.Serialize(new { RunId = "20260101120000000", AppliedAtUtc = DateTimeOffset.UtcNow, TargetPath = savePath, BackupPath = backupPath, ReceiptPath = receiptPath, ProfileId = "test", SchemaId = "test", Classification = "Applied", SourceHash = "abc", TargetHash = "def", AppliedHash = "ghi", OperationsApplied = 1 }, SavePatchApplyService.ReceiptJsonOptions);
            await File.WriteAllTextAsync(receiptPath, json);
            var result = await InvokeResolveLatestBackupPathAsync(helper, savePath, CancellationToken.None);
            result.Should().NotBeNull();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public async Task ResolveLatestBackupPathAsync_InvalidJson_ShouldFallBack()
    {
        var helper = CreateHelper();
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllBytes(Path.Join(tempDir, "test.sav"), new byte[] { 0x01 });
            File.WriteAllBytes(Path.Join(tempDir, "test.sav.bak.20260101120000000.sav"), new byte[] { 0x02 });
            await File.WriteAllTextAsync(Path.Join(tempDir, "test.sav.apply-receipt.20260101120000000.json"), "NOT JSON");
            var result = await InvokeResolveLatestBackupPathAsync(helper, Path.Join(tempDir, "test.sav"), CancellationToken.None);
            result.Should().NotBeNull();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public async Task ResolveLatestBackupPathAsync_MissingBackupInReceipt_ShouldReturnNull()
    {
        var helper = CreateHelper();
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var savePath = Path.Join(tempDir, "test.sav");
            File.WriteAllBytes(savePath, new byte[] { 0x01 });
            var json = JsonSerializer.Serialize(new { RunId = "20260101120000000", AppliedAtUtc = DateTimeOffset.UtcNow, TargetPath = savePath, BackupPath = Path.Join(tempDir, "nonexistent.sav"), ReceiptPath = Path.Join(tempDir, "test.sav.apply-receipt.20260101120000000.json"), ProfileId = "test", SchemaId = "test", Classification = "Applied", SourceHash = "abc", TargetHash = "def", AppliedHash = "ghi", OperationsApplied = 1 }, SavePatchApplyService.ReceiptJsonOptions);
            await File.WriteAllTextAsync(Path.Join(tempDir, "test.sav.apply-receipt.20260101120000000.json"), json);
            var result = await InvokeResolveLatestBackupPathAsync(helper, savePath, CancellationToken.None);
            result.Should().BeNull();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void HelperConstructor_NullCodec_ShouldThrow()
    {
        var act = () => CreateHelperDirect(null!, NullLogger.Instance, "a", "b");
        act.Should().Throw<TargetInvocationException>().WithInnerException<ArgumentNullException>().WithMessage("*saveCodec*");
    }

    [Fact]
    public void HelperConstructor_NullLogger_ShouldThrow()
    {
        var act = () => CreateHelperDirect(new StubSaveCodec(), null!, "a", "b");
        act.Should().Throw<TargetInvocationException>().WithInnerException<ArgumentNullException>().WithMessage("*logger*");
    }

    [Fact]
    public void HelperConstructor_NullSelectorNotFound_ShouldThrow()
    {
        var act = () => CreateHelperDirect(new StubSaveCodec(), NullLogger.Instance, null!, "b");
        act.Should().Throw<TargetInvocationException>().WithInnerException<ArgumentNullException>().WithMessage("*selectorNotFoundInSchemaText*");
    }

    [Fact]
    public void HelperConstructor_NullSelectorUnknown_ShouldThrow()
    {
        var act = () => CreateHelperDirect(new StubSaveCodec(), NullLogger.Instance, "a", null!);
        act.Should().Throw<TargetInvocationException>().WithInnerException<ArgumentNullException>().WithMessage("*selectorUnknownFieldText*");
    }

    [Fact]
    public async Task ApplyAsync_IOException_ShouldReturnTargetLoadFailed()
    {
        await RunApplyTestAsync(new ThrowingLoadSaveCodec(new IOException("read error")), new StubSavePatchPackService(), BuildEmptyPack(), r => r.Failure!.ReasonCode.Should().Be("target_load_failed"));
    }

    [Fact]
    public async Task ApplyAsync_InvalidOp_ShouldReturnTargetLoadFailed()
    {
        await RunApplyTestAsync(new ThrowingLoadSaveCodec(new InvalidOperationException("schema error")), new StubSavePatchPackService(), BuildEmptyPack(), r => r.Failure!.ReasonCode.Should().Be("target_load_failed"));
    }

    [Fact]
    public async Task ApplyAsync_CompatibilityFailed_ShouldReturnFailure()
    {
        await RunApplyTestAsync(new StubSaveCodec(), new StubSavePatchPackService(isCompatible: false), BuildEmptyPack(), r => r.Failure!.ReasonCode.Should().Be("compatibility_failed"));
    }

    [Fact]
    public async Task ApplyAsync_StrictHashMismatch_ShouldReturnFailure()
    {
        await RunApplyTestAsync(new StubSaveCodec(), new StubSavePatchPackService(sourceHashMatches: false), BuildEmptyPack(), r => r.Failure!.ReasonCode.Should().Be("source_hash_mismatch"), strict: true);
    }

    [Fact]
    public async Task ApplyAsync_UnsupportedKind_ShouldReturnFailure()
    {
        var pack = new SavePatchPack(new SavePatchMetadata("v1", "test", "test", "hash", DateTimeOffset.UtcNow), new SavePatchCompatibility(new[] { "test" }, "test", null), new[] { new SavePatchOperation((SavePatchOperationKind)999, "/f", "f1", "int32", null, 42, 0) });
        await RunApplyTestAsync(new StubSaveCodec(), new StubSavePatchPackService(), pack, r => r.Failure!.ReasonCode.Should().Be("unsupported_operation_kind"), strict: false);
    }

    [Fact]
    public async Task ApplyAsync_NullNewValue_ShouldReturnFailure()
    {
        var pack = new SavePatchPack(new SavePatchMetadata("v1", "test", "test", "hash", DateTimeOffset.UtcNow), new SavePatchCompatibility(new[] { "test" }, "test", null), new[] { new SavePatchOperation(SavePatchOperationKind.SetValue, "/f", "f1", "int32", null, null, 0) });
        await RunApplyTestAsync(new StubSaveCodec(), new StubSavePatchPackService(), pack, r => r.Failure!.ReasonCode.Should().Be("new_value_missing"), strict: false);
    }

    [Fact]
    public async Task ApplyAsync_ValidationFailed_ShouldReturnFailure()
    {
        await RunApplyTestAsync(new StubSaveCodec(isValid: false), new StubSavePatchPackService(), BuildEmptyPack(), r => r.Failure!.ReasonCode.Should().Be("validation_failed"), strict: false);
    }

    [Fact]
    public async Task ApplyAsync_ThreeArgOverload_ShouldDefaultStrict()
    {
        await RunApplyTestAsync(new StubSaveCodec(), new StubSavePatchPackService(sourceHashMatches: false), BuildEmptyPack(), r => r.Failure!.ReasonCode.Should().Be("source_hash_mismatch"));
    }

    [Fact]
    public async Task RestoreLastBackupAsync_NoBackup_ShouldReturnNotRestored()
    {
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllBytes(Path.Join(tempDir, "test.sav"), new byte[] { 0x01 });
            var service = new SavePatchApplyService(new StubSaveCodec(), new StubSavePatchPackService(), NullLogger<SavePatchApplyService>.Instance);
            var result = await service.RestoreLastBackupAsync(Path.Join(tempDir, "test.sav"));
            result.Restored.Should().BeFalse();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public async Task RestoreLastBackupAsync_WithBackup_ShouldRestore()
    {
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllBytes(Path.Join(tempDir, "test.sav"), new byte[] { 0x01, 0x02 });
            File.WriteAllBytes(Path.Join(tempDir, "test.sav.bak.20260101120000000.sav"), new byte[] { 0xAA, 0xBB });
            var service = new SavePatchApplyService(new StubSaveCodec(), new StubSavePatchPackService(), NullLogger<SavePatchApplyService>.Instance);
            var result = await service.RestoreLastBackupAsync(Path.Join(tempDir, "test.sav"));
            result.Restored.Should().BeTrue();
            result.RestoredHash.Should().NotBeNullOrWhiteSpace();
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void SavePatchApplyResult_Defaults_ShouldBeNull()
    {
        var r = new SavePatchApplyResult(SavePatchApplyClassification.Applied, true, "ok");
        r.OutputPath.Should().BeNull(); r.BackupPath.Should().BeNull(); r.ReceiptPath.Should().BeNull(); r.Failure.Should().BeNull();
    }

    [Fact]
    public void SaveRollbackResult_Defaults_ShouldBeNull()
    {
        var r = new SaveRollbackResult(false, "msg");
        r.TargetPath.Should().BeNull(); r.BackupPath.Should().BeNull(); r.RestoredHash.Should().BeNull();
    }

    [Fact]
    public void SavePatchApplyFailure_ShouldStoreAll()
    {
        var f = new SavePatchApplyFailure("reason", "msg", "f1", "/path");
        f.ReasonCode.Should().Be("reason"); f.Message.Should().Be("msg"); f.FieldId.Should().Be("f1"); f.FieldPath.Should().Be("/path");
    }

    private static async Task RunApplyTestAsync(ISaveCodec codec, ISavePatchPackService packService, SavePatchPack pack, Action<SavePatchApplyResult> assertion, bool strict = true)
    {
        var tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var savePath = Path.Join(tempDir, "test.sav");
            File.WriteAllBytes(savePath, new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var service = new SavePatchApplyService(codec, packService, NullLogger<SavePatchApplyService>.Instance);
            var result = await service.ApplyAsync(savePath, pack, "test", strict, CancellationToken.None);
            result.Applied.Should().BeFalse();
            assertion(result);
        }
        finally { Directory.Delete(tempDir, true); }
    }

    private static object CreateHelper(ISaveCodec? codec = null)
    {
        var t = typeof(SavePatchApplyService).Assembly.GetType("SwfocTrainer.Saves.Services.SavePatchApplyServiceHelper")!;
        return Activator.CreateInstance(t, BindingFlags.Instance | BindingFlags.Public, null, new object[] { codec ?? new StubSaveCodec(), NullLogger.Instance, "not found in schema", "unknown save field selector" }, null)!;
    }

    private static object CreateHelperDirect(ISaveCodec codec, ILogger logger, string s1, string s2)
    {
        var t = typeof(SavePatchApplyService).Assembly.GetType("SwfocTrainer.Saves.Services.SavePatchApplyServiceHelper")!;
        return Activator.CreateInstance(t, BindingFlags.Instance | BindingFlags.Public, null, new object[] { codec, logger, s1, s2 }, null)!;
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

    private static async Task<SavePatchApplyResult?> InvokeTryApplyOperationValueAsync(object helper, SaveDocument doc, SavePatchOperation op, object? value, string reason, CancellationToken ct)
    {
        var task = (Task<SavePatchApplyResult?>)helper.GetType().GetMethod("TryApplyOperationValueAsync")!.Invoke(helper, new[] { doc, op, value, reason, ct })!;
        return await task;
    }

    private static async Task<string?> InvokeResolveLatestBackupPathAsync(object helper, string targetPath, CancellationToken ct)
    {
        var task = (Task<string?>)helper.GetType().GetMethod("ResolveLatestBackupPathAsync")!.Invoke(helper, new object[] { targetPath, ct })!;
        return await task;
    }

    private static SavePatchPack BuildEmptyPack() => new(new SavePatchMetadata("v1", "test", "test", "hash", DateTimeOffset.UtcNow), new SavePatchCompatibility(new[] { "test" }, "test", null), Array.Empty<SavePatchOperation>());

    private sealed class StubSaveCodec : ISaveCodec
    {
        private readonly bool _isValid;
        public StubSaveCodec(bool isValid = true) => _isValid = isValid;
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken ct) => Task.FromResult(new SaveDocument(path, schemaId, new byte[] { 0x01, 0x02, 0x03, 0x04 }, new SaveNode("/", "root", "root", null)));
        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken ct) => Task.CompletedTask;
        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken ct) => Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken ct) => Task.FromResult(new SaveValidationResult(_isValid, _isValid ? Array.Empty<string>() : new[] { "error" }, Array.Empty<string>()));
        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class ThrowingLoadSaveCodec : ISaveCodec
    {
        private readonly Exception _ex;
        public ThrowingLoadSaveCodec(Exception ex) => _ex = ex;
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken ct) => throw _ex;
        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken ct) => Task.CompletedTask;
        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken ct) => Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken ct) => Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class ThrowingEditSaveCodec : ISaveCodec
    {
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken ct) => Task.FromResult(new SaveDocument(path, schemaId, new byte[] { 0x01, 0x02, 0x03, 0x04 }, new SaveNode("/", "root", "root", null)));
        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken ct) => throw new InvalidOperationException("not found in schema: " + nodePath);
        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken ct) => Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken ct) => Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class StubSavePatchPackService : ISavePatchPackService
    {
        private readonly bool _isCompatible;
        private readonly bool _sourceHashMatches;
        public StubSavePatchPackService(bool isCompatible = true, bool sourceHashMatches = true) { _isCompatible = isCompatible; _sourceHashMatches = sourceHashMatches; }
        public Task<SavePatchPack> ExportAsync(SaveDocument original, SaveDocument modified, string profileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken ct) => throw new NotImplementedException();
        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument target, string profileId, CancellationToken ct) => Task.FromResult(new SavePatchCompatibilityResult(_isCompatible, _sourceHashMatches, "hash", _isCompatible ? Array.Empty<string>() : new[] { "error" }, Array.Empty<string>()));
        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument target, string profileId, CancellationToken ct) => throw new NotImplementedException();
    }
}
