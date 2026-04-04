using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class BinarySaveCodecBranchTests
{
    [Fact]
    public void Constructor_ShouldThrow_WhenOptionsIsNull()
    {
        var act = () => new BinarySaveCodec(null!, NullLogger<BinarySaveCodec>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        var act = () => new BinarySaveCodec(new SaveOptions(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadAsync_ShouldThrow_WhenPathIsNull()
    {
        var codec = CreateCodec();
        var act = () => codec.LoadAsync(null!, "base_swfoc_steam_v1");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadAsync_ShouldThrow_WhenSchemaIdIsNull()
    {
        var codec = CreateCodec();
        var act = () => codec.LoadAsync("/some/path.sav", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadAsync_ShouldThrow_WhenFileDoesNotExist()
    {
        var codec = CreateCodec();
        var fakePath = Path.Join(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.sav");
        var act = () => codec.LoadAsync(fakePath, "base_swfoc_steam_v1");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadAsync_Overload_ShouldDelegate()
    {
        var codec = CreateCodec();
        var tempFile = await CreateTempSavAsync();
        try
        {
            var doc = await codec.LoadAsync(tempFile, "base_swfoc_steam_v1");
            doc.Should().NotBeNull();
            doc.SchemaId.Should().Be("base_swfoc_steam_v1");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldThrow_WhenDocumentIsNull()
    {
        var codec = CreateCodec();
        var act = () => codec.EditAsync(null!, "/economy/credits_empire", 1000);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EditAsync_ShouldThrow_WhenNodePathIsNull()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        var act = () => codec.EditAsync(doc, null!, 1000);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EditAsync_ShouldThrow_WhenFieldNotFound()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        var act = () => codec.EditAsync(doc, "/nonexistent/field", 42);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EditAsync_ShouldResolveByFieldId()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        await codec.EditAsync(doc, "credits_empire", 9999);
        BitConverter.ToInt32(doc.Raw, 6144).Should().Be(9999);
    }

    [Fact]
    public async Task EditAsync_Overload_ShouldDelegate()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        await codec.EditAsync(doc, "/economy/credits_empire", 5555);
        BitConverter.ToInt32(doc.Raw, 6144).Should().Be(5555);
    }

    [Fact]
    public async Task EditAsync_ShouldWriteInt32BigEndian()
    {
        var (codec, schemaRoot) = await CreateBigEndianCodecAsync("int32", 0, 4);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "big_endian_test");
            await codec.EditAsync(doc, "/header/test_field", 42);
            BinaryPrimitives.ReadInt32BigEndian(doc.Raw.AsSpan(0, 4)).Should().Be(42);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteUInt32LittleEndian()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "uint32", 6144, 4, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 300_000, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", 12345u);
            BinaryPrimitives.ReadUInt32LittleEndian(doc.Raw.AsSpan(6144, 4)).Should().Be(12345u);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteUInt32BigEndian()
    {
        var (codec, schemaRoot) = await CreateBigEndianCodecAsync("uint32", 0, 4);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "big_endian_test");
            await codec.EditAsync(doc, "/header/test_field", 42u);
            BinaryPrimitives.ReadUInt32BigEndian(doc.Raw.AsSpan(0, 4)).Should().Be(42u);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteInt64LittleEndian()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "int64", 0, 8, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", 999999999L);
            BitConverter.ToInt64(doc.Raw, 0).Should().Be(999999999L);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteInt64BigEndian()
    {
        var (codec, schemaRoot) = await CreateBigEndianCodecAsync("int64", 0, 8);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "big_endian_test");
            await codec.EditAsync(doc, "/header/test_field", 999999999L);
            BinaryPrimitives.ReadInt64BigEndian(doc.Raw.AsSpan(0, 8)).Should().Be(999999999L);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteByteField()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "byte", 0, 1, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", (byte)0xAB);
            doc.Raw[0].Should().Be(0xAB);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteBoolField_True()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "bool", 0, 1, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", true);
            doc.Raw[0].Should().Be(1);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteBoolField_False()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "bool", 0, 1, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            doc.Raw[0] = 0xFF;
            await codec.EditAsync(doc, "/header/test_field", false);
            doc.Raw[0].Should().Be(0);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteFloatField()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "float", 0, 4, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", 2.5f);
            BitConverter.ToSingle(doc.Raw, 0).Should().BeApproximately(2.5f, 0.001f);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteDoubleField()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "double", 0, 8, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", 7.77d);
            BitConverter.ToDouble(doc.Raw, 0).Should().BeApproximately(7.77d, 0.001d);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteAsciiField()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "ascii", 0, 8, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", "Test");
            Encoding.ASCII.GetString(doc.Raw, 0, 4).Should().Be("Test");
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteAsciiField_TruncatesToFieldLength()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "ascii", 0, 4, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            await codec.EditAsync(doc, "/header/test_field", "VeryLongString");
            Encoding.ASCII.GetString(doc.Raw, 0, 4).Should().Be("Very");
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldThrow_ForUnsupportedFieldType()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "custom_hex", 0, 4, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            var act = () => codec.EditAsync(doc, "/header/test_field", 42);
            await act.Should().ThrowAsync<NotSupportedException>();
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldThrow_WhenFieldOutsideBounds()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "int32", 999, 4, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            var act = () => codec.EditAsync(doc, "/header/test_field", 42);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenDocumentIsNull()
    {
        var codec = CreateCodec();
        var act = () => codec.ValidateAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateAsync_ShouldReportOutOfRangeBlocks()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "int32", 0, 4, "little",
            blockOffset: 999990, blockLength: 100);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(x => x.Contains("out of range"));
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldReportOutOfRangeFields()
    {
        var codec = CreateCodecWithSchema(out var schemaRoot, "int32", 999990, 4, "little");
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "custom_edit_test");
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(x => x.Contains("out of range"));
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldReportNegativeIntAsError()
    {
        var codec = CreateCodec();
        var tempFile = await CreateTempSavAsync();
        try
        {
            var doc = await codec.LoadAsync(tempFile, "base_swfoc_steam_v1");
            await codec.EditAsync(doc, "/economy/credits_empire", -1);
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(x => x.Contains("negative"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldReportValidationWarnings()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "warning_test", """
        {
          "schemaId": "warning_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["test_int"] }
          ],
          "fieldDefs": [
            { "id": "test_int", "name": "Test Int", "valueType": "int32", "offset": 0, "length": 4, "path": "/header/test_int" }
          ],
          "arrayDefs": [],
          "validationRules": [
            { "id": "warn_negative", "rule": "field_non_negative", "target": "test_int", "message": "Test field should not be negative", "severity": "warning" }
          ],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "warning_test");
            BinaryPrimitives.WriteInt32LittleEndian(doc.Raw.AsSpan(0, 4), -1);
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeTrue();
            result.Warnings.Should().Contain(x => x.Contains("negative"));
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldSkipRule_WhenTargetFieldMissing()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "skip_rule_test", """
        {
          "schemaId": "skip_rule_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": [] }
          ],
          "fieldDefs": [],
          "arrayDefs": [],
          "validationRules": [
            { "id": "rule_missing_target", "rule": "field_non_negative", "target": "nonexistent_field", "message": "Should not fire", "severity": "error" }
          ],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "skip_rule_test");
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldSkipRule_WhenRuleTypeUnknown()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "unknown_rule_test", """
        {
          "schemaId": "unknown_rule_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["test_int"] }
          ],
          "fieldDefs": [
            { "id": "test_int", "name": "Test Int", "valueType": "int32", "offset": 0, "length": 4, "path": "/header/test_int" }
          ],
          "arrayDefs": [],
          "validationRules": [
            { "id": "unknown_type", "rule": "unknown_rule_type", "target": "test_int", "message": "Should not fire", "severity": "error" }
          ],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "unknown_rule_test");
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldDetectNegativeInt64()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "int64_neg_test", """
        {
          "schemaId": "int64_neg_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["test_long"] }
          ],
          "fieldDefs": [
            { "id": "test_long", "name": "Test Long", "valueType": "int64", "offset": 0, "length": 8, "path": "/header/test_long" }
          ],
          "arrayDefs": [],
          "validationRules": [
            { "id": "long_non_neg", "rule": "field_non_negative", "target": "test_long", "message": "Long field cannot be negative", "severity": "error" }
          ],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "int64_neg_test");
            BinaryPrimitives.WriteInt64LittleEndian(doc.Raw.AsSpan(0, 8), -5L);
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(x => x.Contains("negative"));
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ValidateAsync_Overload_ShouldDelegate()
    {
        var codec = CreateCodec();
        var tempFile = await CreateTempSavAsync();
        try
        {
            var doc = await codec.LoadAsync(tempFile, "base_swfoc_steam_v1");
            var result = await codec.ValidateAsync(doc);
            result.IsValid.Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldThrow_WhenDocumentIsNull()
    {
        var codec = CreateCodec();
        var act = () => codec.WriteAsync(null!, "/some/path.sav");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_ShouldThrow_WhenOutputPathIsNull()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        var act = () => codec.WriteAsync(doc, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_Overload_ShouldDelegate()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        var outputPath = Path.Join(Path.GetTempPath(), $"swfoc-write-test-{Guid.NewGuid():N}.sav");
        try
        {
            await codec.WriteAsync(doc, outputPath);
            File.Exists(outputPath).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldApplyCrc32Checksums()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        await codec.EditAsync(doc, "/economy/credits_empire", 7777);
        var outputPath = Path.Join(Path.GetTempPath(), $"swfoc-crc-test-{Guid.NewGuid():N}.sav");
        try
        {
            await codec.WriteAsync(doc, outputPath);
            var written = await File.ReadAllBytesAsync(outputPath);
            var checksumBytes = new byte[4];
            Array.Copy(written, 508, checksumBytes, 0, 4);
            BinaryPrimitives.ReadUInt32LittleEndian(checksumBytes).Should().NotBe(0u);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldSkipOutOfBoundsChecksumRule()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "bad_checksum_test", """
        {
          "schemaId": "bad_checksum_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 16, "type": "struct", "fields": ["test_int"] }
          ],
          "fieldDefs": [
            { "id": "test_int", "name": "Test", "valueType": "int32", "offset": 0, "length": 4, "path": "/header/test_int" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": [
            { "id": "bad_rule", "algorithm": "crc32", "startOffset": -1, "endOffset": 100, "outputOffset": 0, "outputLength": 4 }
          ]
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "bad_checksum_test");
            var outputPath = Path.Join(Path.GetTempPath(), $"swfoc-bad-crc-{Guid.NewGuid():N}.sav");
            try
            {
                await codec.WriteAsync(doc, outputPath);
                File.Exists(outputPath).Should().BeTrue();
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldSkipUnknownChecksumAlgorithm()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "unknown_algo_test", """
        {
          "schemaId": "unknown_algo_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["test_int"] }
          ],
          "fieldDefs": [
            { "id": "test_int", "name": "Test", "valueType": "int32", "offset": 0, "length": 4, "path": "/header/test_int" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": [
            { "id": "unknown_algo", "algorithm": "sha512", "startOffset": 0, "endOffset": 16, "outputOffset": 16, "outputLength": 4 }
          ]
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "unknown_algo_test");
            var outputPath = Path.Join(Path.GetTempPath(), $"swfoc-unknown-algo-{Guid.NewGuid():N}.sav");
            try
            {
                await codec.WriteAsync(doc, outputPath);
                var written = await File.ReadAllBytesAsync(outputPath);
                BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(16, 4)).Should().Be(0u);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task WriteAsync_ShouldSkipChecksumRule_WhenOutputTooShort()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "short_output_test", """
        {
          "schemaId": "short_output_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["test_int"] }
          ],
          "fieldDefs": [
            { "id": "test_int", "name": "Test", "valueType": "int32", "offset": 0, "length": 4, "path": "/header/test_int" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": [
            { "id": "short_output", "algorithm": "crc32", "startOffset": 0, "endOffset": 16, "outputOffset": 16, "outputLength": 2 }
          ]
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "short_output_test");
            var outputPath = Path.Join(Path.GetTempPath(), $"swfoc-short-output-{Guid.NewGuid():N}.sav");
            try
            {
                await codec.WriteAsync(doc, outputPath);
                File.Exists(outputPath).Should().BeTrue();
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task RoundTripCheckAsync_ShouldThrow_WhenDocumentIsNull()
    {
        var codec = CreateCodec();
        var act = () => codec.RoundTripCheckAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RoundTripCheckAsync_Overload_ShouldDelegate()
    {
        var codec = CreateCodec();
        var doc = await LoadDocAsync(codec);
        var ok = await codec.RoundTripCheckAsync(doc);
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task BuildNodeTree_ShouldSkipUnknownFieldIds()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "missing_field_test", """
        {
          "schemaId": "missing_field_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["real_field", "ghost_field"] }
          ],
          "fieldDefs": [
            { "id": "real_field", "name": "Real", "valueType": "int32", "offset": 0, "length": 4, "path": "/header/real_field" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "missing_field_test");
            doc.Root.Children.Should().HaveCount(1);
            doc.Root.Children![0].Children.Should().HaveCount(1);
            doc.Root.Children[0].Children![0].Path.Should().Be("/header/real_field");
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task BuildNodeTree_ShouldUseFieldIdWhenPathIsNull()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "null_path_test", """
        {
          "schemaId": "null_path_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["pathless_field"] }
          ],
          "fieldDefs": [
            { "id": "pathless_field", "name": "Pathless", "valueType": "int32", "offset": 0, "length": 4 }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "null_path_test");
            doc.Root.Children![0].Children![0].Path.Should().Be("pathless_field");
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task ReadFieldValue_Private_ShouldReturnNull_WhenFieldExceedsBounds()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "oob_field_test", """
        {
          "schemaId": "oob_field_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["oob_field"] }
          ],
          "fieldDefs": [
            { "id": "oob_field", "name": "OOB", "valueType": "int32", "offset": 100, "length": 4, "path": "/header/oob_field" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "oob_field_test");
            doc.Root.Children![0].Children![0].Value.Should().BeNull();
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteFloatBigEndian()
    {
        var (codec, schemaRoot) = await CreateBigEndianCodecAsync("float", 0, 4);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "big_endian_test");
            await codec.EditAsync(doc, "/header/test_field", 1.5f);
            var floatBytes = doc.Raw[..4];
            if (BitConverter.IsLittleEndian) Array.Reverse(floatBytes);
            BitConverter.ToSingle(floatBytes, 0).Should().BeApproximately(1.5f, 0.001f);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task EditAsync_ShouldWriteDoubleBigEndian()
    {
        var (codec, schemaRoot) = await CreateBigEndianCodecAsync("double", 0, 8);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "big_endian_test");
            await codec.EditAsync(doc, "/header/test_field", 2.5d);
            var doubleBytes = doc.Raw[..8];
            if (BitConverter.IsLittleEndian) Array.Reverse(doubleBytes);
            BitConverter.ToDouble(doubleBytes, 0).Should().BeApproximately(2.5d, 0.001d);
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    [Fact]
    public async Task BuildNodeTree_ShouldHandleBlockWithNullFields()
    {
        var schemaRoot = CreateTempSchemaDir();
        await WriteSchemaFileAsync(schemaRoot, "null_fields_test", """
        {
          "schemaId": "null_fields_test",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct" }
          ],
          "fieldDefs": [],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": []
        }
        """);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        try
        {
            var doc = await LoadCustomDocAsync(codec, 32, "null_fields_test");
            doc.Root.Children.Should().HaveCount(1);
            doc.Root.Children![0].Children.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(schemaRoot);
        }
    }

    private static BinarySaveCodec CreateCodec()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new SaveOptions
        {
            SchemaRootPath = Path.Join(root, "profiles", "default", "schemas")
        };
        return new BinarySaveCodec(options, NullLogger<BinarySaveCodec>.Instance);
    }

    private static async Task<string> CreateTempSavAsync(int size = 300_000)
    {
        var path = Path.Join(Path.GetTempPath(), $"swfoc-codec-branch-{Guid.NewGuid():N}.sav");
        await File.WriteAllBytesAsync(path, new byte[size]);
        return path;
    }

    private static async Task<SaveDocument> LoadDocAsync(BinarySaveCodec codec)
    {
        var tempFile = await CreateTempSavAsync();
        return await codec.LoadAsync(tempFile, "base_swfoc_steam_v1");
    }

    private static string CreateTempSchemaDir()
    {
        var path = Path.Join(Path.GetTempPath(), $"swfoc-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WriteSchemaFileAsync(string schemaRoot, string schemaId, string json)
    {
        await File.WriteAllTextAsync(Path.Join(schemaRoot, $"{schemaId}.json"), json);
    }

    private static BinarySaveCodec CreateCodecWithSchema(out string schemaRoot, string valueType, int offset, int length, string endianness, int blockOffset = 0, int blockLength = 32)
    {
        schemaRoot = CreateTempSchemaDir();
        var json = $$"""
        {
          "schemaId": "custom_edit_test",
          "gameBuild": "test",
          "endianness": "{{endianness}}",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": {{blockOffset}}, "length": {{blockLength}}, "type": "struct", "fields": ["test_field"] }
          ],
          "fieldDefs": [
            { "id": "test_field", "name": "Test Field", "valueType": "{{valueType}}", "offset": {{offset}}, "length": {{length}}, "path": "/header/test_field" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": []
        }
        """;
        File.WriteAllText(Path.Join(schemaRoot, "custom_edit_test.json"), json);
        return new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
    }

    private static async Task<(BinarySaveCodec Codec, string SchemaRoot)> CreateBigEndianCodecAsync(string valueType, int offset, int length)
    {
        var schemaRoot = CreateTempSchemaDir();
        var json = $$"""
        {
          "schemaId": "big_endian_test",
          "gameBuild": "test",
          "endianness": "big",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 32, "type": "struct", "fields": ["test_field"] }
          ],
          "fieldDefs": [
            { "id": "test_field", "name": "Test Field", "valueType": "{{valueType}}", "offset": {{offset}}, "length": {{length}}, "path": "/header/test_field" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": []
        }
        """;
        await File.WriteAllTextAsync(Path.Join(schemaRoot, "big_endian_test.json"), json);
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = schemaRoot }, NullLogger<BinarySaveCodec>.Instance);
        return (codec, schemaRoot);
    }

    private static async Task<SaveDocument> LoadCustomDocAsync(BinarySaveCodec codec, int size, string schemaId)
    {
        var tempFile = Path.Join(Path.GetTempPath(), $"swfoc-custom-{Guid.NewGuid():N}.sav");
        await File.WriteAllBytesAsync(tempFile, new byte[size]);
        return await codec.LoadAsync(tempFile, schemaId);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
