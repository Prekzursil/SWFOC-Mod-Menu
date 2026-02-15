using System.Text.Json;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;

namespace SwfocTrainer.Saves.Internal;

internal sealed class SaveSchemaRepository
{
    private readonly SaveOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public SaveSchemaRepository(SaveOptions options)
    {
        _options = options;
    }

    public async Task<SaveSchema> LoadSchemaAsync(string schemaId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_options.SchemaRootPath, $"{schemaId}.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Save schema not found: {path}");
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var schema = JsonSerializer.Deserialize<SaveSchema>(json, _jsonOptions);
        return schema ?? throw new InvalidDataException($"Failed to deserialize schema '{schemaId}'");
    }
}
