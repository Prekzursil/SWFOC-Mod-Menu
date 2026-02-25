#pragma warning disable S4136
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Checksum;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Internal;

namespace SwfocTrainer.Saves.Services;

public sealed class BinarySaveCodec : ISaveCodec
{
    private static readonly string[] AllowedSaveExtensions = [".sav"];
    private readonly SaveSchemaRepository _schemaRepository;
    private readonly ILogger<BinarySaveCodec> _logger;

    public BinarySaveCodec(SaveOptions options, ILogger<BinarySaveCodec> logger)
    {
        _schemaRepository = new SaveSchemaRepository(options);
        _logger = logger;
    }

    public async Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeSaveFilePath(path, requireExistingFile: true);
        var schema = await _schemaRepository.LoadSchemaAsync(schemaId, cancellationToken);
        var bytes = await File.ReadAllBytesAsync(normalizedPath, cancellationToken);
        var root = BuildNodeTree(schema, bytes);
        return new SaveDocument(normalizedPath, schemaId, bytes, root);
    }

    public async Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken)
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

    public async Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken)
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

    public async Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken)
    {
        var normalizedOutput = NormalizeSaveFilePath(outputPath, requireExistingFile: false);
        var outputDirectory = Path.GetDirectoryName(normalizedOutput);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException($"Output path '{outputPath}' has no parent directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        var schema = await _schemaRepository.LoadSchemaAsync(document.SchemaId, cancellationToken);
        ApplyChecksums(schema, document.Raw);
        await File.WriteAllBytesAsync(normalizedOutput, document.Raw, cancellationToken);
    }

    public async Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var tempPath = NormalizeSaveFilePath(
            Path.Combine(tempRoot, $"swfoc-roundtrip-{Guid.NewGuid():N}.sav"),
            requireExistingFile: false);
        TrustedPathPolicy.EnsureSubPath(tempRoot, tempPath);
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

    public Task<SaveDocument> LoadAsync(string path, string schemaId)
    {
        return LoadAsync(path, schemaId, CancellationToken.None);
    }

    public Task EditAsync(SaveDocument document, string nodePath, object? value)
    {
        return EditAsync(document, nodePath, value, CancellationToken.None);
    }

    public Task<SaveValidationResult> ValidateAsync(SaveDocument document)
    {
        return ValidateAsync(document, CancellationToken.None);
    }

    public Task WriteAsync(SaveDocument document, string outputPath)
    {
        return WriteAsync(document, outputPath, CancellationToken.None);
    }

    public Task<bool> RoundTripCheckAsync(SaveDocument document)
    {
        return RoundTripCheckAsync(document, CancellationToken.None);
    }

    private static string NormalizeSaveFilePath(string path, bool requireExistingFile)
    {
        var normalized = TrustedPathPolicy.NormalizeAbsolute(path);
        TrustedPathPolicy.EnsureAllowedExtension(normalized, AllowedSaveExtensions);
        if (requireExistingFile && !File.Exists(normalized))
        {
            throw new FileNotFoundException($"Save file '{normalized}' does not exist.", normalized);
        }

        return normalized;
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
            "double" when field.Length >= 8 => ReadDouble(span[..8], little),
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

    private static double ReadDouble(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        var buffer = bytes.ToArray();
        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(buffer);
        }

        return BitConverter.ToDouble(buffer, 0);
    }

    private static void ApplyFieldEdit(byte[] raw, SaveFieldDefinition field, object? value, string endianness)
    {
        if (field.Offset + field.Length > raw.Length)
        {
            throw new InvalidOperationException($"Field '{field.Id}' points outside save bounds.");
        }

        var little = !endianness.Equals("big", StringComparison.OrdinalIgnoreCase);
        var span = raw.AsSpan(field.Offset, field.Length);
        var valueType = field.ValueType.ToLowerInvariant();

        if (TryWriteIntegerField(valueType, span, value, little))
        {
            return;
        }

        switch (valueType)
        {
            case "byte":
            {
                span[0] = Convert.ToByte(value, CultureInfo.InvariantCulture);
                break;
            }
            case "float":
            {
                WriteFloatingPoint(span, BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture)), little);
                break;
            }
            case "double":
            {
                WriteFloatingPoint(span, BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture)), little);
                break;
            }
            case "bool":
            {
                span[0] = Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? (byte)1 : (byte)0;
                break;
            }
            case "ascii":
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                var bytes = Encoding.ASCII.GetBytes(text);
                span.Clear();
                var copyLength = Math.Min(bytes.Length, span.Length);
                bytes.AsSpan(0, copyLength).CopyTo(span);
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported field type for editing: {field.ValueType}");
        }
    }

    private static bool TryWriteIntegerField(string valueType, Span<byte> span, object? value, bool littleEndian)
    {
        switch (valueType)
        {
            case "int32":
                WriteInt32(span, Convert.ToInt32(value, CultureInfo.InvariantCulture), littleEndian);
                return true;
            case "uint32":
                WriteUInt32(span, Convert.ToUInt32(value, CultureInfo.InvariantCulture), littleEndian);
                return true;
            case "int64":
                WriteInt64(span, Convert.ToInt64(value, CultureInfo.InvariantCulture), littleEndian);
                return true;
            default:
                return false;
        }
    }

    private static void WriteInt32(Span<byte> span, int value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span, value);
            return;
        }

        BinaryPrimitives.WriteInt32BigEndian(span, value);
    }

    private static void WriteUInt32(Span<byte> span, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span, value);
            return;
        }

        BinaryPrimitives.WriteUInt32BigEndian(span, value);
    }

    private static void WriteInt64(Span<byte> span, long value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt64LittleEndian(span, value);
            return;
        }

        BinaryPrimitives.WriteInt64BigEndian(span, value);
    }

    private static void WriteFloatingPoint(Span<byte> target, byte[] sourceBytes, bool littleEndian)
    {
        if (sourceBytes.Length > target.Length)
        {
            throw new InvalidOperationException("Floating point source bytes exceed target field length.");
        }

        if (BitConverter.IsLittleEndian != littleEndian)
        {
            Array.Reverse(sourceBytes);
        }

        target.Clear();
        sourceBytes.AsSpan().CopyTo(target);
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
