namespace SwfocTrainer.Core.Models;

public sealed record SaveSchema(
    string SchemaId,
    string GameBuild,
    string Endianness,
    IReadOnlyList<SaveBlockDefinition> RootBlocks,
    IReadOnlyList<SaveFieldDefinition> FieldDefs,
    IReadOnlyList<SaveArrayDefinition> ArrayDefs,
    IReadOnlyList<ValidationRule> ValidationRules,
    IReadOnlyList<ChecksumRule> ChecksumRules);

public sealed record SaveBlockDefinition(
    string Id,
    string Name,
    int Offset,
    int Length,
    string Type,
    IReadOnlyList<string>? Fields = null,
    IReadOnlyList<string>? Children = null);

public sealed record SaveFieldDefinition(
    string Id,
    string Name,
    string ValueType,
    int Offset,
    int Length,
    string? Description = null,
    string? Path = null);

public sealed record SaveArrayDefinition(
    string Id,
    string Name,
    string ElementType,
    int Offset,
    int Count,
    int Stride,
    string? Path = null);

public sealed record ValidationRule(
    string Id,
    string Rule,
    string Target,
    string Message,
    string Severity = "error");

public sealed record ChecksumRule(
    string Id,
    string Algorithm,
    int StartOffset,
    int EndOffset,
    int OutputOffset,
    int OutputLength);

public sealed record SaveNode(
    string Path,
    string Name,
    string ValueType,
    object? Value,
    IReadOnlyList<SaveNode>? Children = null);

public sealed record SaveDocument(
    string Path,
    string SchemaId,
    byte[] Raw,
    SaveNode Root);

public sealed record SaveValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
