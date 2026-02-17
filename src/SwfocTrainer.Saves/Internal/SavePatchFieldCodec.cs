using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Saves.Internal;

internal static class SavePatchFieldCodec
{
    public static string ComputeSha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static object? ReadFieldValue(byte[] raw, SaveFieldDefinition field, string endianness)
    {
        if (field.Offset < 0 || field.Offset + field.Length > raw.Length)
        {
            return null;
        }

        var span = raw.AsSpan(field.Offset, field.Length);
        var little = !endianness.Equals("big", StringComparison.OrdinalIgnoreCase);

        return field.ValueType.ToLowerInvariant() switch
        {
            "int32" when field.Length >= 4 => little ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span),
            "uint32" when field.Length >= 4 => little ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span),
            "int64" when field.Length >= 8 => little ? BinaryPrimitives.ReadInt64LittleEndian(span) : BinaryPrimitives.ReadInt64BigEndian(span),
            "float" when field.Length >= 4 => ReadSingle(span[..4], little),
            "double" when field.Length >= 8 => ReadDouble(span[..8], little),
            "byte" => span[0],
            "bool" => span[0] != 0,
            "ascii" => System.Text.Encoding.ASCII.GetString(span).TrimEnd('\0'),
            _ => Convert.ToHexString(span)
        };
    }

    public static object? NormalizePatchValue(object? rawValue, string valueType)
    {
        var scalar = rawValue is JsonElement element ? ExtractJsonElementScalar(element) : rawValue;
        if (scalar is null)
        {
            return null;
        }

        return valueType.ToLowerInvariant() switch
        {
            "int32" => Convert.ToInt32(scalar, CultureInfo.InvariantCulture),
            "uint32" => Convert.ToUInt32(scalar, CultureInfo.InvariantCulture),
            "int64" => Convert.ToInt64(scalar, CultureInfo.InvariantCulture),
            "float" => Convert.ToSingle(scalar, CultureInfo.InvariantCulture),
            "double" => Convert.ToDouble(scalar, CultureInfo.InvariantCulture),
            "byte" => Convert.ToByte(scalar, CultureInfo.InvariantCulture),
            "bool" => Convert.ToBoolean(scalar, CultureInfo.InvariantCulture),
            "ascii" => Convert.ToString(scalar, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => scalar
        };
    }

    public static bool ValuesEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    private static object? ExtractJsonElementScalar(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var i32) => i32,
            JsonValueKind.Number when element.TryGetInt64(out var i64) => i64,
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        var buffer = bytes.ToArray();
        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(buffer);
        }

        return BitConverter.ToSingle(buffer, 0);
    }

    private static double ReadDouble(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        var buffer = bytes.ToArray();
        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(buffer);
        }

        return BitConverter.ToDouble(buffer, 0);
    }
}
