using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Read-only loader for <c>knowledge-base/verified_facts.json</c>. Used by the
/// Phase 9 replay tests to assert every Lua function name a service emits is
/// represented in the verified facts ledger.
/// </summary>
/// <remarks>
/// The lookup is intentionally permissive: it matches a function name against
/// either the entry id (e.g. <c>rva_find_player_wrapper</c>) or any substring
/// of the entry's <c>claim</c>. The ledger does not store function names in a
/// dedicated field, so substring matching is the most stable index. Tests
/// must keep this loader read-only — see the project rule that
/// <c>verified_facts.json</c> is not test-mutable.
/// </remarks>
public static class VerifiedLedgerLookup
{
    private static readonly Lazy<HashSet<string>> _knownFunctionTokens = new(LoadKnownFunctionTokens);

    /// <summary>
    /// Returns the canonical absolute path to <c>verified_facts.json</c>. The
    /// project relies on a fixed swfoc_memory checkout next to the editor, so
    /// the path is hard-coded with an env-var override for CI.
    /// </summary>
    public static string GetLedgerPath()
    {
        var envOverride = Environment.GetEnvironmentVariable("SWFOC_VERIFIED_FACTS");
        if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
        {
            return envOverride;
        }
        return @"C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\verified_facts.json";
    }

    /// <summary>True when the ledger file exists at the expected location.</summary>
    public static bool LedgerAvailable => File.Exists(GetLedgerPath());

    /// <summary>
    /// Returns true when <paramref name="functionName"/> appears in the ledger
    /// either as an exact token, an entry-id substring, or anywhere inside the
    /// concatenated entry text. Comparison is case-insensitive because the
    /// ledger uses snake_case entry ids (e.g. <c>rva_story_event_command_unit_ctor</c>)
    /// while service code uses Pascal_Case Lua names (e.g. <c>Story_Event</c>).
    /// The substring fallback handles the common case where the ledger embeds
    /// the function name inside a larger compound identifier.
    /// </summary>
    public static bool ContainsFunction(string functionName)
    {
        ArgumentNullException.ThrowIfNull(functionName);
        if (functionName.Length == 0) return false;
        var needle = functionName.ToLowerInvariant();
        if (_knownFunctionTokens.Value.Contains(needle)) return true;
        // Substring fallback: many ledger keys are compound identifiers like
        // "rva_story_event_command_unit_ctor" where the bare "story_event"
        // never appears as a standalone token.
        return _ledgerHaystack.Value.Contains(needle, StringComparison.Ordinal);
    }

    private static readonly Lazy<string> _ledgerHaystack = new(LoadLedgerHaystack);

    private static string LoadLedgerHaystack()
    {
        var ledgerPath = GetLedgerPath();
        if (!File.Exists(ledgerPath)) return string.Empty;
        try
        {
            return File.ReadAllText(ledgerPath).ToLowerInvariant();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts every Lua identifier of the form <c>Letter_Or_Underscore[\w]*</c>
    /// from a Lua source string. Used to enumerate every function call inside a
    /// service's BuildLuaCommand output for ledger verification.
    /// </summary>
    public static IReadOnlyList<string> ExtractLuaIdentifiers(string luaSource)
    {
        ArgumentNullException.ThrowIfNull(luaSource);
        // Match identifiers that start with an upper-case letter or `set_`,
        // followed by alphanumerics/underscores. Lower-case `local`/`if`/`then`
        // are filtered explicitly to avoid noise from Lua control flow.
        var matches = Regex.Matches(luaSource, @"\b([A-Z][A-Za-z0-9_]*|set_[A-Za-z0-9_]+)\b");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (Match m in matches)
        {
            var token = m.Value;
            if (LuaKeywordSet.Contains(token)) continue;
            if (seen.Add(token)) ordered.Add(token);
        }
        return ordered;
    }

    /// <summary>
    /// Identifiers that look like type/value names but are not callable engine
    /// bindings — they should not be required to appear in the ledger.
    /// </summary>
    private static readonly HashSet<string> LuaKeywordSet = new(StringComparer.Ordinal)
    {
        "WARNING", "EMPIRE", "REBEL", "UNDERWORLD", "REPUBLIC", "CIS",
        "CORUSCANT", "TRUE", "FALSE",
        // Lua language keywords (capitalized variants caught by the regex are unusual but possible)
        "And", "Or", "Not", "If", "Then", "Else", "End", "Do", "While", "For",
    };

    private static HashSet<string> LoadKnownFunctionTokens()
    {
        var ledgerPath = GetLedgerPath();
        var bag = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(ledgerPath))
        {
            return bag;
        }

        try
        {
            var raw = File.ReadAllText(ledgerPath);
            var doc = JsonNode.Parse(raw) as JsonObject;
            if (doc is null) return bag;

            foreach (var (entryId, entryNode) in doc)
            {
                AddTokensFrom(entryId, bag);
                if (entryNode is JsonObject obj)
                {
                    if (obj.TryGetPropertyValue("claim", out var claimNode) && claimNode is not null)
                    {
                        AddTokensFrom(claimNode.GetValue<string>(), bag);
                    }
                    if (obj.TryGetPropertyValue("notes", out var notesNode) && notesNode is not null)
                    {
                        AddTokensFrom(notesNode.GetValue<string>(), bag);
                    }
                }
            }
        }
        catch (IOException)
        {
            // Best-effort: tests will fall back to stubbed mode if the ledger is unreadable.
        }
        catch (JsonException)
        {
        }
        return bag;
    }

    private static void AddTokensFrom(string source, HashSet<string> sink)
    {
        if (string.IsNullOrEmpty(source)) return;
        foreach (Match m in Regex.Matches(source, @"[A-Za-z][A-Za-z0-9_]*"))
        {
            sink.Add(m.Value.ToLowerInvariant());
        }
    }
}
