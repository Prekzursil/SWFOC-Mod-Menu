using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Checksum;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Internal;

namespace SwfocTrainer.Saves.Services;

public sealed class BinarySaveCodec : ISaveCodec
{
    private readonly SaveSchemaRepository _schemaRepository;
    private readonly ILogger<BinarySaveCodec> _logger;

    public BinarySaveCodec(SaveOptions options, ILogger<BinarySaveCodec> logger)
    {
        _schemaRepository = new SaveSchemaRepository(options);
        _logger = logger;
    }

    public async Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken = default)
    {
        var schema = await _schemaRepository.LoadSchemaAsync(schemaId, cancellationToken);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var root = BuildNodeTree(schema, bytes);
        return new SaveDocument(path, schemaId, bytes, root);
    }

    public async Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken = default)
    {
        var schema = await _schemaRepository.LoadSchemaAsync(document.SchemaId, cancellationToken);

        var targetField = schema.FieldDefs.FirstOrDefault(f =>
            string.Equals(f.Path, nodePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Id, nodePath, StringComparison.OrdinalIgnoreCase));

        if (targetField is null)
        {
            throw new InvalidOperationException($"Field '{nodePath}' not found in schema '{schema.SchemaId}'.");
        }

        ApplyFieldEdit(document.Raw, targetField, value, schema.Endianness);
    }

    public async Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken = default)
    {
        var schema = await _schemaRepository.LoadSchemaAsync(document.SchemaId, cancellationToken);
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var block in schema.RootBlocks)
        {
            if (block.Offset < 0 || block.Offset + block.Length > document.Raw.Length)
            {
                errors.Add($"Block '{block.Id}' is out of range ({block.Offset}..{block.Offset + block.Length})");
            }
        }

        foreach (var field in schema.FieldDefs)
        {
            if (field.Offset < 0 || field.Offset + field.Length > document.Raw.Length)
            {
                errors.Add($"Field '{field.Id}' is out of range ({field.Offset}..{field.Offset + field.Length})");
            }
        }

        foreach (var rule in schema.ValidationRules)
        {
            var outcome = EvaluateRule(rule, schema, document.Raw);
            if (outcome is null)
            {
                continue;
            }

            if (rule.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(outcome);
            }
            else
            {
                errors.Add(outcome);
            }
        }

        return new SaveValidationResult(errors.Count == 0, errors, warnings);
    }

    public async Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken = default)
    {
        var schema = await _schemaRepository.LoadSchemaAsync(document.SchemaId, cancellationToken);
        ApplyChecksums(schema, document.Raw);
        await File.WriteAllBytesAsync(outputPath, document.Raw, cancellationToken);
    }

    public async Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-roundtrip-{Guid.NewGuid():N}.sav");
        try
        {
            await WriteAsync(document, tempPath, cancellationToken);
            var reloaded = await LoadAsync(tempPath, document.SchemaId, cancellationToken);
            return reloaded.Raw.SequenceEqual(document.Raw);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static SaveNode BuildNodeTree(SaveSchema schema, byte[] raw)
    {
        var children = new List<SaveNode>();

        foreach (var block in schema.RootBlocks)
        {
            var blockChildren = new List<SaveNode>();
            var fieldIds = block.Fields ?? Array.Empty<string>();
            foreach (var fieldId in fieldIds)
            {
                var field = schema.FieldDefs.FirstOrDefault(x => x.Id.Equals(fieldId, StringComparison.OrdinalIgnoreCase));
                if (field is null)
                {
                    continue;
                }

                var value = ReadFieldValue(raw, field, schema.Endianness);
                blockChildren.Add(new SaveNode(field.Path ?? field.Id, field.Name, field.ValueType, value));
            }

            children.Add(new SaveNode(block.Id, block.Name, block.Type, null, blockChildren));
        }

        return new SaveNode("/", "Root", "root", null, children);
    }

    private static object? ReadFieldValue(byte[] raw, SaveFieldDefinition field, string endianness)
    {
        if (field.Offset + field.Length > raw.Length)
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
            "byte" => span[0],
            "bool" => span[0] != 0,
            "ascii" => System.Text.Encoding.ASCII.GetString(span).TrimEnd('\0'),
            _ => Convert.ToHexString(span)
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

    private static void ApplyFieldEdit(byte[] raw, SaveFieldDefinition field, object? value, string endianness)
    {
        if (field.Offset + field.Length > raw.Length)
        {
            throw new InvalidOperationException($"Field '{field.Id}' points outside save bounds.");
        }

        var little = !endianness.Equals("big", StringComparison.OrdinalIgnoreCase);
        var span = raw.AsSpan(field.Offset, field.Length);

        switch (field.ValueType.ToLowerInvariant())
        {
            case "int32":
            {
                var intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (little)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(span, intValue);
                }
                else
                {
                    BinaryPrimitives.WriteInt32BigEndian(span, intValue);
                }

                break;
            }
            case "uint32":
            {
                var uintValue = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                if (little)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(span, uintValue);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32BigEndian(span, uintValue);
                }

                break;
            }
            case "int64":
            {
                var longValue = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                if (little)
                {
                    BinaryPrimitives.WriteInt64LittleEndian(span, longValue);
                }
                else
                {
                    BinaryPrimitives.WriteInt64BigEndian(span, longValue);
                }

                break;
            }
            case "byte":
            {
                span[0] = Convert.ToByte(value, CultureInfo.InvariantCulture);
                break;
            }
            case "bool":
            {
                span[0] = Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? (byte)1 : (byte)0;
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported field type for editing: {field.ValueType}");
        }
    }

    private static string? EvaluateRule(ValidationRule rule, SaveSchema schema, byte[] raw)
    {
        if (rule.Rule.Equals("field_non_negative", StringComparison.OrdinalIgnoreCase))
        {
            var field = schema.FieldDefs.FirstOrDefault(x => x.Id.Equals(rule.Target, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                return null;
            }

            var value = ReadFieldValue(raw, field, schema.Endianness);
            if (value is int intValue && intValue < 0)
            {
                return rule.Message;
            }

            if (value is long longValue && longValue < 0)
            {
                return rule.Message;
            }
        }

        return null;
    }

    private void ApplyChecksums(SaveSchema schema, byte[] raw)
    {
        foreach (var rule in schema.ChecksumRules)
        {
            if (rule.StartOffset < 0 || rule.EndOffset > raw.Length || rule.OutputOffset < 0 || rule.OutputOffset + rule.OutputLength > raw.Length)
            {
                _logger.LogWarning("Skipping checksum rule {RuleId}: out of bounds", rule.Id);
                continue;
            }

            var slice = raw.AsSpan(rule.StartOffset, rule.EndOffset - rule.StartOffset);
            uint checksum = rule.Algorithm.ToLowerInvariant() switch
            {
                "crc32" => Crc32.Compute(slice),
                _ => 0
            };

            var output = raw.AsSpan(rule.OutputOffset, rule.OutputLength);
            if (output.Length >= 4)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(output, checksum);
            }
        }
    }
}
