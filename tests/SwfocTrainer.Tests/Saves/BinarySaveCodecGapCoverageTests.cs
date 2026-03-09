using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class BinarySaveCodecGapCoverageTests
{
    [Fact]
    public async Task ValidateAsync_ShouldReportWarningsErrors_AndIgnoreMissingBlockFields()
    {
        var schemaRoot = await CreateSchemaRootAsync(
            """
            {
              "schemaId": "codec_gap_validation",
              "gameBuild": "test",
              "endianness": "little",
              "rootBlocks": [
                { "id": "root", "name": "Root", "offset": 0, "length": 32, "type": "struct", "fields": ["warn_field", "error_field", "missing_field"] }
              ],
              "fieldDefs": [
                { "id": "warn_field", "name": "Warn Field", "valueType": "int32", "offset": 0, "length": 4, "path": "/root/warn_field" },
                { "id": "error_field", "name": "Error Field", "valueType": "int32", "offset": 4, "length": 4, "path": "/root/error_field" },
                { "id": "oob_field", "name": "Out Of Bounds", "valueType": "int32", "offset": 40, "length": 4, "path": "/root/oob_field" }
              ],
              "arrayDefs": [],
              "validationRules": [
                { "id": "warn", "rule": "field_non_negative", "target": "warn_field", "message": "warn hit", "severity": "warning" },
                { "id": "error", "rule": "field_non_negative", "target": "error_field", "message": "error hit", "severity": "error" }
              ],
              "checksumRules": []
            }
            """,
            "codec_gap_validation");

        try
        {
            var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
            var path = Path.Combine(schemaRoot, "input.sav");
            var bytes = new byte[16];
            BitConverter.GetBytes(-1).CopyTo(bytes, 0);
            BitConverter.GetBytes(-2).CopyTo(bytes, 4);
            await File.WriteAllBytesAsync(path, bytes);

            var document = await codec.LoadAsync(path, "codec_gap_validation", CancellationToken.None);
            document.Root.Children.Should().ContainSingle();
            document.Root.Children![0].Children.Should().HaveCount(2);

            var validation = await codec.ValidateAsync(document, CancellationToken.None);

            validation.IsValid.Should().BeFalse();
            validation.Warnings.Should().ContainSingle("warn hit");
            validation.Errors.Should().Contain(x => x.Contains("error hit", StringComparison.OrdinalIgnoreCase));
            validation.Errors.Should().Contain(x => x.Contains("out of range", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldCreateOutputDirectory_AndTolerateShortChecksumOutputs()
    {
        var schemaRoot = await CreateSchemaRootAsync(
            """
            {
              "schemaId": "codec_gap_write",
              "gameBuild": "test",
              "endianness": "little",
              "rootBlocks": [
                { "id": "root", "name": "Root", "offset": 0, "length": 32, "type": "struct", "fields": ["credits"] }
              ],
              "fieldDefs": [
                { "id": "credits", "name": "Credits", "valueType": "int32", "offset": 0, "length": 4, "path": "/root/credits" }
              ],
              "arrayDefs": [],
              "validationRules": [],
              "checksumRules": [
                { "id": "crc-short", "algorithm": "crc32", "startOffset": 0, "endOffset": 4, "outputOffset": 8, "outputLength": 2 }
              ]
            }
            """,
            "codec_gap_write");

        try
        {
            var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
            var inputPath = Path.Combine(schemaRoot, "input.sav");
            var nestedOutput = Path.Combine(schemaRoot, "nested", "deep", "output.sav");
            await File.WriteAllBytesAsync(inputPath, new byte[32]);

            var document = await codec.LoadAsync(inputPath, "codec_gap_write", CancellationToken.None);
            await codec.EditAsync(document, "/root/credits", 4242, CancellationToken.None);
            await codec.WriteAsync(document, nestedOutput, CancellationToken.None);

            File.Exists(nestedOutput).Should().BeTrue();
            var written = await File.ReadAllBytesAsync(nestedOutput);
            BitConverter.ToInt32(written, 0).Should().Be(4242);
            written[8].Should().Be(0);
            written[9].Should().Be(0);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldThrow_WhenFieldIsUnknown()
    {
        var schemaRoot = await CreateSchemaRootAsync(
            """
            {
              "schemaId": "codec_gap_edit",
              "gameBuild": "test",
              "endianness": "little",
              "rootBlocks": [
                { "id": "root", "name": "Root", "offset": 0, "length": 16, "type": "struct", "fields": [] }
              ],
              "fieldDefs": [],
              "arrayDefs": [],
              "validationRules": [],
              "checksumRules": []
            }
            """,
            "codec_gap_edit");

        try
        {
            var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
            var inputPath = Path.Combine(schemaRoot, "input.sav");
            await File.WriteAllBytesAsync(inputPath, new byte[16]);
            var document = await codec.LoadAsync(inputPath, "codec_gap_edit", CancellationToken.None);

            var act = () => codec.EditAsync(document, "/root/unknown", 1, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*not found in schema*");
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    private static async Task<string> CreateSchemaRootAsync(string schemaJson, string schemaId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-codec-gap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, $"{schemaId}.json"), schemaJson);
        return root;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
