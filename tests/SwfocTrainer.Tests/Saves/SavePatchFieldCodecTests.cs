using System.Buffers.Binary;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Internal;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SavePatchFieldCodecTests
{
    [Fact]
    public void ComputeSha256Hex_ShouldReturnLowercaseHexHash()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var hash = SavePatchFieldCodec.ComputeSha256Hex(bytes);
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().Be(hash.ToLowerInvariant());
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeSha256Hex_ShouldThrow_WhenNull()
    {
        var act = () => SavePatchFieldCodec.ComputeSha256Hex(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadFieldValue_ShouldThrow_WhenRawIsNull()
    {
        var field = MakeField("test", "int32", 0, 4);
        var act = () => SavePatchFieldCodec.ReadFieldValue(null!, field, "little");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadFieldValue_ShouldThrow_WhenFieldIsNull()
    {
        var act = () => SavePatchFieldCodec.ReadFieldValue(new byte[4], null!, "little");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadFieldValue_ShouldThrow_WhenEndiannessIsNull()
    {
        var field = MakeField("test", "int32", 0, 4);
        var act = () => SavePatchFieldCodec.ReadFieldValue(new byte[4], field, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnNull_WhenOffsetIsNegative()
    {
        var field = MakeField("test", "int32", -1, 4);
        SavePatchFieldCodec.ReadFieldValue(new byte[8], field, "little").Should().BeNull();
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnNull_WhenFieldExceedsBounds()
    {
        var field = MakeField("test", "int32", 6, 4);
        SavePatchFieldCodec.ReadFieldValue(new byte[8], field, "little").Should().BeNull();
    }

    [Fact]
    public void ReadFieldValue_ShouldReadInt32LittleEndian()
    {
        var raw = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), 42);
        var field = MakeField("test", "int32", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be(42);
    }

    [Fact]
    public void ReadFieldValue_ShouldReadInt32BigEndian()
    {
        var raw = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(raw.AsSpan(0, 4), 42);
        var field = MakeField("test", "int32", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "big").Should().Be(42);
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnHex_WhenInt32FieldTooShort()
    {
        var raw = new byte[2];
        var field = MakeField("test", "int32", 0, 2);
        var result = SavePatchFieldCodec.ReadFieldValue(raw, field, "little");
        result.Should().BeOfType<string>();
    }

    [Fact]
    public void ReadFieldValue_ShouldReadUInt32LittleEndian()
    {
        var raw = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0, 4), 99u);
        var field = MakeField("test", "uint32", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be(99u);
    }

    [Fact]
    public void ReadFieldValue_ShouldReadUInt32BigEndian()
    {
        var raw = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(0, 4), 99u);
        var field = MakeField("test", "uint32", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "big").Should().Be(99u);
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnHex_WhenUInt32FieldTooShort()
    {
        var raw = new byte[2];
        var field = MakeField("test", "uint32", 0, 2);
        var result = SavePatchFieldCodec.ReadFieldValue(raw, field, "little");
        result.Should().BeOfType<string>();
    }

    [Fact]
    public void ReadFieldValue_ShouldReadInt64LittleEndian()
    {
        var raw = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(0, 8), 123456789L);
        var field = MakeField("test", "int64", 0, 8);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be(123456789L);
    }

    [Fact]
    public void ReadFieldValue_ShouldReadInt64BigEndian()
    {
        var raw = new byte[16];
        BinaryPrimitives.WriteInt64BigEndian(raw.AsSpan(0, 8), 123456789L);
        var field = MakeField("test", "int64", 0, 8);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "big").Should().Be(123456789L);
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnHex_WhenInt64FieldTooShort()
    {
        var raw = new byte[4];
        var field = MakeField("test", "int64", 0, 4);
        var result = SavePatchFieldCodec.ReadFieldValue(raw, field, "little");
        result.Should().BeOfType<string>();
    }

    [Fact]
    public void ReadFieldValue_ShouldReadFloatLittleEndian()
    {
        var raw = new byte[8];
        var floatBytes = BitConverter.GetBytes(3.14f);
        floatBytes.CopyTo(raw, 0);
        var field = MakeField("test", "float", 0, 4);
        var result = (float)SavePatchFieldCodec.ReadFieldValue(raw, field, "little")!;
        result.Should().BeApproximately(3.14f, 0.001f);
    }

    [Fact]
    public void ReadFieldValue_ShouldReadFloatBigEndian()
    {
        var raw = new byte[8];
        var floatBytes = BitConverter.GetBytes(3.14f);
        if (BitConverter.IsLittleEndian) Array.Reverse(floatBytes);
        floatBytes.CopyTo(raw, 0);
        var field = MakeField("test", "float", 0, 4);
        var result = (float)SavePatchFieldCodec.ReadFieldValue(raw, field, "big")!;
        result.Should().BeApproximately(3.14f, 0.001f);
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnHex_WhenFloatFieldTooShort()
    {
        var raw = new byte[2];
        var field = MakeField("test", "float", 0, 2);
        var result = SavePatchFieldCodec.ReadFieldValue(raw, field, "little");
        result.Should().BeOfType<string>();
    }

    [Fact]
    public void ReadFieldValue_ShouldReadDoubleLittleEndian()
    {
        var raw = new byte[16];
        var doubleBytes = BitConverter.GetBytes(9.87654321d);
        doubleBytes.CopyTo(raw, 0);
        var field = MakeField("test", "double", 0, 8);
        var result = (double)SavePatchFieldCodec.ReadFieldValue(raw, field, "little")!;
        result.Should().BeApproximately(9.87654321d, 0.0001d);
    }

    [Fact]
    public void ReadFieldValue_ShouldReadDoubleBigEndian()
    {
        var raw = new byte[16];
        var doubleBytes = BitConverter.GetBytes(9.87654321d);
        if (BitConverter.IsLittleEndian) Array.Reverse(doubleBytes);
        doubleBytes.CopyTo(raw, 0);
        var field = MakeField("test", "double", 0, 8);
        var result = (double)SavePatchFieldCodec.ReadFieldValue(raw, field, "big")!;
        result.Should().BeApproximately(9.87654321d, 0.0001d);
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnHex_WhenDoubleFieldTooShort()
    {
        var raw = new byte[4];
        var field = MakeField("test", "double", 0, 4);
        var result = SavePatchFieldCodec.ReadFieldValue(raw, field, "little");
        result.Should().BeOfType<string>();
    }

    [Fact]
    public void ReadFieldValue_ShouldReadByte()
    {
        var raw = new byte[] { 0xAB };
        var field = MakeField("test", "byte", 0, 1);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be((byte)0xAB);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(255, true)]
    public void ReadFieldValue_ShouldReadBool(byte rawValue, bool expected)
    {
        var raw = new byte[] { rawValue };
        var field = MakeField("test", "bool", 0, 1);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be(expected);
    }

    [Fact]
    public void ReadFieldValue_ShouldReadAsciiAndTrimNulls()
    {
        var raw = new byte[] { (byte)'H', (byte)'i', 0, 0 };
        var field = MakeField("test", "ascii", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "little").Should().Be("Hi");
    }

    [Fact]
    public void ReadFieldValue_ShouldReturnHexForUnknownType()
    {
        var raw = new byte[] { 0xDE, 0xAD };
        var field = MakeField("test", "unknown_type", 0, 2);
        var result = SavePatchFieldCodec.ReadFieldValue(raw, field, "little");
        result.Should().Be("DEAD");
    }

    [Fact]
    public void NormalizePatchValue_ShouldThrow_WhenValueTypeIsNull()
    {
        var act = () => SavePatchFieldCodec.NormalizePatchValue(42, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NormalizePatchValue_ShouldReturnNull_WhenScalarIsNull()
    {
        SavePatchFieldCodec.NormalizePatchValue(null, "int32").Should().BeNull();
    }

    [Theory]
    [InlineData("int32", 42, typeof(int))]
    [InlineData("uint32", 42, typeof(uint))]
    [InlineData("int64", 42, typeof(long))]
    [InlineData("float", 42, typeof(float))]
    [InlineData("double", 42, typeof(double))]
    [InlineData("byte", 42, typeof(byte))]
    [InlineData("bool", true, typeof(bool))]
    [InlineData("ascii", "hello", typeof(string))]
    public void NormalizePatchValue_ShouldConvertToExpectedType(string valueType, object input, Type expectedType)
    {
        var result = SavePatchFieldCodec.NormalizePatchValue(input, valueType);
        result.Should().NotBeNull();
        result!.GetType().Should().Be(expectedType);
    }

    [Fact]
    public void NormalizePatchValue_ShouldPassThroughUnknownType()
    {
        var result = SavePatchFieldCodec.NormalizePatchValue("abc", "unknown");
        result.Should().Be("abc");
    }

    [Fact]
    public void NormalizePatchValue_ShouldExtractJsonElementInt32()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("42");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "int32");
        result.Should().Be(42);
    }

    [Fact]
    public void NormalizePatchValue_ShouldExtractJsonElementInt64()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("3000000000");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "int64");
        result.Should().Be(3000000000L);
    }

    [Fact]
    public void NormalizePatchValue_ShouldExtractJsonElementDouble()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("3.14");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "double");
        result.Should().NotBeNull();
    }

    [Fact]
    public void NormalizePatchValue_ShouldExtractJsonElementString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("\"hello\"");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "ascii");
        result.Should().Be("hello");
    }

    [Fact]
    public void NormalizePatchValue_ShouldExtractJsonElementTrue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("true");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "bool");
        result.Should().Be(true);
    }

    [Fact]
    public void NormalizePatchValue_ShouldExtractJsonElementFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("false");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "bool");
        result.Should().Be(false);
    }

    [Fact]
    public void NormalizePatchValue_ShouldReturnNull_ForJsonElementNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("null");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "int32");
        result.Should().BeNull();
    }

    [Fact]
    public void NormalizePatchValue_ShouldReturnRawText_ForJsonElementArray()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("[1,2,3]");
        var result = SavePatchFieldCodec.NormalizePatchValue(json, "unknown");
        result.Should().NotBeNull();
        result!.ToString().Should().Contain("1");
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, 42, false)]
    [InlineData(42, null, false)]
    [InlineData(42, 42, true)]
    [InlineData(42, 43, false)]
    public void ValuesEqual_ShouldCompareCorrectly(object? left, object? right, bool expected)
    {
        SavePatchFieldCodec.ValuesEqual(left, right).Should().Be(expected);
    }

    [Fact]
    public void ReadFieldValue_ShouldHandleNonBigEndianness_AsLittle()
    {
        var raw = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), 77);
        var field = MakeField("test", "int32", 0, 4);
        SavePatchFieldCodec.ReadFieldValue(raw, field, "LITTLE").Should().Be(77);
    }

    private static SaveFieldDefinition MakeField(string id, string valueType, int offset, int length)
        => new(id, id, valueType, offset, length, Path: $"/{id}");
}
