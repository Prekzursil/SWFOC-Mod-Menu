using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public static class ProfileMetadataParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<SymbolValidationRule> ParseSymbolValidationRules(TrainerProfile profile)
    {
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue("symbolValidationRules", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<SymbolValidationRule>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<SymbolValidationRule>>(raw, JsonOptions);
            return parsed is not null
                ? parsed
                : Array.Empty<SymbolValidationRule>();
        }
        catch (JsonException)
        {
            return Array.Empty<SymbolValidationRule>();
        }
    }

    public static HashSet<string> ParseCriticalSymbolSet(TrainerProfile profile)
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue("criticalSymbols", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return symbols;
        }

        foreach (var symbol in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            symbols.Add(symbol);
        }

        return symbols;
    }
}
