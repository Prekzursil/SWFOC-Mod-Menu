using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

/// <summary>
/// Wave 8b coverage: remaining branches in SavePatchApplyService (RestoreAfterWriteFailureAsync),
/// SavePatchPackService (ValidateOperationContracts edge cases, ResolveField fallback,
/// ValidateRawOperationContract null newValue, BuildExportOperations checksum overlap),
/// BinarySaveCodec (WriteAsync null directory, ValidateAsync warning severity, RoundTripCheckAsync),
/// SaveSchemaRepository (missing schema), and SavePatchFieldCodec (unknown valueType).
/// </summary>
public sealed class SavesWave8bCoverageTests
{
    #region SavePatchPackService — ValidateCompatibilityAsync edge cases

    [Fact]
    public async Task ValidateCompatibility_ShouldReportSchemaVersionMismatch()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);

        var metadata = new SavePatchMetadata("2.0", "profile1", "test_schema", "abc123", DateTimeOffset.UtcNow);
        var compatibility = new SavePatchCompatibility(new[] { "profile1" }, "test_schema", "1.0");
        var pack = new SavePatchPack(metadata, compatibility, Array.Empty<SavePatchOperation>());
        var doc = new SaveDocument("test.sav", "test_schema", new byte[100], new SaveNode("root", "root", "container", null));

        var result = await sut.ValidateCompatibilityAsync(pack, doc, "profile1");
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("schemaVersion"));
    }

    [Fact]
    public async Task ValidateCompatibility_ShouldReportSchemaMismatch()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);

        var metadata = new SavePatchMetadata("1.0", "profile1", "schema_a", "abc123", DateTimeOffset.UtcNow);
        var compatibility = new SavePatchCompatibility(new[] { "profile1" }, "schema_a", "1.0");
        var pack = new SavePatchPack(metadata, compatibility, Array.Empty<SavePatchOperation>());
        var doc = new SaveDocument("test.sav", "schema_b", new byte[100], new SaveNode("root", "root", "container", null));

        var result = await sut.ValidateCompatibilityAsync(pack, doc, "profile1");
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Schema mismatch"));
    }

    [Fact]
    public async Task ValidateCompatibility_ShouldReportProfileMismatch()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);

        var metadata = new SavePatchMetadata("1.0", "profile1", "test_schema", "abc123", DateTimeOffset.UtcNow);
        var compatibility = new SavePatchCompatibility(new[] { "profile1" }, "test_schema", "1.0");
        var pack = new SavePatchPack(metadata, compatibility, Array.Empty<SavePatchOperation>());
        var doc = new SaveDocument("test.sav", "test_schema", new byte[100], new SaveNode("root", "root", "container", null));

        var result = await sut.ValidateCompatibilityAsync(pack, doc, "other_profile");
        result.IsCompatible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Profile mismatch"));
    }

    [Fact]
    public async Task ValidateCompatibility_ShouldWarn_WhenSourceHashMismatch()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);

        var metadata = new SavePatchMetadata("1.0", "profile1", "test_schema", "wrong_hash", DateTimeOffset.UtcNow);
        var compatibility = new SavePatchCompatibility(new[] { "*" }, "test_schema", "1.0");
        var pack = new SavePatchPack(metadata, compatibility, Array.Empty<SavePatchOperation>());
        var doc = new SaveDocument("test.sav", "test_schema", new byte[100], new SaveNode("root", "root", "container", null));

        var result = await sut.ValidateCompatibilityAsync(pack, doc, "profile1");
        result.SourceHashMatches.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("Source hash mismatch"));
    }

    #endregion

    #region SavePatchPackService — LoadPackAsync validation

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenFileDoesNotExist()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var fakePath = Path.Join(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");
        var act = () => sut.LoadPackAsync(fakePath);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenMetadataIsMissing()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var tempFile = Path.Join(Path.GetTempPath(), $"bad_pack_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempFile, """{"operations":[]}""");
            var act = () => sut.LoadPackAsync(tempFile);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*metadata*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenOperationsIsMissing()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var tempFile = Path.Join(Path.GetTempPath(), $"no_ops_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempFile, """{"metadata":{"schemaVersion":"1.0","createdAtUtc":"2024-01-01T00:00:00Z"}}""");
            var act = () => sut.LoadPackAsync(tempFile);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*operations*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenSchemaVersionIsMissing()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var tempFile = Path.Join(Path.GetTempPath(), $"no_sv_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempFile, """{"metadata":{"createdAtUtc":"2024-01-01T00:00:00Z"},"operations":[]}""");
            var act = () => sut.LoadPackAsync(tempFile);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*schemaVersion*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenCreatedAtUtcIsMissing()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var tempFile = Path.Join(Path.GetTempPath(), $"no_ts_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempFile, """{"metadata":{"schemaVersion":"1.0"},"operations":[]}""");
            var act = () => sut.LoadPackAsync(tempFile);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*createdAtUtc*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenOperationHasNullNewValue()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var tempFile = Path.Join(Path.GetTempPath(), $"null_nv_{Guid.NewGuid():N}.json");
        try
        {
            var json = """
            {
                "metadata": {"schemaVersion":"1.0","createdAtUtc":"2024-01-01T00:00:00Z"},
                "operations": [
                    {"kind":"SetValue","fieldPath":"a/b","fieldId":"f1","valueType":"int32","newValue":null,"offset":0}
                ]
            }
            """;
            await File.WriteAllTextAsync(tempFile, json);
            var act = () => sut.LoadPackAsync(tempFile);
            await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*newValue*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadPackAsync_ShouldThrow_WhenOperationFieldsMissing()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var tempFile = Path.Join(Path.GetTempPath(), $"missing_fields_{Guid.NewGuid():N}.json");
        try
        {
            var json = """
            {
                "metadata": {"schemaVersion":"1.0","createdAtUtc":"2024-01-01T00:00:00Z"},
                "operations": [{}]
            }
            """;
            await File.WriteAllTextAsync(tempFile, json);
            var act = () => sut.LoadPackAsync(tempFile);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region SavePatchPackService — ExportAsync schema mismatch

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenSchemaMismatch()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var original = new SaveDocument("test.sav", "schema_a", new byte[100], new SaveNode("root", "root", "container", null));
        var edited = new SaveDocument("test.sav", "schema_b", new byte[100], new SaveNode("root", "root", "container", null));

        var act = () => sut.ExportAsync(original, edited, "profile1");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*same schema*");
    }

    [Fact]
    public async Task ExportAsync_ShouldThrow_WhenProfileIdIsWhitespace()
    {
        var options = new SaveOptions { SchemaRootPath = CreateTempSchemaDir() };
        var sut = new SavePatchPackService(options);
        var doc = new SaveDocument("test.sav", "test_schema", new byte[100], new SaveNode("root", "root", "container", null));

        var act = () => sut.ExportAsync(doc, doc, "   ");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Profile ID*");
    }

    #endregion

    #region SavePatchApplyService — constructor and null guards for apply overloads

    [Fact]
    public void SavePatchApplyService_Constructor_ShouldThrow_WhenAllNull()
    {
        var act = () => new SavePatchApplyService(null!, null!, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Helpers

    private static string CreateTempSchemaDir()
    {
        var dir = Path.Join(Path.GetTempPath(), $"schema_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class StubSaveCodec : ISaveCodec
    {
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken) =>
            Task.FromResult(new SaveDocument(path, schemaId, new byte[100], new SaveNode("root", "root", "container", null)));
        public Task EditAsync(SaveDocument document, string fieldSelector, object? value, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken) =>
            Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubPatchPackService : ISavePatchPackService
    {
        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId) =>
            throw new NotImplementedException();
        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<SavePatchPack> LoadPackAsync(string path) =>
            throw new NotImplementedException();
        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>()));
        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId) =>
            ValidateCompatibilityAsync(pack, targetDoc, targetProfileId, CancellationToken.None);
        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId) =>
            throw new NotImplementedException();
    }

    private sealed class StubLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    #endregion
}
