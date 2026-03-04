using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Models;
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

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = CodecType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected static method {methodName}");
        return method!.Invoke(null, args);
    }
}


