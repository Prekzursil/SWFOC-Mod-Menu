using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Internal;

namespace SwfocTrainer.Saves.Services;

/// <summary>
/// Schema-path patch-pack export/import and compatibility checks.
/// </summary>
public sealed class SavePatchPackService : ISavePatchPackService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SaveSchemaRepository _schemaRepository;

    public SavePatchPackService(SaveOptions options)
    {
        _schemaRepository = new SaveSchemaRepository(options);
    }

    public async Task<SavePatchPack> ExportAsync(
        SaveDocument originalDoc,
        SaveDocument editedDoc,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new InvalidOperationException("Profile ID is required for patch-pack export.");
        }

        if (!string.Equals(originalDoc.SchemaId, editedDoc.SchemaId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Original and edited save documents must use the same schema ID.");
        }

        var schema = await _schemaRepository.LoadSchemaAsync(originalDoc.SchemaId, cancellationToken);
        var checksumOutputRanges = BuildChecksumOutputRanges(schema);
        var operations = new List<SavePatchOperation>();

        foreach (var field in schema.FieldDefs.OrderBy(x => x.Offset).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (FieldOverlapsChecksumOutput(field, checksumOutputRanges))
            {
                continue;
            }

            var oldValue = SavePatchFieldCodec.ReadFieldValue(originalDoc.Raw, field, schema.Endianness);
            var newValue = SavePatchFieldCodec.ReadFieldValue(editedDoc.Raw, field, schema.Endianness);
            if (SavePatchFieldCodec.ValuesEqual(oldValue, newValue))
            {
                continue;
            }

            operations.Add(new SavePatchOperation(
                SavePatchOperationKind.SetValue,
                field.Path ?? field.Id,
                field.Id,
                field.ValueType,
                oldValue,
                newValue,
                field.Offset));
        }

        var metadata = new SavePatchMetadata(
            SchemaVersion: "1.0",
            ProfileId: profileId,
            SchemaId: schema.SchemaId,
            SourceHash: SavePatchFieldCodec.ComputeSha256Hex(originalDoc.Raw),
            CreatedAtUtc: DateTimeOffset.UtcNow);

        var compatibility = new SavePatchCompatibility(
            AllowedProfileIds: [profileId],
            RequiredSchemaId: schema.SchemaId,
            SaveBuildHint: schema.GameBuild);

        return new SavePatchPack(metadata, compatibility, operations);
    }

    public async Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = TrustedPathPolicy.NormalizeAbsolute(path);
        TrustedPathPolicy.EnsureAllowedExtension(normalizedPath, ".json");
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException($"Save patch-pack file not found: {normalizedPath}", normalizedPath);
        }

        var json = await File.ReadAllTextAsync(normalizedPath, cancellationToken);
        using var rawDocument = JsonDocument.Parse(json);
        var rawContractErrors = ValidateRawPackContract(rawDocument.RootElement);
        if (rawContractErrors.Count > 0)
        {
            throw new InvalidDataException($"Invalid save patch-pack contract: {string.Join("; ", rawContractErrors)}");
        }

        var pack = JsonSerializer.Deserialize<SavePatchPack>(json, JsonOptions);
        if (pack is null)
        {
            throw new InvalidDataException("Save patch-pack JSON could not be deserialized.");
        }

        var normalizedPack = pack with
        {
            Operations = pack.Operations
                .Select(x => x with
                {
                    OldValue = SavePatchFieldCodec.NormalizePatchValue(x.OldValue, x.ValueType),
                    NewValue = SavePatchFieldCodec.NormalizePatchValue(x.NewValue, x.ValueType)
                })
                .ToArray()
        };

        var contractErrors = ValidatePackContract(normalizedPack);
        if (contractErrors.Count > 0)
        {
            throw new InvalidDataException($"Invalid save patch-pack contract: {string.Join("; ", contractErrors)}");
        }

        return normalizedPack;
    }

    public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var warnings = new List<string>();

        if (!string.Equals(pack.Metadata.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            errors.Add($"Unsupported patch-pack schemaVersion '{pack.Metadata.SchemaVersion}'.");
        }

        if (!string.Equals(pack.Compatibility.RequiredSchemaId, targetDoc.SchemaId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Schema mismatch: pack requires '{pack.Compatibility.RequiredSchemaId}', target save is '{targetDoc.SchemaId}'.");
        }

        var allowedProfiles = pack.Compatibility.AllowedProfileIds ?? Array.Empty<string>();
        var wildcardAllowed = allowedProfiles.Any(x => x == "*");
        var profileAllowed = wildcardAllowed || allowedProfiles.Any(x => x.Equals(targetProfileId, StringComparison.OrdinalIgnoreCase));
        if (!profileAllowed)
        {
            errors.Add($"Profile mismatch: '{targetProfileId}' is not in allowedProfileIds.");
        }

        var targetHash = SavePatchFieldCodec.ComputeSha256Hex(targetDoc.Raw);
        var sourceHashMatches = string.Equals(pack.Metadata.SourceHash, targetHash, StringComparison.OrdinalIgnoreCase);
        if (!sourceHashMatches)
        {
            warnings.Add("Source hash mismatch: target save differs from patch origin.");
        }

        return Task.FromResult(new SavePatchCompatibilityResult(
            IsCompatible: errors.Count == 0,
            SourceHashMatches: sourceHashMatches,
            TargetHash: targetHash,
            Errors: errors,
            Warnings: warnings));
    }

    public async Task<SavePatchPreview> PreviewApplyAsync(
        SavePatchPack pack,
        SaveDocument targetDoc,
        string targetProfileId,
        CancellationToken cancellationToken = default)
    {
        var schema = await _schemaRepository.LoadSchemaAsync(targetDoc.SchemaId, cancellationToken);
        var compatibility = await ValidateCompatibilityAsync(pack, targetDoc, targetProfileId, cancellationToken);
        var errors = compatibility.Errors.ToList();
        var warnings = compatibility.Warnings.ToList();

        var fieldByPath = schema.FieldDefs
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .ToDictionary(x => x.Path!, x => x, StringComparer.OrdinalIgnoreCase);
        var fieldById = schema.FieldDefs
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        var operations = new List<SavePatchOperation>(pack.Operations.Count);
        foreach (var operation in pack.Operations)
        {
            if (operation.Kind != SavePatchOperationKind.SetValue)
            {
                errors.Add($"Unsupported operation kind '{operation.Kind}' for '{operation.FieldId}'.");
                continue;
            }

            if (operation.NewValue is null)
            {
                errors.Add($"Operation '{operation.FieldId}' is missing required newValue.");
                continue;
            }

            var field = ResolveField(fieldByPath, fieldById, operation, warnings);
            if (field is null)
            {
                errors.Add($"Field not found for operation id='{operation.FieldId}' path='{operation.FieldPath}'.");
                continue;
            }

            var normalizedValue = SavePatchFieldCodec.NormalizePatchValue(operation.NewValue, operation.ValueType);
            var currentValue = SavePatchFieldCodec.ReadFieldValue(targetDoc.Raw, field, schema.Endianness);
            if (SavePatchFieldCodec.ValuesEqual(currentValue, normalizedValue))
            {
                continue;
            }

            operations.Add(operation with { NewValue = normalizedValue });
        }

        return new SavePatchPreview(
            IsCompatible: errors.Count == 0,
            Errors: errors,
            Warnings: warnings,
            OperationsToApply: operations);
    }

    private static IReadOnlyList<(int Start, int End)> BuildChecksumOutputRanges(SaveSchema schema)
        => schema.ChecksumRules
            .Select(x => (Start: x.OutputOffset, End: x.OutputOffset + x.OutputLength))
            .ToArray();

    private static bool FieldOverlapsChecksumOutput(SaveFieldDefinition field, IReadOnlyList<(int Start, int End)> ranges)
    {
        var fieldStart = field.Offset;
        var fieldEnd = field.Offset + field.Length;
        foreach (var (start, end) in ranges)
        {
            if (fieldStart < end && fieldEnd > start)
            {
                return true;
            }
        }

        return false;
    }

    private static SaveFieldDefinition? ResolveField(
        IReadOnlyDictionary<string, SaveFieldDefinition> byPath,
        IReadOnlyDictionary<string, SaveFieldDefinition> byId,
        SavePatchOperation operation,
        List<string> warnings)
    {
        if (byId.TryGetValue(operation.FieldId, out var idField))
        {
            if (!string.IsNullOrWhiteSpace(operation.FieldPath))
            {
                var canonicalPath = idField.Path ?? idField.Id;
                if (!string.Equals(operation.FieldPath, canonicalPath, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(
                        $"Field path mismatch for '{operation.FieldId}': pack='{operation.FieldPath}', schema='{canonicalPath}'. Using fieldId selector.");
                }
            }

            return idField;
        }

        if (!string.IsNullOrWhiteSpace(operation.FieldPath) && byPath.TryGetValue(operation.FieldPath, out var pathField))
        {
            warnings.Add($"FieldId '{operation.FieldId}' not found. Falling back to fieldPath '{operation.FieldPath}'.");
            return pathField;
        }

        return null;
    }

    private static IReadOnlyList<string> ValidatePackContract(SavePatchPack pack)
    {
        var errors = new List<string>();
        if (pack.Metadata is null)
        {
            errors.Add("metadata is required");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(pack.Metadata.SchemaVersion))
        {
            errors.Add("metadata.schemaVersion is required");
        }
        else if (!string.Equals(pack.Metadata.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            errors.Add("metadata.schemaVersion must be '1.0'");
        }

        if (string.IsNullOrWhiteSpace(pack.Metadata.ProfileId))
        {
            errors.Add("metadata.profileId is required");
        }

        if (string.IsNullOrWhiteSpace(pack.Metadata.SchemaId))
        {
            errors.Add("metadata.schemaId is required");
        }

        if (string.IsNullOrWhiteSpace(pack.Metadata.SourceHash))
        {
            errors.Add("metadata.sourceHash is required");
        }

        if (pack.Metadata.CreatedAtUtc == default)
        {
            errors.Add("metadata.createdAtUtc is required");
        }

        if (pack.Compatibility is null)
        {
            errors.Add("compatibility is required");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(pack.Compatibility.RequiredSchemaId))
            {
                errors.Add("compatibility.requiredSchemaId is required");
            }

            if (pack.Compatibility.AllowedProfileIds is null || pack.Compatibility.AllowedProfileIds.Count == 0)
            {
                errors.Add("compatibility.allowedProfileIds must contain at least one value");
            }
        }

        if (pack.Operations is null)
        {
            errors.Add("operations is required");
            return errors;
        }

        for (var i = 0; i < pack.Operations.Count; i++)
        {
            var operation = pack.Operations[i];
            if (string.IsNullOrWhiteSpace(operation.FieldPath))
            {
                errors.Add($"operations[{i}].fieldPath is required");
            }

            if (string.IsNullOrWhiteSpace(operation.FieldId))
            {
                errors.Add($"operations[{i}].fieldId is required");
            }

            if (string.IsNullOrWhiteSpace(operation.ValueType))
            {
                errors.Add($"operations[{i}].valueType is required");
            }

            if (operation.Kind != SavePatchOperationKind.SetValue)
            {
                errors.Add($"operations[{i}].kind must be SetValue");
            }

            if (operation.Offset < 0)
            {
                errors.Add($"operations[{i}].offset must be >= 0");
            }

            if (operation.NewValue is null)
            {
                errors.Add($"operations[{i}].newValue is required");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateRawPackContract(JsonElement root)
    {
        var errors = new List<string>();
        if (!TryGetPropertyIgnoreCase(root, "metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            errors.Add("metadata is required");
            return errors;
        }

        if (!TryGetPropertyIgnoreCase(metadata, "schemaVersion", out _))
        {
            errors.Add("metadata.schemaVersion is required");
        }

        if (!TryGetPropertyIgnoreCase(metadata, "createdAtUtc", out _))
        {
            errors.Add("metadata.createdAtUtc is required");
        }

        if (!TryGetPropertyIgnoreCase(root, "operations", out var operations) || operations.ValueKind != JsonValueKind.Array)
        {
            errors.Add("operations is required");
            return errors;
        }

        var index = 0;
        foreach (var operation in operations.EnumerateArray())
        {
            if (operation.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"operations[{index}] must be an object");
                index++;
                continue;
            }

            foreach (var field in new[] { "kind", "fieldPath", "fieldId", "valueType", "newValue", "offset" })
            {
                if (!TryGetPropertyIgnoreCase(operation, field, out _))
                {
                    errors.Add($"operations[{index}].{field} is required");
                }
            }

            if (TryGetPropertyIgnoreCase(operation, "newValue", out var newValue) &&
                newValue.ValueKind == JsonValueKind.Null)
            {
                errors.Add($"operations[{index}].newValue cannot be null");
            }

            index++;
        }

        return errors;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
