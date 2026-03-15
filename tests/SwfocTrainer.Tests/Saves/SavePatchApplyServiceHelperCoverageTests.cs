using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchApplyServiceHelperCoverageTests
{
    [Fact]
    public async Task ResolveLatestBackupPathAsync_ShouldPreferValidReceiptPath_ThenFallbackToCandidates()
    {
        var helper = CreateHelper(new StubCodec());

        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");
        await File.WriteAllTextAsync(targetPath, "target");

        var backupA = Path.Combine(tempDir, "campaign.sav.bak.100.sav");
        var backupB = Path.Combine(tempDir, "campaign.sav.bak.200.sav");
        await File.WriteAllTextAsync(backupA, "a");
        await File.WriteAllTextAsync(backupB, "b");
        File.SetLastWriteTimeUtc(backupA, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(backupB, DateTime.UtcNow.AddMinutes(-1));

        var invalidReceiptPath = Path.Combine(tempDir, "campaign.sav.apply-receipt.900.json");
        await WriteReceiptAsync(invalidReceiptPath, Path.Combine(tempDir, "missing.sav"));
        File.SetLastWriteTimeUtc(invalidReceiptPath, DateTime.UtcNow.AddMinutes(1));

        var resolvedFallback = await ResolveLatestBackupPathAsync(helper, targetPath);
        resolvedFallback.Should().Be(backupB);

        var validReceiptPath = Path.Combine(tempDir, "campaign.sav.apply-receipt.901.json");
        await WriteReceiptAsync(validReceiptPath, backupA);
        File.SetLastWriteTimeUtc(validReceiptPath, DateTime.UtcNow.AddMinutes(2));

        var resolvedReceipt = await ResolveLatestBackupPathAsync(helper, targetPath);
        resolvedReceipt.Should().Be(backupA);
    }

    [Fact]
    public async Task ResolveLatestBackupPathAsync_ShouldIgnoreMalformedReceipt_AndInvalidTarget()
    {
        var helper = CreateHelper(new StubCodec());

        var tempDir = CreateTempDirectory();
        var targetPath = Path.Combine(tempDir, "campaign.sav");
        await File.WriteAllTextAsync(targetPath, "target");

        var backup = Path.Combine(tempDir, "campaign.sav.bak.300.sav");
        await File.WriteAllTextAsync(backup, "backup");

        var malformedReceiptPath = Path.Combine(tempDir, "campaign.sav.apply-receipt.999.json");
        await File.WriteAllTextAsync(malformedReceiptPath, "{ not json");
        File.SetLastWriteTimeUtc(malformedReceiptPath, DateTime.UtcNow.AddMinutes(1));

        var resolved = await ResolveLatestBackupPathAsync(helper, targetPath);
        resolved.Should().Be(backup);

        var invalid = await ResolveLatestBackupPathAsync(helper, string.Empty);
        invalid.Should().BeNull();
    }

    [Fact]
    public async Task TryNormalizeAndApply_ShouldFailClosed_WhenCodecRejectsSelectors()
    {
        var codec = new StubCodec
        {
            EditBehavior = selector => throw new InvalidOperationException($"{selector} not found in schema")
        };
        var helper = CreateHelper(codec);

        var operation = new SavePatchOperation(
            SavePatchOperationKind.SetValue,
            FieldPath: "/economy/credits",
            FieldId: "credits",
            ValueType: "int32",
            OldValue: 0,
            NewValue: "not-int",
            Offset: 1);

        var normalizeTuple = InvokeNormalize(helper, operation, "value_normalization_failed");
        var normalizeFailure = normalizeTuple.GetType().GetField("Item2")!.GetValue(normalizeTuple) as SavePatchApplyResult;
        normalizeFailure.Should().NotBeNull();
        normalizeFailure!.Failure!.ReasonCode.Should().Be("value_normalization_failed");

        var applyFailure = await InvokeApplyAsync(helper, CreateDocument(), operation with { NewValue = 1 }, 1, "field_apply_failed");
        applyFailure.Should().NotBeNull();
        applyFailure!.Failure!.ReasonCode.Should().Be("field_apply_failed");

        codec.EditCalls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task TryApplyOperationValueAsync_ShouldFail_WhenNoSelectorsOrNonMismatchErrorsExist()
    {
        var mismatchHelper = CreateHelper(new StubCodec());
        var mismatchOperation = new SavePatchOperation(
            SavePatchOperationKind.SetValue,
            FieldPath: " ",
            FieldId: "",
            ValueType: "int32",
            OldValue: 0,
            NewValue: 1,
            Offset: 0);

        var noSelectorFailure = await InvokeApplyAsync(mismatchHelper, CreateDocument(), mismatchOperation, 1, "no_selector");

        noSelectorFailure.Should().NotBeNull();
        noSelectorFailure!.Failure!.ReasonCode.Should().Be("no_selector");

        var crashingCodec = new StubCodec
        {
            EditBehavior = static _ => new IOException("disk failure")
        };
        var crashingHelper = CreateHelper(crashingCodec);
        var crashingOperation = mismatchOperation with
        {
            FieldId = "credits",
            FieldPath = "/economy/credits"
        };

        var nonMismatchFailure = await InvokeApplyAsync(crashingHelper, CreateDocument(), crashingOperation, 1, "codec_failure");

        nonMismatchFailure.Should().NotBeNull();
        nonMismatchFailure!.Failure!.ReasonCode.Should().Be("codec_failure");
        crashingCodec.EditCalls.Should().Be(1);
    }

    [Fact]
    public void TryDeleteTempOutput_ShouldHandleMissingPath_AndLockedFile()
    {
        var helper = CreateHelper(new StubCodec());

        InvokeDeleteTemp(helper, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.tmp"));

        var tempFile = Path.GetTempFileName();
        using (File.Open(tempFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            InvokeDeleteTemp(helper, tempFile);
        }

        File.Exists(tempFile).Should().BeTrue();
        File.Delete(tempFile);
    }

    [Fact]
    public void TryNormalizeBackupCandidatePath_ShouldRejectEmptyWrongExtensionAndMissingFiles()
    {
        InvokeNormalizeBackupCandidatePath(string.Empty, out var emptyPath, out var emptyReason).Should().BeFalse();
        emptyPath.Should().BeNull();
        emptyReason.Should().Be("path is empty");

        var wrongExtension = Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid():N}.txt");
        File.WriteAllText(wrongExtension, "backup");
        try
        {
            InvokeNormalizeBackupCandidatePath(wrongExtension, out var wrongExtPath, out var wrongExtReason).Should().BeFalse();
            wrongExtPath.Should().BeNull();
            wrongExtReason.Should().Be("path does not use .sav extension");
        }
        finally
        {
            File.Delete(wrongExtension);
        }

        var missingSav = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.sav");
        InvokeNormalizeBackupCandidatePath(missingSav, out var missingPath, out var missingReason).Should().BeFalse();
        missingPath.Should().BeNull();
        missingReason.Should().Be("backup file does not exist");
    }

    private static object CreateHelper(ISaveCodec codec)
    {
        var helperType = typeof(SavePatchApplyService).Assembly.GetType("SwfocTrainer.Saves.Services.SavePatchApplyServiceHelper");
        helperType.Should().NotBeNull();

        var helper = Activator.CreateInstance(
            helperType!,
            codec,
            NullLogger.Instance,
            "not found in schema",
            "unknown field");
        helper.Should().NotBeNull();
        return helper!;
    }

    private static async Task<string?> ResolveLatestBackupPathAsync(object helper, string targetPath)
    {
        var method = helper.GetType().GetMethod("ResolveLatestBackupPathAsync", BindingFlags.Instance | BindingFlags.Public);
        method.Should().NotBeNull();
        var task = (Task<string?>)method!.Invoke(helper, new object?[] { targetPath, CancellationToken.None })!;
        return await task;
    }

    private static object InvokeNormalize(object helper, SavePatchOperation operation, string reasonCode)
    {
        var method = helper.GetType().GetMethod("TryNormalizePatchValue", BindingFlags.Instance | BindingFlags.Public);
        method.Should().NotBeNull();
        return method!.Invoke(helper, new object?[] { operation, reasonCode })!;
    }

    private static async Task<SavePatchApplyResult?> InvokeApplyAsync(
        object helper,
        SaveDocument doc,
        SavePatchOperation operation,
        object? value,
        string reasonCode)
    {
        var method = helper.GetType().GetMethod("TryApplyOperationValueAsync", BindingFlags.Instance | BindingFlags.Public);
        method.Should().NotBeNull();
        var task = (Task<SavePatchApplyResult?>)method!.Invoke(helper, new object?[] { doc, operation, value, reasonCode, CancellationToken.None })!;
        return await task;
    }

    private static void InvokeDeleteTemp(object helper, string path)
    {
        var method = helper.GetType().GetMethod("TryDeleteTempOutput", BindingFlags.Instance | BindingFlags.Public);
        method.Should().NotBeNull();
        method!.Invoke(helper, new object?[] { path });
    }

    private static bool InvokeNormalizeBackupCandidatePath(string path, out string? normalized, out string invalidReason)
    {
        var helperType = typeof(SavePatchApplyService).Assembly.GetType("SwfocTrainer.Saves.Services.SavePatchApplyServiceHelper");
        helperType.Should().NotBeNull();
        var method = helperType!.GetMethod("TryNormalizeBackupCandidatePath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        object?[] args = [path, null, string.Empty];
        var result = (bool)method!.Invoke(null, args)!;
        normalized = args[1] as string;
        invalidReason = (string)args[2]!;
        return result;
    }

    private static SaveDocument CreateDocument()
    {
        var root = new SaveNode("root", "root", "root", null, new[] { new SaveNode("/economy/credits", "credits", "int32", 0) });
        return new SaveDocument("save.sav", "schema", new byte[64], root);
    }

    private static async Task WriteReceiptAsync(string path, string backupPath)
    {
        var payload = new
        {
            RunId = Guid.NewGuid().ToString("N"),
            AppliedAtUtc = DateTimeOffset.UtcNow,
            TargetPath = "target.sav",
            BackupPath = backupPath,
            ReceiptPath = path,
            ProfileId = "base_swfoc",
            SchemaId = "schema",
            Classification = "Applied",
            SourceHash = "a",
            TargetHash = "b",
            AppliedHash = "c",
            OperationsApplied = 1
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swfoc-helper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubCodec : ISaveCodec
    {
        public int EditCalls { get; private set; }

        public Func<string, Exception>? EditBehavior { get; init; }

        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(new SaveDocument(path, schemaId, new byte[32], new SaveNode("root", "root", "root", null)));
        }

        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken)
        {
            _ = document;
            _ = value;
            _ = cancellationToken;
            EditCalls++;
            if (EditBehavior is { } behavior)
            {
                throw behavior(nodePath);
            }

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
            return Task.CompletedTask;
        }

        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            _ = document;
            _ = cancellationToken;
            return Task.FromResult(true);
        }
    }
}
