using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Internal;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavesWave10CoverageTests
{
    // ── SavePatchFieldCodec: NormalizePatchValue ascii branch (line 62) ──
    [Fact]
    public void NormalizePatchValue_Ascii_ReturnsString()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("hello", "ascii");
        result.Should().Be("hello");
    }

    [Fact]
    public void NormalizePatchValue_Bool_ReturnsBool()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("true", "bool");
        result.Should().Be(true);
    }

    [Fact]
    public void NormalizePatchValue_Byte_ReturnsByte()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("42", "byte");
        result.Should().Be((byte)42);
    }

    [Fact]
    public void NormalizePatchValue_Double_ReturnsDouble()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("3.14", "double");
        result.Should().BeOfType<double>();
    }

    [Fact]
    public void NormalizePatchValue_Int64_ReturnsLong()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("9999999999", "int64");
        result.Should().Be(9999999999L);
    }

    [Fact]
    public void NormalizePatchValue_UInt32_ReturnsUint()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("42", "uint32");
        result.Should().Be(42u);
    }

    [Fact]
    public void NormalizePatchValue_UnknownType_ReturnsRawScalar()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("raw", "custom_type");
        result.Should().Be("raw");
    }

    [Fact]
    public void NormalizePatchValue_Null_ReturnsNull()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue(null, "int32");
        result.Should().BeNull();
    }

    // ── SavePatchFieldCodec: ValuesEqual branches ──
    [Fact]
    public void ValuesEqual_BothNull_ReturnsTrue()
    {
        SavePatchFieldCodec.ValuesEqual(null, null).Should().BeTrue();
    }

    [Fact]
    public void ValuesEqual_OneNull_ReturnsFalse()
    {
        SavePatchFieldCodec.ValuesEqual(null, 42).Should().BeFalse();
        SavePatchFieldCodec.ValuesEqual(42, null).Should().BeFalse();
    }

    // ── SavePatchFieldCodec: ReadFieldValue edge cases ──
    [Fact]
    public void ReadFieldValue_OutOfRange_ReturnsNull()
    {
        var raw = new byte[4];
        var field = new SaveFieldDefinition("f1", "f1", "int32", 10, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().BeNull();
    }

    [Fact]
    public void ReadFieldValue_BigEndianInt32_ReadsCorrectly()
    {
        var raw = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(0, 4), 12345);
        var field = new SaveFieldDefinition("f1", "f1", "int32", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "big").Should().Be(12345);
    }

    [Fact]
    public void ReadFieldValue_UInt32BigEndian_ReadsCorrectly()
    {
        var raw = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(0, 4), 99u);
        var field = new SaveFieldDefinition("f1", "f1", "uint32", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "big").Should().Be(99u);
    }

    [Fact]
    public void ReadFieldValue_Int64BigEndian_ReadsCorrectly()
    {
        var raw = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(raw.AsSpan(0, 8), 123456789L);
        var field = new SaveFieldDefinition("f1", "f1", "int64", 0, 8);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "big").Should().Be(123456789L);
    }

    [Fact]
    public void ReadFieldValue_BoolTrue_ReadsCorrectly()
    {
        var raw = new byte[] { 1, 0, 0, 0 };
        var field = new SaveFieldDefinition("f1", "f1", "bool", 0, 1);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be(true);
    }

    [Fact]
    public void ReadFieldValue_BoolFalse_ReadsCorrectly()
    {
        var raw = new byte[] { 0, 0, 0, 0 };
        var field = new SaveFieldDefinition("f1", "f1", "bool", 0, 1);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be(false);
    }

    [Fact]
    public void ReadFieldValue_Ascii_ReadsCorrectly()
    {
        var raw = Encoding.ASCII.GetBytes("HELLO\0\0\0");
        var field = new SaveFieldDefinition("f1", "f1", "ascii", 0, 8);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be("HELLO");
    }

    [Fact]
    public void ReadFieldValue_UnknownType_ReturnsHex()
    {
        var raw = new byte[] { 0xAB, 0xCD };
        var field = new SaveFieldDefinition("f1", "f1", "custom", 0, 2);
        var result = (string)SavePatchFieldCodec.ReadFieldValue(raw, field, "little")!;
        result.Should().Be("ABCD");
    }

    // ── ComputeSha256Hex ──
    [Fact]
    public void ComputeSha256Hex_KnownInput_ReturnsExpectedHash()
    {
        var result = SavePatchFieldCodec.ComputeSha256Hex(new byte[] { 1, 2, 3 });
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().HaveLength(64); // SHA-256 = 32 bytes = 64 hex chars
    }
}
