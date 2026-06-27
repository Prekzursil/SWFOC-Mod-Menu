using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwfocTrainer.App.V2.Infrastructure;

// ============================================================================
// Lua probe recipe persistence. Recipes are display-name + lua-text pairs that
// the Probes tab surfaces as a quick-pick dropdown. Users can add their own by
// hitting "Save Recipe" on the probes tab.
// ============================================================================

public sealed class V2Recipe
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("lua")]
    public string Lua { get; set; } = string.Empty;

    public V2Recipe() { }

    public V2Recipe(string name, string lua)
    {
        Name = name;
        Lua = lua;
    }

    public override string ToString() => Name;
}

public sealed class V2RecipeStore
{
    /// <summary>
    /// Curated built-in recipes. Always present regardless of whether the user
    /// has a <c>recipes.json</c> yet. Each entry is safe to run against the
    /// current bridge build. The advanced diagnostic helpers gracefully fall
    /// back to a bridge-side error if the deployed <c>powrprof.dll</c> is
    /// older than 2026-04-10 (the build that added them).
    /// </summary>
    public static IReadOnlyList<V2Recipe> BuiltIn { get; } = new List<V2Recipe>
    {
        new("Get version + build info",
            "return SWFOC_GetVersion()..'|'..SWFOC_GetBuildInfo()"),
        new("List registered helpers",
            "return SWFOC_DiagListRegisteredFunctions()"),
        new("Give 10M credits to local player",
            "return SWFOC_SetCredits(10000000)"),
        new("Max tech level (5)",
            "return SWFOC_SetTechLevel(5)"),
        new("Reveal map (FoW)",
            "FOWManager.Reveal_All() return \"done\""),
        new("Get local player slot + faction",
            "local s,f = SWFOC_GetLocalPlayer() return tostring(s)..'|'..tostring(f)"),
        new("Self-test",
            "return SWFOC_DiagSelfTest()"),
        new("Probe SWFOC_BatchTypeExists (vanilla 3 factions)",
            "return SWFOC_BatchTypeExists(\"REBEL_INFANTRY|EMPIRE_AT_AT|UNDERWORLD_FRIGATE\")"),
    }.AsReadOnly();

    public static List<V2Recipe> LoadAll()
    {
        var combined = new List<V2Recipe>(BuiltIn);

        try
        {
            if (!File.Exists(V2Settings.RecipesFilePath))
            {
                return combined;
            }

            var json = File.ReadAllText(V2Settings.RecipesFilePath);
            var user = JsonSerializer.Deserialize<List<V2Recipe>>(json);
            if (user is not null)
            {
                combined.AddRange(user);
            }
        }
        catch (IOException)
        {
            // Fall through with built-ins only.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        return combined;
    }

    public static bool TryAppend(V2Recipe recipe, out string? error)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        try
        {
            Directory.CreateDirectory(V2Settings.AppDataDirectory);
            List<V2Recipe> existing;
            if (File.Exists(V2Settings.RecipesFilePath))
            {
                var json = File.ReadAllText(V2Settings.RecipesFilePath);
                existing = JsonSerializer.Deserialize<List<V2Recipe>>(json) ?? new();
            }
            else
            {
                existing = new();
            }

            existing.Add(recipe);
            var payload = JsonSerializer.Serialize(
                existing,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(V2Settings.RecipesFilePath, payload);
            error = null;
            return true;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
