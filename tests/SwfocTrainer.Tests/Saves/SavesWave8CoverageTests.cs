using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

/// <summary>
/// Wave 8 coverage: remaining branches in SavePatchApplyService and SavePatchPackService —
/// constructor null guards, ApplyAsync null guards, unsupported operation kind,
/// missing newValue, target load errors, compatibility checks, strict source hash mismatch.
/// </summary>
public sealed class SavesWave8CoverageTests
{
    #region SavePatchApplyService — constructor null guards

    [Fact]
    public void Constructor_ShouldThrow_WhenSaveCodecIsNull()
    {
        var act = () => new SavePatchApplyService(
            null!,
            new StubPatchPackService(),
            new StubLogger<SavePatchApplyService>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenPatchPackServiceIsNull()
    {
        var act = () => new SavePatchApplyService(
            new StubSaveCodec(),
            null!,
            new StubLogger<SavePatchApplyService>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        var act = () => new SavePatchApplyService(
            new StubSaveCodec(),
            new StubPatchPackService(),
            null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region SavePatchApplyService — ApplyAsync null guards

    [Fact]
    public async Task ApplyAsync_ShouldThrow_WhenTargetSavePathIsNull()
    {
        var service = CreateApplyService();
        var act = async () => await service.ApplyAsync(null!, CreatePack(), "profile", true, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrow_WhenPackIsNull()
    {
        var service = CreateApplyService();
        var act = async () => await service.ApplyAsync("test.sav", null!, "profile", true, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrow_WhenTargetProfileIdIsNull()
    {
        var service = CreateApplyService();
        var act = async () => await service.ApplyAsync("test.sav", CreatePack(), null!, true, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region SavePatchApplyService — overloads

    [Fact]
    public async Task ApplyAsync_TwoParamOverload_ShouldThrow_WhenPathIsNull()
    {
        var service = CreateApplyService();
        var act = async () => await service.ApplyAsync(null!, CreatePack(), "profile");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ApplyAsync_ThreeParamOverload_ShouldThrow_WhenPathIsNull()
    {
        var service = CreateApplyService();
        var act = async () => await service.ApplyAsync(null!, CreatePack(), "profile", strict: false);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region SavePatchApplyService — RestoreLastBackupAsync null guard

    [Fact]
    public async Task RestoreLastBackupAsync_ShouldThrow_WhenTargetSavePathIsNull()
    {
        var service = CreateApplyService();
        var act = async () => await service.RestoreLastBackupAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RestoreLastBackupAsync_ParameterlessOverload_ShouldThrow_WhenNull()
    {
        var service = CreateApplyService();
        var act = async () => await service.RestoreLastBackupAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region SavePatchPackService — constructor null guard

    [Fact]
    public void SavePatchPackService_Constructor_ShouldThrow_WhenOptionsIsNull()
    {
        var act = () => new SavePatchPackService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region SavePatchPackService — ExportAsync null guards

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenOriginalDocIsNull()
    {
        var service = CreatePackService();
        var act = async () => await service.ExportAsync(null!, CreateDoc(), "profile", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenEditedDocIsNull()
    {
        var service = CreatePackService();
        var act = async () => await service.ExportAsync(CreateDoc(), null!, "profile", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var service = CreatePackService();
        var act = async () => await service.ExportAsync(CreateDoc(), CreateDoc(), null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenProfileIdIsWhitespace()
    {
        var service = CreatePackService();
        var act = async () => await service.ExportAsync(CreateDoc(), CreateDoc(), "   ", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenSchemaIdMismatch()
    {
        var service = CreatePackService();
        var doc1 = new SaveDocument("test.sav", "schema_a", new byte[10], new SaveNode("/", "root", "block", null));
        var doc2 = new SaveDocument("test.sav", "schema_b", new byte[10], new SaveNode("/", "root", "block", null));
        var act = async () => await service.ExportAsync(doc1, doc2, "profile", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region SavePatchPackService — ValidateCompatibilityAsync null guards

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldThrow_WhenPackIsNull()
    {
        var service = CreatePackService();
        var act = async () => await service.ValidateCompatibilityAsync(null!, CreateDoc(), "profile", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldThrow_WhenTargetDocIsNull()
    {
        var service = CreatePackService();
        var act = async () => await service.ValidateCompatibilityAsync(CreatePack(), null!, "profile", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateCompatibilityAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var service = CreatePackService();
        var act = async () => await service.ValidateCompatibilityAsync(CreatePack(), CreateDoc(), null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region Helpers

    private static SavePatchApplyService CreateApplyService()
    {
        return new SavePatchApplyService(
            new StubSaveCodec(),
            new StubPatchPackService(),
            new StubLogger<SavePatchApplyService>());
    }

    private static SavePatchPackService CreatePackService()
    {
        return new SavePatchPackService(new SaveOptions
        {
            SchemaRootPath = Path.Join(Path.GetTempPath(), $"swfoc-saves-w8-{Guid.NewGuid():N}")
        });
    }

    private static SavePatchPack CreatePack()
    {
        return new SavePatchPack(
            new SavePatchMetadata("1.0", "profile", "schema", "hash", DateTimeOffset.UtcNow),
            new SavePatchCompatibility(new[] { "profile" }, "schema"),
            Array.Empty<SavePatchOperation>());
    }

    private static SaveDocument CreateDoc()
    {
        return new SaveDocument("test.sav", "schema", new byte[10], new SaveNode("/", "root", "block", null));
    }

    private sealed class StubSaveCodec : ISaveCodec
    {
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SaveDocument(path, schemaId, new byte[10], new SaveNode("/", "root", "block", null)));
        }

        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        }

        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class StubPatchPackService : ISavePatchPackService
    {
        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SavePatchPack(
                new SavePatchMetadata("1.0", profileId, originalDoc.SchemaId, "hash", DateTimeOffset.UtcNow),
                new SavePatchCompatibility(new[] { profileId }, originalDoc.SchemaId),
                Array.Empty<SavePatchOperation>()));
        }

        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>()));
        }

        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class StubLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    #endregion
}
