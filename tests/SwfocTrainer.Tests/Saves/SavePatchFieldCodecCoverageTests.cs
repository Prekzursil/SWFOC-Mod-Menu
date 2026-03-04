#pragma warning disable CA1014
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchFieldCodecCoverageTests
{
    private static readonly Type CodecType =
        typeof(SavePatchPackService).Assembly.GetType("SwfocTrainer.Saves.Internal.SavePatchFieldCodec")
        ?? throw new InvalidOperationException("SavePatchFieldCodec type not found.");

    [Fact]
    public void ComputeSha256Hex_ShouldReturnLowercaseHash()
    {
        var bytes = Encoding.ASCII.GetBytes("swfoc");

        var hash = (string)InvokeStatic("ComputeSha256Hex", bytes)!;

        hash.Should().Be("526c4afb8fcf0106596b198788c4619fac7e55ab908ee1c6407bf64d4a4743d8");
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnNull_WhenOffsetOutsideRaw()
    {
        var field = new SaveFieldDefinition("id", "name", "int32", 10, 4);

        var value = InvokeStatic("ReadFieldValue", new byte[8], field, "little");

        value.Should().BeNull();
    }

    [Fact]
    public void ReadFieldValue_ShouldDecodeAllSupportedTypes()
    {
        var raw = new byte[64];

        BitConverter.GetBytes(1234).CopyTo(raw, 0);
        BitConverter.GetBytes(4321u).CopyTo(raw, 4);
        BitConverter.GetBytes(9876543210L).CopyTo(raw, 8);
        BitConverter.GetBytes(1.5f).CopyTo(raw, 16);
        BitConverter.GetBytes(2.5d).CopyTo(raw, 20);
        raw[28] = 0x7F;
        raw[29] = 1;
        Encoding.ASCII.GetBytes("ABC\0\0").CopyTo(raw, 30);
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }.CopyTo(raw, 36);

        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f1", "f1", "int32", 0, 4), "little").Should().Be(1234);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f2", "f2", "uint32", 4, 4), "little").Should().Be(4321u);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f3", "f3", "int64", 8, 8), "little").Should().Be(9876543210L);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f4", "f4", "float", 16, 4), "little").Should().Be(1.5f);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f5", "f5", "double", 20, 8), "little").Should().Be(2.5d);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f6", "f6", "byte", 28, 1), "little").Should().Be((byte)0x7F);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f7", "f7", "bool", 29, 1), "little").Should().Be(true);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f8", "f8", "ascii", 30, 5), "little").Should().Be("ABC");
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f9", "f9", "hexlike", 36, 4), "little").Should().Be("DEADBEEF");
    }

    [Fact]
    public void ReadFieldValue_ShouldDecodeBigEndian_WhenRequested()
    {
        var raw = new byte[16];
        new byte[] { 0x00, 0x00, 0x00, 0x2A }.CopyTo(raw, 0);
        new byte[] { 0x40, 0x20, 0x00, 0x00 }.CopyTo(raw, 4);
        new byte[] { 0x40, 0x09, 0x21, 0xFB, 0x54, 0x44, 0x2D, 0x18 }.CopyTo(raw, 8);

        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("i", "i", "int32", 0, 4), "big").Should().Be(42);
        InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("f", "f", "float", 4, 4), "big").Should().Be(2.5f);
        ((double)InvokeStatic("ReadFieldValue", raw, new SaveFieldDefinition("d", "d", "double", 8, 8), "big")!).Should().BeApproximately(Math.PI, 1e-12);
    }

    [Fact]
    public void NormalizePatchValue_ShouldConvertScalarsAndJsonElements()
    {
        using var doc = JsonDocument.Parse("""
        {
          "i32": 10,
          "i64": 922337203685477580,
          "dbl": 3.5,
          "str": "abc",
          "btrue": true,
          "bfalse": false,
          "nullv": null,
          "obj": { "a": 1 }
        }
        """);
        var root = doc.RootElement;

        InvokeStatic("NormalizePatchValue", 10.0, "int32").Should().Be(10);
        InvokeStatic("NormalizePatchValue", 10, "uint32").Should().Be((uint)10);
        InvokeStatic("NormalizePatchValue", 10, "int64").Should().Be(10L);
        InvokeStatic("NormalizePatchValue", 2, "float").Should().Be(2f);
        InvokeStatic("NormalizePatchValue", 2, "double").Should().Be(2d);
        InvokeStatic("NormalizePatchValue", 7, "byte").Should().Be((byte)7);
        InvokeStatic("NormalizePatchValue", "true", "bool").Should().Be(true);
        InvokeStatic("NormalizePatchValue", 123, "ascii").Should().Be("123");
        InvokeStatic("NormalizePatchValue", "raw", "unknown").Should().Be("raw");

        InvokeStatic("NormalizePatchValue", root.GetProperty("i32"), "int32").Should().Be(10);
        InvokeStatic("NormalizePatchValue", root.GetProperty("i64"), "int64").Should().Be(922337203685477580L);
        InvokeStatic("NormalizePatchValue", root.GetProperty("dbl"), "double").Should().Be(3.5d);
        InvokeStatic("NormalizePatchValue", root.GetProperty("str"), "ascii").Should().Be("abc");
        InvokeStatic("NormalizePatchValue", root.GetProperty("btrue"), "bool").Should().Be(true);
        InvokeStatic("NormalizePatchValue", root.GetProperty("bfalse"), "bool").Should().Be(false);
        InvokeStatic("NormalizePatchValue", root.GetProperty("nullv"), "ascii").Should().BeNull();
        InvokeStatic("NormalizePatchValue", root.GetProperty("obj"), "unknown")!.ToString().Should().Contain("\"a\": 1");
    }

    [Fact]
    public void ValuesEqual_ShouldHandleNullAndValueComparisons()
    {
        ((bool)InvokeStatic("ValuesEqual", null, null)!).Should().BeTrue();
        ((bool)InvokeStatic("ValuesEqual", null, 1)!).Should().BeFalse();
        ((bool)InvokeStatic("ValuesEqual", 1, null)!).Should().BeFalse();
        ((bool)InvokeStatic("ValuesEqual", 1, 1)!).Should().BeTrue();
        ((bool)InvokeStatic("ValuesEqual", "a", "b")!).Should().BeFalse();
    }


    [Fact]
    public void BinarySaveCodec_PrivateHelpers_ShouldCoverAdditionalBranches()
    {
        var root = CreateCodecSchemaRoot();
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = root }, NullLogger<BinarySaveCodec>.Instance);

        try
        {
            var raw = new byte[32];
            var intField = new SaveFieldDefinition("i32", "i32", "int32", 0, 4);
            var boolField = new SaveFieldDefinition("flag", "flag", "bool", 4, 1);
            var asciiField = new SaveFieldDefinition("text", "text", "ascii", 8, 4);

            InvokeBinaryStatic("ApplyFieldEdit", raw, intField, 7, "little");
            BitConverter.ToInt32(raw, 0).Should().Be(7);

            InvokeBinaryStatic("ApplyFieldEdit", raw, boolField, true, "little");
            raw[4].Should().Be(1);

            InvokeBinaryStatic("ApplyFieldEdit", raw, asciiField, "AB", "little");
            Encoding.ASCII.GetString(raw, 8, 2).Should().Be("AB");

            var schema = new SaveSchema(
                "schema",
                "build",
                "little",
                new[] { new SaveBlockDefinition("root", "root", 0, 32, "struct", new[] { "i32" }) },
                new[] { intField },
                Array.Empty<SaveArrayDefinition>(),
                new[]
                {
                    new ValidationRule("r1", "field_non_negative", "i32", "neg int"),
                    new ValidationRule("r2", "field_non_negative", "missing", "missing field")
                },
                new[]
                {
                    new ChecksumRule("crc", "crc32", 0, 8, 12, 4),
                    new ChecksumRule("skip", "crc32", 99, 120, 24, 4)
                });

            var ruleRaw = new byte[32];
            BitConverter.GetBytes(-1).CopyTo(ruleRaw, 0);
            var evalRule = InvokeBinaryStatic("EvaluateRule", schema.ValidationRules[0], schema, ruleRaw);
            evalRule.Should().Be("neg int");
            InvokeBinaryStatic("EvaluateRule", schema.ValidationRules[1], schema, raw).Should().BeNull();

            var applyChecksums = typeof(BinarySaveCodec).GetMethod("ApplyChecksums", BindingFlags.Instance | BindingFlags.NonPublic);
            applyChecksums.Should().NotBeNull();
            applyChecksums!.Invoke(codec, new object?[] { schema, raw });

            var checksum = BitConverter.ToUInt32(raw, 12);
            checksum.Should().NotBe(0u);

            var tempSav = Path.Combine(root, "input.sav");
            File.WriteAllBytes(tempSav, new byte[16]);
            InvokeBinaryStatic("NormalizeSaveFilePath", tempSav, true).Should().Be(tempSav);

            var badExt = Path.Combine(root, "bad.txt");
            var missingSav = Path.Combine(root, "missing.sav");
            var badExtCall = () => InvokeBinaryStatic("NormalizeSaveFilePath", badExt, false);
            badExtCall.Should().Throw<TargetInvocationException>();

            var missingCall = () => InvokeBinaryStatic("NormalizeSaveFilePath", missingSav, true);
            missingCall.Should().Throw<TargetInvocationException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BinarySaveCodec_PrivateWriteHelpers_ShouldCoverBigEndianAndErrorBranches()
    {
        var raw = new byte[24];

        InvokeBinaryStatic("ApplyFieldEdit", raw, new SaveFieldDefinition("i32", "i32", "int32", 0, 4), 0x01020304, "big");
        raw[0].Should().Be(0x01);

        InvokeBinaryStatic("ApplyFieldEdit", raw, new SaveFieldDefinition("u32", "u32", "uint32", 4, 4), 0x01020304u, "big");
        raw[4].Should().Be(0x01);

        InvokeBinaryStatic("ApplyFieldEdit", raw, new SaveFieldDefinition("i64", "i64", "int64", 8, 8), 0x0102030405060708L, "big");
        raw[8].Should().Be(0x01);

        var overflowField = new SaveFieldDefinition("f", "f", "float", 16, 2);
        var overflow = () => InvokeBinaryStatic("ApplyFieldEdit", raw, overflowField, 1.25f, "little");
        overflow.Should().Throw<TargetInvocationException>();

        var unsupportedField = new SaveFieldDefinition("x", "x", "vector3", 0, 4);
        var unsupported = () => InvokeBinaryStatic("ApplyFieldEdit", new byte[8], unsupportedField, "1,2,3", "little");
        unsupported.Should().Throw<TargetInvocationException>();
    }

    [Fact]
    public void BinarySaveCodec_EvaluateRule_AndApplyChecksums_ShouldCoverAdditionalBranches()
    {
        var root = CreateCodecSchemaRoot();
        var codec = new BinarySaveCodec(new SaveOptions { SchemaRootPath = root }, NullLogger<BinarySaveCodec>.Instance);

        try
        {
            var intField = new SaveFieldDefinition("i32", "i32", "int32", 0, 4);
            var longField = new SaveFieldDefinition("i64", "i64", "int64", 8, 8);
            var schema = new SaveSchema(
                "schema",
                "build",
                "little",
                new[] { new SaveBlockDefinition("root", "root", 0, 32, "struct", new[] { "i32", "i64" }) },
                new[] { intField, longField },
                Array.Empty<SaveArrayDefinition>(),
                new[] { new ValidationRule("r-long", "field_non_negative", "i64", "neg long") },
                new[]
                {
                    new ChecksumRule("unknown", "adler32", 0, 8, 16, 4),
                    new ChecksumRule("oob", "crc32", 40, 45, 20, 4)
                });

            var raw = new byte[32];
            BitConverter.GetBytes(-5L).CopyTo(raw, 8);
            var eval = InvokeBinaryStatic("EvaluateRule", schema.ValidationRules[0], schema, raw);
            eval.Should().Be("neg long");

            var applyChecksums = typeof(BinarySaveCodec).GetMethod("ApplyChecksums", BindingFlags.Instance | BindingFlags.NonPublic);
            applyChecksums.Should().NotBeNull();
            applyChecksums!.Invoke(codec, new object?[] { schema, raw });

            BitConverter.ToUInt32(raw, 16).Should().Be(0u);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
    private static string CreateCodecSchemaRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-codec-branch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static object? InvokeBinaryStatic(string methodName, params object?[] args)
    {
        var method = typeof(BinarySaveCodec).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected BinarySaveCodec private static method {methodName}");
        return method!.Invoke(null, args);
    }
    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = CodecType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected static method {methodName}");
        return method!.Invoke(null, args);
    }
}

#pragma warning restore CA1014

